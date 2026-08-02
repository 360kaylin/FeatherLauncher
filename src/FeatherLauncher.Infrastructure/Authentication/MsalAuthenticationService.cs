using System.Threading.Channels;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;

namespace FeatherLauncher.Infrastructure.Authentication;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class MsalClientAdapter : IMsalClient
{
    private readonly IPublicClientApplication application;
    private readonly ILogger logger;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;
    public MsalClientAdapter(AuthenticationConfiguration configuration, ISecureTokenStorage storage, ILogger logger)
    {
        this.logger = logger;
        var builder = PublicClientApplicationBuilder.Create(configuration.ClientId).WithAuthority(configuration.Authority);
        if (configuration.RedirectUri is not null) builder = builder.WithRedirectUri(configuration.RedirectUri.AbsoluteUri);
        application = builder.Build();
        application.UserTokenCache.SetBeforeAccessAsync(async args => { logger.LogInformation("Authentication token cache read started"); var value = await storage.RetrieveAsync("msal-token-cache"); if (value is not null) args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(value)); logger.LogInformation("Authentication token cache read completed; cache state: {CacheState}", value is null ? "empty" : "restored"); });
        application.UserTokenCache.SetAfterAccessAsync(async args => { logger.LogInformation("Authentication token cache callback invoked; state changed: {StateChanged}", args.HasStateChanged); if (args.HasStateChanged) { await storage.StoreAsync("msal-token-cache", Convert.ToBase64String(args.TokenCache.SerializeMsalV3())); logger.LogInformation("Authentication token cache write completed"); } });
    }
    public async Task<MicrosoftTokenResult> SignInAsync(IReadOnlyList<string> scopes, bool deviceCode, CancellationToken cancellationToken)
    {
        logger.LogInformation("Microsoft authentication request started; flow: {Flow}", deviceCode ? "device-code" : "system-browser");
        AuthenticationResult result;
        if (deviceCode) result = await application.AcquireTokenWithDeviceCode(scopes, code => { DeviceCodeReceived?.Invoke(this, new(code.VerificationUrl, code.UserCode, code.ExpiresOn)); return Task.CompletedTask; }).ExecuteAsync(cancellationToken);
        else result = await application.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false).ExecuteAsync(cancellationToken);
        logger.LogInformation("Microsoft authentication result received");
        var converted = ToResult(result);
        logger.LogInformation("Microsoft authentication completion callback completed");
        return converted;
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
    private readonly object gate = new(); private readonly List<Channel<AccountState>> observers = []; private CancellationTokenSource? active; private long generation; private string? minecraftToken; private AccountState currentState = new SignedOutAccount();
    private readonly ILogger logger;
    public AccountState CurrentState { get { lock (gate) return currentState; } }
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;
    public MsalAuthenticationService(AuthenticationConfiguration configuration, ISecureTokenStorage storage, IXboxAuthenticationService xbox, IXstsAuthorizationService xsts, IMinecraftAuthenticationService minecraft, IMinecraftEntitlementService entitlement, IMinecraftProfileService profiles)
        : this(configuration, storage, xbox, xsts, minecraft, entitlement, profiles, logger: NullLogger<MsalAuthenticationService>.Instance) { }
    public MsalAuthenticationService(AuthenticationConfiguration configuration, ISecureTokenStorage storage, IXboxAuthenticationService xbox, IXstsAuthorizationService xsts, IMinecraftAuthenticationService minecraft, IMinecraftEntitlementService entitlement, IMinecraftProfileService profiles, ILogger<MsalAuthenticationService> logger)
        : this(configuration, storage, xbox, xsts, minecraft, entitlement, profiles, new MsalClientAdapter(configuration, storage, logger), new SystemClock(), logger) { }
    public MsalAuthenticationService(AuthenticationConfiguration configuration, ISecureTokenStorage storage, IXboxAuthenticationService xbox, IXstsAuthorizationService xsts, IMinecraftAuthenticationService minecraft, IMinecraftEntitlementService entitlement, IMinecraftProfileService profiles, IMsalClient msal, IClock clock, ILogger? logger = null)
    {
        if (!configuration.IsConfigured) throw new AuthenticationException(AuthenticationFailureCategory.ConfigurationMissing, "Microsoft sign-in is not configured yet.", false);
        this.configuration = configuration; this.storage = storage; this.xbox = xbox; this.xsts = xsts; this.minecraft = minecraft; this.entitlement = entitlement; this.profiles = profiles; this.msal = msal; this.clock = clock; this.logger = logger ?? NullLogger.Instance;
        msal.DeviceCodeReceived += OnDeviceCode;
    }
    private void OnDeviceCode(object? sender, DeviceCodeInfo code) { if (code.ExpiresAt > clock.UtcNow) DeviceCodeReceived?.Invoke(this, code); }
    public Task BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource source; long operation; lock (gate) { if (active is not null) throw new InvalidOperationException("A sign-in is already in progress."); active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); source = active; operation = ++generation; }
        logger.LogInformation("Authentication operation started; generation accepted"); Publish(operation, new SigningInAccount(clock.UtcNow)); return RunAsync(operation, source, false);
    }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource source; long operation; lock (gate) { if (active is not null) throw new InvalidOperationException("An authentication operation is already in progress."); active = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); source = active; operation = ++generation; }
        await RunAsync(operation, source, true);
    }
    private async Task RunAsync(long operation, CancellationTokenSource source, bool refresh)
    {
        try { var result = refresh ? await msal.RefreshAsync(configuration.RequiredScopes, source.Token) : await msal.SignInAsync(configuration.RequiredScopes, configuration.UseDeviceCode, source.Token); logger.LogInformation("Coordinator received Microsoft authentication result; cancellation requested: {CancellationRequested}", source.IsCancellationRequested); if (result.ExpiresAt <= clock.UtcNow) throw new AuthenticationException(AuthenticationFailureCategory.TokenExpired, "The Microsoft session expired. Please sign in again."); await CompletePipelineAsync(operation, result, source.Token); logger.LogInformation("Authentication pipeline completed"); }
        catch (OperationCanceledException) { logger.LogInformation("Authentication operation cancelled"); Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UserCancelled.ToString(), "Sign-in was cancelled.", true)); }
        catch (AuthenticationException ex) { logger.LogInformation("Authentication pipeline failed; category: {Category}", ex.Category); Publish(operation, new AuthenticationFailed(ex.Category.ToString(), ex.Message, ex.Recoverable)); }
        catch (MsalServiceException ex) when (ex.ErrorCode is "authorization_declined" or "access_denied") { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UserDenied.ToString(), "Microsoft sign-in was denied.", true)); }
        catch (MsalServiceException ex) when (ex.ErrorCode is "code_expired" or "expired_token") { Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.DeviceCodeExpired.ToString(), "The device code expired. Start sign-in again.", true)); }
        catch (MsalException) { logger.LogInformation("Authentication pipeline failed; category: MicrosoftAuthenticationFailed"); Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.MicrosoftAuthenticationFailed.ToString(), "Microsoft authentication failed.", true)); }
        catch (HttpRequestException) { logger.LogInformation("Authentication pipeline failed; category: NetworkUnavailable"); Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.NetworkUnavailable.ToString(), "The authentication service could not be reached.", true)); }
        catch (Exception) { logger.LogInformation("Authentication pipeline failed; category: UnknownFailure"); Publish(operation, new AuthenticationFailed(AuthenticationFailureCategory.UnknownFailure.ToString(), "Authentication failed safely.", true)); }
        finally { lock (gate) { if (active == source) { active.Dispose(); active = null; } } }
    }
    private async Task CompletePipelineAsync(long operation, MicrosoftTokenResult result, CancellationToken token)
    {
        Publish(operation, new MicrosoftAuthenticatedAccount()); logger.LogInformation("Authentication stage started: Xbox Live"); var xr = await xbox.AuthenticateAsync(result.AccessToken, token); Publish(operation, new XboxAuthenticatedAccount()); logger.LogInformation("Authentication stage started: XSTS"); var xs = await xsts.AuthorizeAsync(xr.Token, token); Publish(operation, new XstsAuthenticatedAccount()); logger.LogInformation("Authentication stage started: Minecraft"); var mc = await minecraft.AuthenticateAsync(xs.UserHash, xs.Token, token); minecraftToken = mc.AccessToken; Publish(operation, new MinecraftAuthenticatedAccount()); logger.LogInformation("Authentication stage started: Entitlement"); var owned = await entitlement.VerifyAsync(mc.AccessToken, token); if (!owned.OwnsMinecraft) throw new AuthenticationException(AuthenticationFailureCategory.MinecraftNotOwned, "This account does not have a confirmed Minecraft: Java Edition entitlement.", false); Publish(operation, new OwnershipConfirmedAccount()); logger.LogInformation("Authentication stage started: Profile"); var profile = await profiles.GetAsync(mc.AccessToken, token); Publish(operation, new ProfileLoadedAccount(profile)); Publish(operation, new SignedInAccount(new MicrosoftIdentity("local-session", new AccountDisplayInfo("Microsoft account")), profile, owned, new TokenExpiry(mc.ExpiresAt)));
    }
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? current; lock (gate) { generation++; current = active; active = null; }
        current?.Cancel(); PublishUnconditionally(new SigningOutAccount()); minecraftToken = null;
        AuthenticationException? failure = null; try { await msal.RemoveAccountsAsync(cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { failure = new(AuthenticationFailureCategory.SecureStorageUnavailable, "The local Microsoft session could not be completely removed.", true); }
        try { await storage.DeleteAsync(CacheKey, cancellationToken); } catch (Exception ex) when (ex is not OperationCanceledException) { failure = new(AuthenticationFailureCategory.SecureStorageUnavailable, "Secure credential storage could not be cleared.", true, ex); }
        PublishUnconditionally(new SignedOutAccount()); current?.Dispose(); if (failure is not null) throw failure;
    }
    public async IAsyncEnumerable<AccountState> ObserveAccountStateAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<AccountState>(); lock (gate) { observers.Add(channel); channel.Writer.TryWrite(currentState); }
        try { await foreach (var state in channel.Reader.ReadAllAsync(cancellationToken)) yield return state; }
        finally { lock (gate) observers.Remove(channel); }
    }
    private void Publish(long operation, AccountState state) { lock (gate) { if (operation != generation) { logger.LogInformation("Authentication state rejected because generation is stale"); return; } PublishLocked(state); } }
    private void PublishUnconditionally(AccountState state) { lock (gate) PublishLocked(state); }
    private void PublishLocked(AccountState state) { currentState = state; logger.LogInformation("Authentication state transition: {State}", state.GetType().Name); foreach (var observer in observers) observer.Writer.TryWrite(state); }
    public void Dispose() { msal.DeviceCodeReceived -= OnDeviceCode; active?.Cancel(); active?.Dispose(); }
}
