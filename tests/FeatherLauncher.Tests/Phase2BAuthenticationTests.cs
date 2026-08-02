using System.Text.Json;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Infrastructure.Authentication;
using FeatherLauncher.Infrastructure.Logging;
using Xunit;

namespace FeatherLauncher.Tests;

public sealed class Phase2BAuthenticationTests
{
    [Fact] public void ValidConfigurationIsAccepted() => Assert.True(new AuthenticationConfiguration(true, Guid.NewGuid().ToString(), null, ["XboxLive.signin", "offline_access"], "https://login.microsoftonline.com/consumers", true).IsConfigured);
    [Fact] public void MissingClientIdIsRejected() => Assert.False(new AuthenticationConfiguration(true, null, null, ["XboxLive.signin"], "https://login.microsoftonline.com/consumers", true).IsConfigured);
    [Fact] public void FeatureFlagIsRequired() => Assert.False(new AuthenticationConfiguration(false, Guid.NewGuid().ToString(), null, ["XboxLive.signin"], "https://login.microsoftonline.com/consumers", true).IsConfigured);
    [Fact] public async Task InMemorySecureStorageRoundTripsAndDeletes() { var storage = new InMemoryTokenStorage(); await storage.StoreAsync("cache", "sensitive"); Assert.Equal("sensitive", await storage.RetrieveAsync("cache")); await storage.DeleteAsync("cache"); Assert.Null(await storage.RetrieveAsync("cache")); }
    [Fact] public async Task SecureStorageHonorsCancellation() { using var source = new CancellationTokenSource(); source.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InMemoryTokenStorage().StoreAsync("cache", "value", source.Token)); }
    [Fact] public void XboxResponseParses() { using var doc = JsonDocument.Parse("""{"Token":"xbox-token","DisplayClaims":{"xui":[{"uhs":"hash"}]}}"""); var value = XboxAuthenticationService.Parse(doc.RootElement); Assert.Equal("hash", value.UserHash); Assert.NotEmpty(value.Token); }
    [Fact] public void XboxMissingClaimsFailsSafely() { using var doc = JsonDocument.Parse("""{"Token":"xbox-token"}"""); var error = Assert.Throws<AuthenticationException>(() => XboxAuthenticationService.Parse(doc.RootElement)); Assert.Equal(AuthenticationFailureCategory.XboxAuthenticationFailed, error.Category); Assert.DoesNotContain("xbox-token", error.Message); }
    [Fact] public void XstsResponseParses() { using var doc = JsonDocument.Parse("""{"Token":"xsts-token","DisplayClaims":{"xui":[{"uhs":"hash"}]}}"""); Assert.Equal("hash", XstsAuthorizationService.Parse(doc.RootElement).UserHash); }
    [Fact] public void MinecraftAuthenticationParsesExpiry() { using var doc = JsonDocument.Parse("""{"access_token":"minecraft-token","expires_in":3600}"""); var now = DateTimeOffset.UtcNow; var result = MinecraftAuthenticationService.Parse(doc.RootElement, now); Assert.Equal(now.AddHours(1), result.ExpiresAt); }
    [Fact] public void MinecraftAuthenticationRejectsBadExpiry() { using var doc = JsonDocument.Parse("""{"access_token":"minecraft-token","expires_in":-1}"""); Assert.Throws<AuthenticationException>(() => MinecraftAuthenticationService.Parse(doc.RootElement, DateTimeOffset.UtcNow)); }
    [Fact] public void ProfileParses() { using var doc = JsonDocument.Parse("""{"id":"0123456789abcdef0123456789abcdef","name":"Player_1"}"""); Assert.Equal("Player_1", MinecraftProfileService.Parse(doc.RootElement).Name); }
    [Theory][InlineData("short")][InlineData("zz23456789abcdef0123456789abcdef")][InlineData("0123456789abcdef0123456789abcdef00")] public void ProfileIdValidation(string id) { using var doc = JsonDocument.Parse($"{{\"id\":\"{id}\",\"name\":\"Player\"}}"); Assert.Throws<AuthenticationException>(() => MinecraftProfileService.Parse(doc.RootElement)); }
    [Theory][InlineData("ab")][InlineData("name-with-dash")][InlineData("PlayerNameIsFarTooLong")] public void ProfileNameValidation(string name) { using var doc = JsonDocument.Parse($"{{\"id\":\"0123456789abcdef0123456789abcdef\",\"name\":\"{name}\"}}"); Assert.Throws<AuthenticationException>(() => MinecraftProfileService.Parse(doc.RootElement)); }
    [Theory][InlineData("access_token=access-secret", "access-secret")][InlineData("refresh-token: refresh-secret", "refresh-secret")][InlineData("Authorization: Bearer auth-secret", "auth-secret")][InlineData("xuid=xuid-secret", "xuid-secret")][InlineData("email=user@example.test", "user@example.test")][InlineData("password=hunter2", "hunter2")] public void SensitiveValuesAreRedacted(string input, string secret) => Assert.DoesNotContain(secret, new LogRedactor().Redact(input));
    [Fact] public void ExceptionsExposeOnlySafeMessage() { var inner = new InvalidOperationException("access_token=do-not-display"); var error = new AuthenticationException(AuthenticationFailureCategory.UnknownFailure, "Authentication failed safely.", true, inner); Assert.DoesNotContain("do-not-display", error.Message); }
    [Fact] public async Task DisabledProviderCannotSignIn() { var service = new DisabledMicrosoftAuthenticationService(); var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BeginSignInAsync()); Assert.Equal("Microsoft sign-in is not configured yet.", error.Message); }
    [Fact] public void TokenExpiryIsDetected() => Assert.True(new TokenExpiry(DateTimeOffset.UtcNow.AddSeconds(-1)).IsExpired(DateTimeOffset.UtcNow));
    [Fact] public void ConcurrentStateSignInIsRejected() { var store = new AccountStateStore(); store.Transition(new SigningInAccount(DateTimeOffset.UtcNow)); Assert.Throws<InvalidOperationException>(() => store.Transition(new SigningInAccount(DateTimeOffset.UtcNow))); }
}
