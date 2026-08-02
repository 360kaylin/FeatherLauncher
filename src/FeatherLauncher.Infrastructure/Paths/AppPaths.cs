using FeatherLauncher.Core.Services;

namespace FeatherLauncher.Infrastructure.Paths;

public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? basePath = null)
    {
        var root = basePath ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("A local application-data directory is unavailable.");
        Data = SafeCombine(root, "FeatherLauncher"); Logs = SafeCombine(Data, "logs"); Cache = SafeCombine(Data, "cache"); Instances = SafeCombine(Data, "instances"); SettingsFile = SafeCombine(Data, "settings.json");
    }
    public string Data { get; }
    public string Logs { get; }
    public string Cache { get; }
    public string Instances { get; }
    public string SettingsFile { get; }
    public void EnsureCreated() { Directory.CreateDirectory(Data); Directory.CreateDirectory(Logs); Directory.CreateDirectory(Cache); Directory.CreateDirectory(Instances); }
    public static string SafeCombine(string root, params string[] segments)
    {
        var fullRoot = Path.GetFullPath(root); var candidate = Path.GetFullPath(Path.Combine(new[] { fullRoot }.Concat(segments).ToArray()));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The path must remain under its root.", nameof(segments));
        return candidate;
    }
}
