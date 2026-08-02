using System.Text.Json;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using Microsoft.Extensions.Logging;

namespace FeatherLauncher.Infrastructure.Settings;

public sealed class JsonSettingsService(IAppPaths paths, ILogger<JsonSettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsFile)) return Defaults();
        try
        {
            await using var stream = File.OpenRead(paths.SettingsFile);
            var settings = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, Options, cancellationToken);
            return Validate(settings ?? throw new JsonException("Settings were empty."));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Invalid settings ignored: {ErrorType}", ex.GetType().Name); return Defaults();
        }
    }
    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        settings = Validate(settings); paths.EnsureCreated(); var temporary = paths.SettingsFile + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        File.Move(temporary, paths.SettingsFile, true);
    }
    public async Task<LauncherSettings> ResetAsync(CancellationToken cancellationToken = default) { var value = Defaults(); await SaveAsync(value, cancellationToken); return value; }
    private LauncherSettings Defaults() => new() { DefaultInstanceLocation = paths.Instances };
    private static LauncherSettings Validate(LauncherSettings value)
    {
        if (!Enum.IsDefined(value.Theme) || !Enum.IsDefined(value.GameStartBehavior) || value.CacheSizeLimitMb is < 128 or > 1_048_576 || string.IsNullOrWhiteSpace(value.DefaultInstanceLocation)) throw new JsonException("Settings contain invalid values.");
        return value;
    }
}
