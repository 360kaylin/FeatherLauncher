using System.Threading.Channels;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using Microsoft.Identity.Client;

namespace FeatherLauncher.Infrastructure.Authentication;

public sealed class MsalAuthenticationService : IMicrosoftAuthenticationService, IDisposable
{
    private const string CacheKey = "msal-token-cache";
    private readonly AuthenticationConfiguration configuration;
    private readonly ISecureTokenStorage storage;
    private readonly IXboxAuthenticationService xbox;
    private readonly IXstsAuthorizationService xsts;
    private readonly IMinecraftAuthenticationService minecraft;
    private readonly IMinecraftEntitlementService entitlement;
    private readonly IMinecraftProfileService profiles;
    private readonly IPublicClientApplication application;
    private readonly Channel<AccountState> states = Channel.CreateUnbounded<AccountState>();
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private long generation;
    private string? minecraftToken;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;

    public MsalAuthenticationService(AuthenticationConfiguration configuration, ISecureTokenStorage storage, IXboxAuthenticationService xbox, IXstsAuthorizationService xsts, IMinecraftAuthenticationService minecraft, IMinecraftEntitlementService entitlement, IMinecraftProfileService profiles)
    {
        this.configuration = configuration; this.storage = storage; this.xbox = xbox; this.xsts = xsts; this.minecraft = minecraft; this.entitlement = entitlement; this.profiles = profiles;
        if (!configuration.IsConfigured) throw new AuthenticationException(AuthenticationFailureCategory.ConfigurationMissing, "Microsoft sign-in is not configured yet.", false);
        var builder = PublicClientApplicationBuilder.Create(configuration.ClientId).WithAuthority(configuration.Authority);
        if (configuration.RedirectUri is not null) builder = builder.WithRedirectUri(configuration.RedirectUri.AbsoluteUri);
        application = builder.Build(); application.UserTokenCache.SetBeforeAccessAsync(BeforeCacheAccessAsync); application.UserTokenCache.SetAfterAccessAsync(AfterCacheAccessAsync);
        states.Writer.TryWrite(new SignedOutAccount());
    }

