using FeatherLauncher.Core.Models;

namespace FeatherLauncher.Core.Services;

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
    Task<LauncherSettings> ResetAsync(CancellationToken cancellationToken = default);
}
public interface IAppPaths
{
    string Data { get; }
    string Logs { get; }
    string Cache { get; }
    string Instances { get; }
    string SettingsFile { get; }
    void EnsureCreated();
}
public interface ICacheService { Task<long> GetSizeBytesAsync(CancellationToken cancellationToken = default); }
public interface ILogRedactor { string Redact(string message); }

public interface IMicrosoftAuthenticationService
{
    event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;
    Task BeginSignInAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AccountState> ObserveAccountStateAsync(CancellationToken cancellationToken = default);
}
public interface ISecureTokenStorage
{
    Task StoreAsync(string key, string token, CancellationToken cancellationToken = default);
    Task<string?> RetrieveAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
public interface IMinecraftEntitlementService { Task<MinecraftEntitlement> VerifyAsync(string accessToken, CancellationToken cancellationToken = default); }
public interface IMinecraftProfileService { Task<MinecraftProfile> GetAsync(string accessToken, CancellationToken cancellationToken = default); }
public interface IAuthenticationConfigurationProvider { AuthenticationConfiguration Get(); }
public interface IXboxAuthenticationService { Task<XboxAuthenticationResult> AuthenticateAsync(string microsoftAccessToken, CancellationToken cancellationToken = default); }
public interface IXstsAuthorizationService { Task<XstsAuthorizationResult> AuthorizeAsync(string xboxToken, CancellationToken cancellationToken = default); }
public interface IMinecraftAuthenticationService { Task<MinecraftAuthenticationResult> AuthenticateAsync(string userHash, string xstsToken, CancellationToken cancellationToken = default); }
public sealed record XboxAuthenticationResult(string Token, string UserHash);
public sealed record XstsAuthorizationResult(string Token, string UserHash);
public sealed record MinecraftAuthenticationResult(string AccessToken, DateTimeOffset ExpiresAt);
public interface IMinecraftMetadataService
{
    Task<ManifestResult> GetManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<VersionMetadata> GetVersionAsync(MinecraftVersion version, CancellationToken cancellationToken = default);
    Task ClearCacheAsync(CancellationToken cancellationToken = default);
    Task<MetadataCacheStatus> GetCacheStatusAsync(CancellationToken cancellationToken = default);
}
