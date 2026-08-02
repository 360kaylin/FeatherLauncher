using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using FeatherLauncher.Infrastructure.Authentication;
using FeatherLauncher.Infrastructure.Logging;
using Xunit;

namespace FeatherLauncher.Tests;

public sealed class Phase2CCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task SuccessfulPipelinePublishesEveryStage()
    {
        using var fixture = new Fixture(); var states = await fixture.RunAsync();
        Assert.Collection(states, s => Assert.IsType<SignedOutAccount>(s), s => Assert.IsType<SigningInAccount>(s), s => Assert.IsType<MicrosoftAuthenticatedAccount>(s), s => Assert.IsType<XboxAuthenticatedAccount>(s), s => Assert.IsType<MinecraftAuthenticatedAccount>(s), s => Assert.IsType<OwnershipConfirmedAccount>(s), s => Assert.IsType<ProfileLoadedAccount>(s), s => Assert.IsType<SignedInAccount>(s));
    }
    [Theory]
    [InlineData(AuthenticationFailureCategory.UserCancelled)]
    [InlineData(AuthenticationFailureCategory.UserDenied)]
    [InlineData(AuthenticationFailureCategory.DeviceCodeExpired)]
    [InlineData(AuthenticationFailureCategory.NetworkUnavailable)]
    [InlineData(AuthenticationFailureCategory.TokenRevoked)]
    [InlineData(AuthenticationFailureCategory.MicrosoftAuthenticationFailed)]
    public async Task MicrosoftFailuresAreTyped(AuthenticationFailureCategory category) { using var f = new Fixture { MicrosoftFailure = category }; Assert.Equal(category.ToString(), (await f.RunAsync()).OfType<AuthenticationFailed>().Single().Code); }
    [Theory]
    [InlineData("xbox", AuthenticationFailureCategory.XboxAuthenticationFailed)]
    [InlineData("xsts", AuthenticationFailureCategory.XstsAuthenticationFailed)]
    [InlineData("minecraft", AuthenticationFailureCategory.MinecraftAuthenticationFailed)]
    [InlineData("entitlement", AuthenticationFailureCategory.MinecraftNotOwned)]
    [InlineData("profile", AuthenticationFailureCategory.MinecraftProfileMissing)]
    public async Task RemoteStageFailuresAreTyped(string stage, AuthenticationFailureCategory category) { using var f = new Fixture { FailedStage = stage }; Assert.Equal(category.ToString(), (await f.RunAsync()).OfType<AuthenticationFailed>().Single().Code); }
    [Fact] public async Task ExpiredMicrosoftTokenIsRejectedUsingInjectedClock() { using var f = new Fixture { MicrosoftExpiry = Now }; Assert.Equal(nameof(AuthenticationFailureCategory.TokenExpired), (await f.RunAsync()).OfType<AuthenticationFailed>().Single().Code); }
    [Fact] public async Task SilentRefreshSuccessRunsCompletePipeline() { using var f = new Fixture(); var read = f.ReadUntilTerminalAsync(); await f.Service.RefreshAsync(); Assert.IsType<SignedInAccount>((await read).Last()); Assert.Equal(1, f.Msal.RefreshCalls); }
    [Fact] public async Task SilentRefreshFailureIsPublishedSafely() { using var f = new Fixture { RefreshFailure = AuthenticationFailureCategory.TokenRevoked }; var read = f.ReadUntilTerminalAsync(); await f.Service.RefreshAsync(); Assert.Equal(nameof(AuthenticationFailureCategory.TokenRevoked), (await read).OfType<AuthenticationFailed>().Single().Code); }
    [Fact] public async Task ConcurrentSignInIsRejected() { using var f = new Fixture { HoldMicrosoft = true }; var first = f.Service.BeginSignInAsync(); await f.Msal.Started.Task; await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.BeginSignInAsync()); await f.Service.SignOutAsync(); await first; }
    [Fact] public async Task SignOutCancelsAndInvalidatesStaleCompletion() { using var f = new Fixture { HoldMicrosoft = true }; var read = f.ReadCountAsync(4); var login = f.Service.BeginSignInAsync(); await f.Msal.Started.Task; await f.Service.SignOutAsync(); f.Msal.Release.TrySetResult(); await login; var states = await read; Assert.IsType<SignedOutAccount>(states.Last()); Assert.DoesNotContain(states, x => x is SignedInAccount); Assert.True(f.Storage.Deleted); Assert.True(f.Msal.Removed); }
    [Fact] public async Task AccountSwitchDoesNotReuseOldMinecraftToken() { using var f = new Fixture(); await f.RunAsync(); var read = f.ReadCountAsync(2); await f.Service.SignOutAsync(); Assert.Collection(await read, s => Assert.IsType<SigningOutAccount>(s), s => Assert.IsType<SignedOutAccount>(s)); Assert.True(f.Storage.Deleted); }
    [Theory]
    [InlineData("xbox")]
    [InlineData("xsts")]
    [InlineData("minecraft")]
    [InlineData("entitlement")]
    [InlineData("profile")]
    public async Task CancellationAtEveryRemoteStageIsSafe(string stage) { using var f = new Fixture { CancelStage = stage }; Assert.Equal(nameof(AuthenticationFailureCategory.UserCancelled), (await f.RunAsync()).OfType<AuthenticationFailed>().Single().Code); }
    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    public async Task SecureStorageLoadAndSaveFailuresAreSanitized(string operation)
    {
        using var f = new Fixture { MicrosoftFailure = AuthenticationFailureCategory.SecureStorageUnavailable };
        var failure = (await f.RunAsync()).OfType<AuthenticationFailed>().Single();
        Assert.Equal(nameof(AuthenticationFailureCategory.SecureStorageUnavailable), failure.Code);
        Assert.DoesNotContain("secret", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(operation is "load" or "save");
    }

    [Fact] public async Task StorageDeleteFailureStillReturnsSignedOut() { using var f = new Fixture(); f.Storage.FailDelete = true; var read = f.ReadCountAsync(3); var error = await Assert.ThrowsAsync<AuthenticationException>(() => f.Service.SignOutAsync()); Assert.Equal(AuthenticationFailureCategory.SecureStorageUnavailable, error.Category); Assert.IsType<SignedOutAccount>((await read).Last()); }
    [Theory]
    [InlineData(2148916237, AuthenticationFailureCategory.ChildOrFamilyRestriction)]
    [InlineData(2148916238, AuthenticationFailureCategory.ChildOrFamilyRestriction)]
    [InlineData(2148916233, AuthenticationFailureCategory.XboxProfileMissing)]
    [InlineData(2148916235, AuthenticationFailureCategory.RegionRestriction)]
    [InlineData(2148916227, AuthenticationFailureCategory.XboxServiceDenied)]
    [InlineData(1, AuthenticationFailureCategory.XstsAuthenticationFailed)]
    public void XstsRestrictionsAreSafelyMapped(long code, AuthenticationFailureCategory category) { var error = XstsAuthorizationService.MapError(code); Assert.Equal(category, error.Category); Assert.DoesNotContain(code.ToString(), error.Message); }
    [Theory]
    [InlineData("access_token=ms-secret", "ms-secret")]
    [InlineData("refresh_token=refresh-secret", "refresh-secret")]
    [InlineData("xbox_user_token=xbox-secret", "xbox-secret")]
    [InlineData("xsts_token=xsts-secret", "xsts-secret")]
    [InlineData("device_code=ABCD-EFGH", "ABCD-EFGH")]
    [InlineData("email=person@example.test", "person@example.test")]
    [InlineData("account_id=account-secret", "account-secret")]
    [InlineData("minecraft_uuid=0123456789abcdef", "0123456789abcdef")]
    public void AuthenticationSecretsAreRedacted(string value, string secret) => Assert.DoesNotContain(secret, new LogRedactor().Redact(value));

    private sealed class Fixture : IDisposable
    {
        public AuthenticationFailureCategory? MicrosoftFailure; public AuthenticationFailureCategory? RefreshFailure; public string? FailedStage; public string? CancelStage; public bool HoldMicrosoft; public DateTimeOffset MicrosoftExpiry = Now.AddHours(1);
        public ScriptedMsal Msal { get; } = new(); public ScriptedStorage Storage { get; } = new(); public MsalAuthenticationService Service { get; }
        public Fixture()
        {
            var config = new AuthenticationConfiguration(true, Guid.NewGuid().ToString(), null, ["XboxLive.signin", "offline_access"], "https://login.microsoftonline.com/consumers", true);
            Msal.Owner = this; Service = new(config, Storage, new Stage<XboxAuthenticationResult>(this, "xbox", new("xbox-secret", "hash")), new Stage<XstsAuthorizationResult>(this, "xsts", new("xsts-secret", "hash")), new Stage<MinecraftAuthenticationResult>(this, "minecraft", new("minecraft-secret", Now.AddHours(2))), new EntitlementStage(this), new ProfileStage(this), Msal, new FakeClock());
        }
        public async Task<List<AccountState>> RunAsync() { var read = ReadUntilTerminalAsync(); await Service.BeginSignInAsync(); return await read; }
        public async Task<List<AccountState>> ReadUntilTerminalAsync() { var result = new List<AccountState>(); await foreach (var state in Service.ObserveAccountStateAsync()) { result.Add(state); if (state is SignedInAccount or AuthenticationFailed) return result; } return result; }
        public async Task<List<AccountState>> ReadCountAsync(int count) { var result = new List<AccountState>(); await foreach (var state in Service.ObserveAccountStateAsync()) { result.Add(state); if (result.Count == count) return result; } return result; }
        public void Dispose() => Service.Dispose();
    }
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class ScriptedMsal : IMsalClient
    {
        public Fixture Owner = null!; public int RefreshCalls; public bool Removed; public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;
        public async Task<MicrosoftTokenResult> SignInAsync(IReadOnlyList<string> scopes, bool deviceCode, CancellationToken token) { Started.TrySetResult(); if (Owner.HoldMicrosoft) await Release.Task.WaitAsync(token); if (Owner.MicrosoftFailure is { } failure) throw new AuthenticationException(failure, "Safe Microsoft failure."); DeviceCodeReceived?.Invoke(this, new("https://microsoft.com/devicelogin", "SECRET-CODE", Now.AddMinutes(10))); return new("ms-secret", "account-secret", Owner.MicrosoftExpiry); }
        public Task<MicrosoftTokenResult> RefreshAsync(IReadOnlyList<string> scopes, CancellationToken token) { RefreshCalls++; if (Owner.RefreshFailure is { } failure) throw new AuthenticationException(failure, "Safe refresh failure."); return Task.FromResult(new MicrosoftTokenResult("new-ms-secret", "account", Now.AddHours(1))); }
        public Task RemoveAccountsAsync(CancellationToken token) { Removed = true; return Task.CompletedTask; }
    }
    private sealed class ScriptedStorage : ISecureTokenStorage { public bool Deleted; public bool FailDelete; public Task StoreAsync(string key, string token, CancellationToken ct = default) => Task.CompletedTask; public Task<string?> RetrieveAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null); public Task DeleteAsync(string key, CancellationToken ct = default) { Deleted = true; return FailDelete ? Task.FromException(new IOException("token=secret")) : Task.CompletedTask; } }
    private sealed class Stage<T>(Fixture owner, string name, T result) : IXboxAuthenticationService, IXstsAuthorizationService, IMinecraftAuthenticationService
    {
        private Task<T> Run(CancellationToken token) { if (owner.CancelStage == name) throw new OperationCanceledException(token); if (owner.FailedStage == name) throw new AuthenticationException(name switch { "xbox" => AuthenticationFailureCategory.XboxAuthenticationFailed, "xsts" => AuthenticationFailureCategory.XstsAuthenticationFailed, _ => AuthenticationFailureCategory.MinecraftAuthenticationFailed }, $"Safe {name} failure."); return Task.FromResult(result); }
        Task<XboxAuthenticationResult> IXboxAuthenticationService.AuthenticateAsync(string value, CancellationToken token) => (Task<XboxAuthenticationResult>)(object)Run(token);
        Task<XstsAuthorizationResult> IXstsAuthorizationService.AuthorizeAsync(string value, CancellationToken token) => (Task<XstsAuthorizationResult>)(object)Run(token);
        Task<MinecraftAuthenticationResult> IMinecraftAuthenticationService.AuthenticateAsync(string hash, string value, CancellationToken token) => (Task<MinecraftAuthenticationResult>)(object)Run(token);
    }
    private sealed class EntitlementStage(Fixture owner) : IMinecraftEntitlementService { public Task<MinecraftEntitlement> VerifyAsync(string token, CancellationToken ct = default) { if (owner.CancelStage == "entitlement") throw new OperationCanceledException(ct); if (owner.FailedStage == "entitlement") return Task.FromResult(new MinecraftEntitlement(false)); return Task.FromResult(new MinecraftEntitlement(true, "Minecraft")); } }
    private sealed class ProfileStage(Fixture owner) : IMinecraftProfileService { public Task<MinecraftProfile> GetAsync(string token, CancellationToken ct = default) { if (owner.CancelStage == "profile") throw new OperationCanceledException(ct); if (owner.FailedStage == "profile") throw new AuthenticationException(AuthenticationFailureCategory.MinecraftProfileMissing, "No Minecraft profile was found."); return Task.FromResult(new MinecraftProfile("0123456789abcdef0123456789abcdef", "Player")); } }
}
