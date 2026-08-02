using System.Text.Json;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Infrastructure.Logging;
using FeatherLauncher.Infrastructure.Paths;
using FeatherLauncher.Infrastructure.Settings;
using FeatherLauncher.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FeatherLauncher.Tests;

public sealed class FoundationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FeatherLauncherTests", Guid.NewGuid().ToString("N"));
    [Fact] public async Task SettingsSaveAndLoadRoundTrips() { var service = CreateSettings(); var expected = new LauncherSettings { Theme = LauncherTheme.Light, GameStartBehavior = GameStartBehavior.Minimize, CheckForUpdatesAutomatically = false, CacheSizeLimitMb = 4096, DefaultInstanceLocation = Path.Combine(root, "custom") }; await service.SaveAsync(expected); Assert.Equal(expected, await service.LoadAsync()); }
    [Fact] public async Task MissingSettingsReturnDefaults() { var value = await CreateSettings().LoadAsync(); Assert.Equal(LauncherTheme.Dark, value.Theme); Assert.Equal(2048, value.CacheSizeLimitMb); Assert.True(value.CheckForUpdatesAutomatically); Assert.Equal(new AppPaths(root).Instances, value.DefaultInstanceLocation); }
    [Fact] public void SafePathsStayWithinRoot() { var paths = new AppPaths(root); Assert.StartsWith(Path.GetFullPath(root), paths.Cache); Assert.Throws<ArgumentException>(() => AppPaths.SafeCombine(root, "..", "escape")); }
    [Theory][InlineData("password=hunter2", "password=[REDACTED]")][InlineData("Authorization: Bearer abc.def", "Authorization=[REDACTED]")][InlineData("email=user@example.com", "email=[REDACTED]")] public void LogsRedactSensitiveValues(string input, string fragment) => Assert.Contains(fragment, new LogRedactor().Redact(input));
    [Fact] public async Task CacheSizeIsCalculatedFromRealFiles() { var paths = new AppPaths(root); paths.EnsureCreated(); await File.WriteAllBytesAsync(Path.Combine(paths.Cache, "one"), new byte[17]); Directory.CreateDirectory(Path.Combine(paths.Cache, "nested")); await File.WriteAllBytesAsync(Path.Combine(paths.Cache, "nested", "two"), new byte[25]); Assert.Equal(42, await new CacheService(paths).GetSizeBytesAsync()); }
    [Theory][InlineData("{broken")][InlineData("{\"cacheSizeLimitMb\":1,\"defaultInstanceLocation\":\"x\"}")] public async Task InvalidConfigurationFallsBackSafely(string json) { var paths = new AppPaths(root); paths.EnsureCreated(); await File.WriteAllTextAsync(paths.SettingsFile, json); var loaded = await CreateSettings().LoadAsync(); Assert.Equal(LauncherSettings.DefaultCacheSizeLimitMb, loaded.CacheSizeLimitMb); Assert.Equal(paths.Instances, loaded.DefaultInstanceLocation); }
    private JsonSettingsService CreateSettings() => new(new AppPaths(root), NullLogger<JsonSettingsService>.Instance);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
