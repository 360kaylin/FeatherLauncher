using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;

namespace FeatherLauncher.Infrastructure.Authentication;

public sealed class EnvironmentAuthenticationConfigurationProvider : IAuthenticationConfigurationProvider
{
    public AuthenticationConfiguration Get()
    {
        var clientId = Environment.GetEnvironmentVariable("FEATHER_MS_CLIENT_ID");
        var redirectText = Environment.GetEnvironmentVariable("FEATHER_MS_REDIRECT_URI");
        _ = Uri.TryCreate(redirectText, UriKind.Absolute, out var redirect);
        var scopes = (Environment.GetEnvironmentVariable("FEATHER_MS_SCOPES") ?? "XboxLive.signin offline_access").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new(string.Equals(Environment.GetEnvironmentVariable("FEATHER_AUTH_ENABLED"), "true", StringComparison.OrdinalIgnoreCase), clientId, redirect, scopes, Environment.GetEnvironmentVariable("FEATHER_MS_AUTHORITY") ?? "https://login.microsoftonline.com/consumers", string.Equals(Environment.GetEnvironmentVariable("FEATHER_MS_USE_DEVICE_CODE"), "true", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DisabledMicrosoftAuthenticationService : IMicrosoftAuthenticationService
{
    public AccountState CurrentState { get; } = new SignedOutAccount();
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived { add { } remove { } }
    public Task BeginSignInAsync(CancellationToken cancellationToken = default) => Task.FromException(new InvalidOperationException("Microsoft sign-in is not configured yet."));
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.FromException(new InvalidOperationException("Microsoft sign-in is not configured yet."));
    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public async IAsyncEnumerable<AccountState> ObserveAccountStateAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); yield return new SignedOutAccount(); await Task.CompletedTask; }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiTokenStorage(string directory) : ISecureTokenStorage
{
    public async Task StoreAsync(string key, string token, CancellationToken cancellationToken = default) { Validate(key); Directory.CreateDirectory(directory); var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser); await File.WriteAllBytesAsync(PathFor(key), encrypted, cancellationToken); }
    public async Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default) { Validate(key); if (!File.Exists(PathFor(key))) return null; var encrypted = await File.ReadAllBytesAsync(PathFor(key), cancellationToken); return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser)); }
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Validate(key); File.Delete(PathFor(key)); return Task.CompletedTask; }
    private string PathFor(string key) => Path.Combine(directory, key + ".bin");
    private static void Validate(string key) { if (string.IsNullOrWhiteSpace(key) || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid token key.", nameof(key)); }
}

public sealed class UnsupportedPlatformTokenStorage : ISecureTokenStorage
{
    private static PlatformNotSupportedException Error() => new("Secure token storage is disabled on this platform; tokens were not persisted.");
    public Task StoreAsync(string key, string token, CancellationToken cancellationToken = default) => Task.FromException(Error());
    public Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default) => Task.FromException<string?>(Error());
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromException(Error());
}

public sealed class InMemoryTokenStorage : ISecureTokenStorage
{
    private readonly Dictionary<string, string> values = [];
    public Task StoreAsync(string key, string token, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); values[key] = token; return Task.CompletedTask; }
    public Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(values.GetValueOrDefault(key)); }
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); values.Remove(key); return Task.CompletedTask; }
}

public sealed class AccountStateStore
{
    public AccountState Current { get; private set; } = new SignedOutAccount();
    public event EventHandler<AccountState>? Changed;
    public void Transition(AccountState state)
    {
        if (state is SigningInAccount && Current is not SignedOutAccount and not AuthenticationFailed) throw new InvalidOperationException("Sign-in can only start while signed out or after a failure.");
        if (state is SignedInAccount && Current is not ProfileLoadedAccount and not SigningInAccount) throw new InvalidOperationException("A signed-in identity must follow profile loading.");
        Current = state; Changed?.Invoke(this, state);
    }
}
