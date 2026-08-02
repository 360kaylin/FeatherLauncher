using System.Text.Json;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using FeatherLauncher.Infrastructure.Authentication;
using FeatherLauncher.Infrastructure.Logging;
using Xunit;

namespace FeatherLauncher.Tests;

public sealed class Phase2DLiveVerificationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "feather-phase2d-" + Guid.NewGuid());
    private static AuthenticationConfiguration Valid(string? id = null) => new(true, id ?? Guid.NewGuid().ToString(), null, ["XboxLive.signin", "offline_access"], "https://login.microsoftonline.com/consumers", true);
    [Fact]
    public void ConfigurationValidationRequiresEnabledNonEmptyGuidHttpsAuthorityScopesAndDeviceCode()
    {
        Assert.True(Valid().IsConfigured); Assert.False(Valid(Guid.Empty.ToString()).IsConfigured); Assert.False((Valid() with { FeatureEnabled = false }).IsConfigured); Assert.False((Valid() with { Authority = "http://example.test" }).IsConfigured); Assert.False((Valid() with { RequiredScopes = ["XboxLive.signin"] }).IsConfigured); Assert.False((Valid() with { UseDeviceCode = false }).IsConfigured);
    }
    [Fact]
    public async Task MaterialConfigurationChangesResetVerificationRecord()
    {
        var configStore = new JsonAuthenticationConfigurationStore(Path.Combine(directory, "auth.json")); var recordStore = new ManualAuthenticationVerificationStore(Path.Combine(directory, "verification.json")); var original = Valid(); var changed = original with { Authority = "https://login.microsoftonline.com/common" }; var originalFingerprint = configStore.Fingerprint(original); await recordStore.SaveAsync(new(true, DateTimeOffset.UtcNow, "test", ["owned"], originalFingerprint));
        Assert.True((await recordStore.LoadAsync(originalFingerprint, "test")).Verified); Assert.False((await recordStore.LoadAsync(configStore.Fingerprint(changed), "test")).Verified);
    }
    [Fact]
    public async Task ChecklistPersistsAndRedactsNotes()
    {
        var path = Path.Combine(directory, "checklist.json"); var store = new AuthenticationChecklistStore(path, new LogRedactor()); await store.SaveAsync(new([new("Owned Minecraft Java account", VerificationResult.Pass, DateTimeOffset.UtcNow, "email=person@example.test access_token=very-secret-token")])); var loaded = await store.LoadAsync(); Assert.Equal(VerificationResult.Pass, loaded.Items.Single().Result); Assert.DoesNotContain("person@example.test", loaded.Items.Single().Note); Assert.DoesNotContain("very-secret-token", loaded.Items.Single().Note);
    }
    [Fact]
    public async Task DiagnosticsExportUsesAllowListAndContainsNoSecrets()
    {
        var path = Path.Combine(directory, "report.json"); var exporter = new AuthenticationDiagnosticsExporter(new LogRedactor()); var report = AuthenticationDiagnosticsExporter.Create(Valid(), true, "Signed out", "None", false, ["Owned account"]); await exporter.ExportAsync(path, report); var text = await File.ReadAllTextAsync(path); Assert.Contains("AuthorityHost", text); Assert.DoesNotContain("AccessToken", text); Assert.DoesNotContain("RefreshToken", text); Assert.DoesNotContain("Authorization", text); Assert.DoesNotContain("MinecraftUuid", text); Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(text); Assert.False(document.RootElement.TryGetProperty("ClientId", out _));
    }
    [Fact]
    public async Task ClearAuthenticationDataSignsOutDeletesCacheAndLogsOnly()
    {
        Directory.CreateDirectory(directory); var logs = Path.Combine(directory, "logs"); Directory.CreateDirectory(logs); await File.WriteAllTextAsync(Path.Combine(logs, "launcher-test.log"), "safe"); var unrelated = Path.Combine(directory, "settings.json"); await File.WriteAllTextAsync(unrelated, "keep"); var auth = new FakeAuthentication(); var storage = new FakeStorage(); await new AuthenticationDataClearer(auth, storage, logs).ClearAsync(); Assert.True(auth.SignedOut); Assert.True(storage.Deleted); Assert.Empty(Directory.GetFiles(logs)); Assert.True(File.Exists(unrelated));
    }
    [Fact] public void SignInEligibilityTracksConfigurationValidity() { Assert.True(Valid().IsConfigured); Assert.False((Valid() with { ClientId = "not-a-guid" }).IsConfigured); }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class FakeStorage : ISecureTokenStorage { public bool Deleted; public Task StoreAsync(string key, string token, CancellationToken cancellationToken = default) => Task.CompletedTask; public Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null); public Task DeleteAsync(string key, CancellationToken cancellationToken = default) { Deleted = true; return Task.CompletedTask; } }
    private sealed class FakeAuthentication : IMicrosoftAuthenticationService { public bool SignedOut; public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived { add { } remove { } } public Task BeginSignInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SignOutAsync(CancellationToken cancellationToken = default) { SignedOut = true; return Task.CompletedTask; } public async IAsyncEnumerable<AccountState> ObserveAccountStateAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield return new SignedOutAccount(); await Task.CompletedTask; } }
}
