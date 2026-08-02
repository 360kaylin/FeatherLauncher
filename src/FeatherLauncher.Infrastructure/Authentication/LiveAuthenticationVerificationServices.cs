using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;

namespace FeatherLauncher.Infrastructure.Authentication;

public sealed class JsonAuthenticationConfigurationStore(string path) : IAuthenticationConfigurationStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public AuthenticationConfiguration Get()
    {
        if (!File.Exists(path)) return new(false, null, null, ["XboxLive.signin", "offline_access"], "https://login.microsoftonline.com/consumers", true);
        try { return JsonSerializer.Deserialize<AuthenticationConfiguration>(File.ReadAllText(path), Options) ?? throw new JsonException(); }
        catch (JsonException) { return new(false, null, null, ["XboxLive.signin", "offline_access"], "https://login.microsoftonline.com/consumers", true); }
    }
    public async Task SaveAsync(AuthenticationConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(configuration with { RedirectUri = null }, Options), cancellationToken);
    }
    public string Fingerprint(AuthenticationConfiguration configuration)
    {
        var material = $"{configuration.FeatureEnabled}|{configuration.ClientId?.Trim()}|{configuration.Authority.Trim()}|{string.Join(' ', configuration.RequiredScopes.Order(StringComparer.Ordinal))}|{configuration.UseDeviceCode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

public sealed class AuthenticationChecklistStore(string path, ILogRedactor redactor)
{
    public static readonly string[] Scenarios = ["Owned Minecraft Java account", "Microsoft account without Java entitlement", "Cancelled device-code sign-in", "Expired device code", "Sign-out", "Account switching", "Account-switch cancellation", "Silent refresh after expiry", "Revoked or invalid refresh session", "Network interruption", "Log and diagnostics inspection"];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<AuthenticationChecklist> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(path)) try { await using var input = File.OpenRead(path); var saved = await JsonSerializer.DeserializeAsync<AuthenticationChecklist>(input, Options, cancellationToken); if (saved is not null) return saved; } catch (JsonException) { }
        return new(Scenarios.Select(x => new AuthenticationChecklistItem(x, VerificationResult.NotTested, null, string.Empty)).ToArray());
    }
    public async Task SaveAsync(AuthenticationChecklist checklist, CancellationToken cancellationToken = default)
    {
        var safe = checklist with { Items = checklist.Items.Select(x => x with { Note = redactor.Redact(x.Note).Trim() }).ToArray() };
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "."); await using var output = File.Create(path); await JsonSerializer.SerializeAsync(output, safe, Options, cancellationToken);
    }
}

public sealed class AuthenticationDiagnosticsExporter(ILogRedactor redactor)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task ExportAsync(string path, AuthenticationDiagnosticsReport report, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(report, Options);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.WriteAllTextAsync(path, redactor.Redact(json), cancellationToken);
    }
    public static AuthenticationDiagnosticsReport Create(AuthenticationConfiguration configuration, bool secureStorage, string state, string error, bool verified, IReadOnlyList<string> scenarios) => new(
        typeof(AuthenticationDiagnosticsExporter).Assembly.GetName().Version?.ToString() ?? "Unavailable", RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), configuration.IsConfigured, configuration.UseDeviceCode ? "Device code" : "System browser", Uri.TryCreate(configuration.Authority, UriKind.Absolute, out var authority) ? authority.Host : "Invalid", configuration.RequiredScopes, secureStorage, state, error, verified, DateTimeOffset.UtcNow, scenarios);
}

public sealed class AuthenticationDataClearer(IMicrosoftAuthenticationService authentication, ISecureTokenStorage storage, string logsDirectory)
{
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try { await authentication.SignOutAsync(cancellationToken); } catch (AuthenticationException) { }
        await storage.DeleteAsync("msal-token-cache", cancellationToken);
        if (Directory.Exists(logsDirectory)) foreach (var log in Directory.EnumerateFiles(logsDirectory, "launcher-*.log")) File.Delete(log);
    }
}
