using System.Text.Json;
using FeatherLauncher.Core.Models;

namespace FeatherLauncher.Infrastructure.Authentication;

/// <summary>Stores an operator's local test record only; it is not evidence of universal service correctness.</summary>
public sealed class ManualAuthenticationVerificationStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<ManualAuthenticationVerification> LoadAsync(string configurationFingerprint, string appVersion, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return ManualAuthenticationVerification.NotVerified(configurationFingerprint, appVersion);
        try
        {
            await using var stream = File.OpenRead(path); var value = await JsonSerializer.DeserializeAsync<ManualAuthenticationVerification>(stream, Options, cancellationToken);
            return value is not null && value.ConfigurationFingerprint == configurationFingerprint ? value : ManualAuthenticationVerification.NotVerified(configurationFingerprint, appVersion);
        }
        catch (JsonException) { return ManualAuthenticationVerification.NotVerified(configurationFingerprint, appVersion); }
    }
    public async Task SaveAsync(ManualAuthenticationVerification value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "."); await using var stream = File.Create(path); await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
    }
}
