using System.Net;
using System.Text;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Infrastructure.Authentication;
using FeatherLauncher.Infrastructure.Logging;
using FeatherLauncher.Infrastructure.Minecraft;
using FeatherLauncher.Infrastructure.Paths;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FeatherLauncher.Tests;

public sealed class Phase2Tests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FeatherPhase2", Guid.NewGuid().ToString("N"));
    private const string Manifest = """{"latest":{"release":"1.21","snapshot":"25w01a"},"versions":[{"id":"1.21","type":"release","url":"https://example.test/1","time":"2025-01-02T00:00:00Z","releaseTime":"2025-01-01T00:00:00Z","sha1":"0123456789012345678901234567890123456789"},{"id":"25w01a","type":"snapshot","url":"https://example.test/2","time":"2025-01-03T00:00:00Z","releaseTime":"2025-01-03T00:00:00Z"},{"id":"b1.0","type":"old_beta","url":"https://example.test/3","time":"2011-01-01T00:00:00Z","releaseTime":"2011-01-01T00:00:00Z"},{"id":"a1.0","type":"old_alpha","url":"https://example.test/4","time":"2010-01-01T00:00:00Z","releaseTime":"2010-01-01T00:00:00Z"}]}""";
    [Fact] public void ManifestParsesAllTypes() { var value = MinecraftMetadataService.Parse(Manifest); Assert.Equal(4, value.Versions.Count); Assert.Equal("1.21", value.LatestRelease); }
    [Theory][InlineData(MinecraftVersionType.Release, "1.21")][InlineData(MinecraftVersionType.Snapshot, "25w01a")][InlineData(MinecraftVersionType.OldBeta, "b1.0")][InlineData(MinecraftVersionType.OldAlpha, "a1.0")] public void ManifestFiltersTypes(MinecraftVersionType type, string id) => Assert.Equal(id, Assert.Single(MinecraftMetadataService.Parse(Manifest).Versions, x => x.Type == type).Id);
    [Theory][InlineData("{")][InlineData("{\"latest\":{},\"versions\":[]}")][InlineData("{\"latest\":{\"release\":\"x\",\"snapshot\":\"y\"},\"versions\":[{\"id\":\"x\"}]}")] public void MalformedOrIncompleteManifestRejected(string json) => Assert.Throws<InvalidDataException>(() => MinecraftMetadataService.Parse(json));
    [Fact] public async Task CacheSavesAndLoads() { var service = CreateService(new ReplyHandler(HttpStatusCode.OK, Manifest)); var first = await service.GetManifestAsync(); var second = await service.GetManifestAsync(); Assert.False(first.Cache.UsedOfflineFallback); Assert.True(second.Cache.Exists); Assert.Equal(4, second.Manifest.Versions.Count); }
    [Fact] public async Task ExpiredCacheUsesOfflineFallback() { var good = CreateService(new ReplyHandler(HttpStatusCode.OK, Manifest), TimeSpan.Zero); await good.GetManifestAsync(); var offline = CreateService(new ReplyHandler(HttpStatusCode.ServiceUnavailable, ""), TimeSpan.Zero); var result = await offline.GetManifestAsync(); Assert.True(result.Cache.IsExpired); Assert.True(result.Cache.UsedOfflineFallback); }
    [Fact] public async Task HttpFailureWithoutCacheFails() => await Assert.ThrowsAsync<HttpRequestException>(() => CreateService(new ReplyHandler(HttpStatusCode.BadGateway, "")).GetManifestAsync());
    [Fact] public async Task CancellationIsHonored() { using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(new ReplyHandler(HttpStatusCode.OK, Manifest)).GetManifestAsync(cancellationToken: cancel.Token)); }
    [Fact] public void TokensAreRedacted() { var output = new LogRedactor().Redact("refresh_token=secret Authorization: Bearer abc.def"); Assert.DoesNotContain("secret", output); Assert.DoesNotContain("abc.def", output); }
    [Fact] public async Task UnsupportedSecureStorageFailsClosed() => await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new UnsupportedPlatformTokenStorage().StoreAsync("account", "token"));
    [Fact] public void AccountStateTransitionsAreEnforced() { var store = new AccountStateStore(); store.Transition(new SigningInAccount(DateTimeOffset.UtcNow)); store.Transition(new SignedInAccount(new("subject", new("Player")), new("id", "Player"), new(true), new(DateTimeOffset.UtcNow.AddHours(1)))); Assert.IsType<SignedInAccount>(store.Current); Assert.Throws<InvalidOperationException>(() => store.Transition(new SigningInAccount(DateTimeOffset.UtcNow))); }
    [Fact] public void AuthenticationIsDisabledWithoutConfiguration() { var old = Environment.GetEnvironmentVariable("FEATHER_AUTH_ENABLED"); try { Environment.SetEnvironmentVariable("FEATHER_AUTH_ENABLED", null); Assert.False(new EnvironmentAuthenticationConfigurationProvider().Get().IsConfigured); } finally { Environment.SetEnvironmentVariable("FEATHER_AUTH_ENABLED", old); } }
    private MinecraftMetadataService CreateService(HttpMessageHandler handler, TimeSpan? lifetime = null) { var paths = new AppPaths(root); paths.EnsureCreated(); return new(new HttpClient(handler), paths, NullLogger<MinecraftMetadataService>.Instance, lifetime: lifetime); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class ReplyHandler(HttpStatusCode status, string body) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); } }
}
