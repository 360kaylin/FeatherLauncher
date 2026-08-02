using System.Threading.Channels;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using Microsoft.Identity.Client;

namespace FeatherLauncher.Infrastructure.Authentication;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class MsalClientAdapter : IMsalClient
{
    private readonly IPublicClientApplication application;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;
    public MsalClientAdapter(AuthenticationConfiguration configuration, ISecureTokenStorage storage)
    {
        var builder = PublicClientApplicationBuilder.Create(configuration.ClientId).WithAuthority(configuration.Authority);
        if (configuration.RedirectUri is not null) builder = builder.WithRedirectUri(configuration.RedirectUri.AbsoluteUri);
        application = builder.Build();
        application.UserTokenCache.SetBeforeAccessAsync(async args => { var value = await storage.RetrieveAsync("msal-token-cache"); if (value is not null) args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(value)); });
        application.UserTokenCache.SetAfterAccessAsync(async args => { if (args.HasStateChanged) await storage.StoreAsync("msal-token-cache", Convert.ToBase64String(args.TokenCache.SerializeMsalV3())); });
    }
    public async Task<MicrosoftTokenResult> SignInAsync(IReadOnlyList<string> scopes, bool deviceCode, CancellationToken cancellationToken)
    {
        AuthenticationResult result;
        if (deviceCode) result = await application.AcquireTokenWithDeviceCode(scopes, code => { DeviceCodeReceived?.Invoke(this, new(code.VerificationUrl, code.UserCode, code.ExpiresOn)); return Task.CompletedTask; }).ExecuteAsync(cancellationToken);
        else result = await application.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false).ExecuteAsync(cancellationToken);
        return ToResult(result);
    }
    public async Task<MicrosoftTokenResult> RefreshAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        var accounts = (await application.GetAccountsAsync()).ToArray();
        if (accounts.Length == 0) throw new AuthenticationException(AuthenticationFailureCategory.TokenRevoked, "No cached Microsoft session is available.");
        if (accounts.Length > 1) throw new AuthenticationException(AuthenticationFailureCategory.MicrosoftAuthenticationFailed, "More than one cached Microsoft account was found. Please switch accounts.");
        try { return ToResult(await application.AcquireTokenSilent(scopes, accounts[0]).ExecuteAsync(cancellationToken)); }
        catch (MsalUiRequiredException ex) { throw new AuthenticationException(AuthenticationFailureCategory.TokenRevoked, "Microsoft requires you to sign in again.", true, ex); }
    }
    public async Task RemoveAccountsAsync(CancellationToken cancellationToken) { foreach (var account in await application.GetAccountsAsync()) { cancellationToken.ThrowIfCancellationRequested(); await application.RemoveAsync(account); } }
    private static MicrosoftTokenResult ToResult(AuthenticationResult result) => new(result.AccessToken, result.Account?.HomeAccountId.Identifier ?? "session", result.ExpiresOn);
}