    public Task BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource source; long operation;
        lock (gate) { if (active is not null) throw new InvalidOperationException("A sign-in is already in progress."); active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); source = active; operation = ++generation; }
        states.Writer.TryWrite(new SigningInAccount(DateTimeOffset.UtcNow));
        return RunSignInAsync(operation, source);
    }

    private async Task RunSignInAsync(long operation, CancellationTokenSource source)
    {
        try
        {
            AuthenticationResult microsoftResult;
            if (configuration.UseDeviceCode)
                microsoftResult = await application.AcquireTokenWithDeviceCode(configuration.RequiredScopes, code => { DeviceCodeReceived?.Invoke(this, new(code.VerificationUrl, code.UserCode, code.ExpiresOn)); return Task.CompletedTask; }).ExecuteAsync(source.Token);
            else
                microsoftResult = await application.AcquireTokenInteractive(configuration.RequiredScopes).WithUseEmbeddedWebView(false).ExecuteAsync(source.Token);
            await CompletePipelineAsync(operation, microsoftResult, source.Token);
        }
        catch (OperationCanceledException) { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UserCancelled.ToString(), "Sign-in was cancelled.", true)); }
        catch (MsalServiceException ex) when (ex.ErrorCode is "authorization_declined" or "access_denied") { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UserDenied.ToString(), "Microsoft sign-in was denied.", true)); }
        catch (MsalException ex) { Publish(operation, Failure(AuthenticationFailureCategory.MicrosoftAuthenticationFailed, "Microsoft authentication failed.", ex)); }
        catch (AuthenticationException ex) { Publish(operation, new AuthenticationFailed(ex.Category.ToString(), ex.Message, ex.Recoverable)); }
        catch (Exception ex) { Publish(operation, Failure(AuthenticationFailureCategory.UnknownFailure, "Authentication failed safely.", ex)); }
        finally { lock (gate) { if (active == source) { active.Dispose(); active = null; } } }
    }

    private async Task CompletePipelineAsync(long operation, AuthenticationResult result, CancellationToken token)
    {
        Publish(operation, new MicrosoftAuthenticatedAccount()); var xboxResult = await xbox.AuthenticateAsync(result.AccessToken, token);
        Publish(operation, new XboxAuthenticatedAccount()); var xstsResult = await xsts.AuthorizeAsync(xboxResult.Token, token);
        var minecraftResult = await minecraft.AuthenticateAsync(xstsResult.UserHash, xstsResult.Token, token); minecraftToken = minecraftResult.AccessToken;
        Publish(operation, new MinecraftAuthenticatedAccount()); var owned = await entitlement.VerifyAsync(minecraftResult.AccessToken, token);
        if (!owned.OwnsMinecraft) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftNotOwned, "This account does not have a confirmed Minecraft: Java Edition entitlement.", false);
        Publish(operation, new OwnershipConfirmedAccount()); var profile = await profiles.GetAsync(minecraftResult.AccessToken, token);
        Publish(operation, new ProfileLoadedAccount(profile));
        var identity = new MicrosoftIdentity("local-session", new AccountDisplayInfo("Microsoft account"));
        Publish(operation, new SignedInAccount(identity, profile, owned, new TokenExpiry(minecraftResult.ExpiresAt)));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await application.GetAccountsAsync(); var account = accounts.FirstOrDefault() ?? throw new AuthenticationException(AuthenticationFailureCategory.TokenRevoked, "The Microsoft session is no longer available.");
        try { var result = await application.AcquireTokenSilent(configuration.RequiredScopes, account).ExecuteAsync(cancellationToken); var operation = Interlocked.Read(ref generation); await CompletePipelineAsync(operation, result, cancellationToken); }
        catch (MsalUiRequiredException ex) { throw new AuthenticationException(AuthenticationFailureCategory.TokenRevoked, "Microsoft requires you to sign in again.", true, ex); }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? current; lock (gate) { generation++; current = active; active = null; }
        current?.Cancel(); current?.Dispose();
        states.Writer.TryWrite(new SigningOutAccount()); minecraftToken = null;
        foreach (var account in await application.GetAccountsAsync()) await application.RemoveAsync(account);
        try { await storage.DeleteAsync(CacheKey, cancellationToken); } catch (PlatformNotSupportedException ex) { throw new AuthenticationException(AuthenticationFailureCategory.SecureStorageUnavailable, "Secure credential storage is unavailable.", true, ex); }
        states.Writer.TryWrite(new SignedOutAccount());
    }

    public IAsyncEnumerable<AccountState> ObserveAccountStateAsync(CancellationToken cancellationToken = default) => states.Reader.ReadAllAsync(cancellationToken);
    private void Publish(long operation, AccountState state) { if (operation == Interlocked.Read(ref generation)) states.Writer.TryWrite(state); }
    private static AuthenticationFailed Failure(AuthenticationFailureCategory category, string message, Exception _) => new(category.ToString(), message, true);
    private async Task BeforeCacheAccessAsync(TokenCacheNotificationArgs args) { try { var value = await storage.RetrieveAsync(CacheKey); if (value is not null) args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(value)); } catch (PlatformNotSupportedException) { } }
    private async Task AfterCacheAccessAsync(TokenCacheNotificationArgs args) { if (args.HasStateChanged) try { await storage.StoreAsync(CacheKey, Convert.ToBase64String(args.TokenCache.SerializeMsalV3())); } catch (PlatformNotSupportedException ex) { throw new AuthenticationException(AuthenticationFailureCategory.SecureStorageUnavailable, "Secure credential storage is unavailable.", true, ex); } }
    public void Dispose() { active?.Cancel(); active?.Dispose(); }
}
