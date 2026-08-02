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