public sealed class MsalAuthenticationService : IMicrosoftAuthenticationService, IDisposable
{
    private const string CacheKey = "msal-token-cache";
    private readonly AuthenticationConfiguration configuration; private readonly ISecureTokenStorage storage; private readonly IMsalClient msal; private readonly IClock clock;
    private readonly IXboxAuthenticationService xbox; private readonly IXstsAuthorizationService xsts; private readonly IMinecraftAuthenticationService minecraft; private readonly IMinecraftEntitlementService entitlement; private readonly IMinecraftProfileService profiles;
    private readonly Channel<AccountState> states = Channel.CreateUnbounded<AccountState>(); private readonly object gate = new(); private CancellationTokenSource? active; private long generation; private string? minecraftToken;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;
    public MsalAuthenticationService(AuthenticationConfiguration configuration, ISecureTokenStorage storage, IXboxAuthenticationService xbox, IXstsAuthorizationService xsts, IMinecraftAuthenticationService minecraft, IMinecraftEntitlementService entitlement, IMinecraftProfileService profiles)
        : this(configuration, storage, xbox, xsts, minecraft, entitlement, profiles, new MsalClientAdapter(configuration, storage), new SystemClock()) { }
    public MsalAuthenticationService(AuthenticationConfiguration configuration, ISecureTokenStorage storage, IXboxAuthenticationService xbox, IXstsAuthorizationService xsts, IMinecraftAuthenticationService minecraft, IMinecraftEntitlementService entitlement, IMinecraftProfileService profiles, IMsalClient msal, IClock clock)
    {
        if (!configuration.IsConfigured) throw new AuthenticationException(AuthenticationFailureCategory.ConfigurationMissing, "Microsoft sign-in is not configured yet.", false);
        this.configuration = configuration; this.storage = storage; this.xbox = xbox; this.xsts = xsts; this.minecraft = minecraft; this.entitlement = entitlement; this.profiles = profiles; this.msal = msal; this.clock = clock;
        msal.DeviceCodeReceived += OnDeviceCode; states.Writer.TryWrite(new SignedOutAccount());
    }
    private void OnDeviceCode(object? sender, DeviceCodeInfo code) { if (code.ExpiresAt > clock.UtcNow) DeviceCodeReceived?.Invoke(this, code); }
    public Task BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource source; long operation; lock (gate) { if (active is not null) throw new InvalidOperationException("A sign-in is already in progress."); active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); source = active; operation = ++generation; }
        states.Writer.TryWrite(new SigningInAccount(clock.UtcNow)); return RunAsync(operation, source, false);
    }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource source; long operation; lock (gate) { if (active is not null) throw new InvalidOperationException("An authentication operation is already in progress."); active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); source = active; operation = ++generation; }
        await RunAsync(operation, source, true);
    }
    private async Task RunAsync(long operation, CancellationTokenSource source, bool refresh)
    {
        try { var result = refresh ? await msal.RefreshAsync(configuration.RequiredScopes, source.Token) : await msal.SignInAsync(configuration.RequiredScopes, configuration.UseDeviceCode, source.Token); if (result.ExpiresAt <= clock.UtcNow) throw new AuthenticationException(AuthenticationFailureCategory.TokenExpired, "The Microsoft session expired. Please sign in again."); await CompletePipelineAsync(operation, result, source.Token); }
        catch (OperationCanceledException) { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UserCancelled.ToString(), "Sign-in was cancelled.", true)); }
        catch (AuthenticationException ex) { Publish(operation, new AuthenticationFailed(ex.Category.ToString(), ex.Message, ex.Recoverable)); }
        catch (MsalServiceException ex) when (ex.ErrorCode is "authorization_declined" or "access_denied") { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UserDenied.ToString(), "Microsoft sign-in was denied.", true)); }
        catch (MsalServiceException ex) when (ex.ErrorCode is "code_expired" or "expired_token") { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.DeviceCodeExpired.ToString(), "The device code expired. Start sign-in again.", true)); }
        catch (MsalException) { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.MicrosoftAuthenticationFailed.ToString(), "Microsoft authentication failed.", true)); }
        catch (HttpRequestException) { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.NetworkUnavailable.ToString(), "The authentication service could not be reached.", true)); }
        catch (Exception) { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UnknownFailure.ToString(), "Authentication failed safely.", true)); }
        finally { lock (gate) { if (active == source) { active.Dispose(); active = null; } } }
    }
    private async Task CompletePipelineAsync(long operation, MicrosoftTokenResult result, CancellationToken token)
    {
        Publish(operation, new MicrosoftAuthenticatedAccount()); var xr = await xbox.AuthenticateAsync(result.AccessToken, token); Publish(operation, new XboxAuthenticatedAccount()); var xs = await xsts.AuthorizeAsync(xr.Token, token); var mc = await minecraft.AuthenticateAsync(xs.UserHash, xs.Token, token); minecraftToken = mc.AccessToken; Publish(operation, new MinecraftAuthenticatedAccount()); var owned = await entitlement.VerifyAsync(mc.AccessToken, token); if (!owned.OwnsMinecraft) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftNotOwned, "This account does not have a confirmed Minecraft: Java Edition entitlement.", false); Publish(operation, new OwnershipConfirmedAccount()); var profile = await profiles.GetAsync(mc.AccessToken, token); Publish(operation, new ProfileLoadedAccount(profile)); Publish(operation, new SignedInAccount(new MicrosoftIdentity("local-session", new AccountDisplayInfo("Microsoft account")), profile, owned, new TokenExpiry(mc.ExpiresAt)));
    }
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? current; lock (gate) { generation++; current = active; active = null; }
        current?.Cancel(); states.Writer.TryWrite(new SigningOutAccount()); minecraftToken = null;
        AuthenticationException? failure = null; try { await msal.RemoveAccountsAsync(cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { failure = new(AuthenticationFailureCategory.SecureStorageUnavailable, "The local Microsoft session could not be completely removed.", true); }
        try { await storage.DeleteAsync(CacheKey, cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { failure = new(AuthenticationFailureCategory.SecureStorageUnavailable, "Secure credential storage could not be cleared.", true, ex); }
        states.Writer.TryWrite(new SignedOutAccount()); current?.Dispose(); if (failure is not null) throw failure;
    }
    public IAsyncEnumerable<AccountState> ObserveAccountStateAsync(CancellationToken cancellationToken = default) => states.Reader.ReadAllAsync(cancellationToken);
    private void Publish(long operation, AccountState state) { if (operation == Interlocked.Read(ref generation)) states.Writer.TryWrite(state); }
    public void Dispose() { msal.DeviceCodeReceived -= OnDeviceCode; active?.Cancel(); active?.Dispose(); }
}
