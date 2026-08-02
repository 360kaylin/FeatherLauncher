namespace FeatherLauncher.Core.Models;

public enum LauncherTheme { Dark, Light, System }
public enum GameStartBehavior { Close, Minimize, KeepOpen }

public sealed record LauncherSettings
{
    public const int DefaultCacheSizeLimitMb = 2048;
    public LauncherTheme Theme { get; init; } = LauncherTheme.Dark;
    public GameStartBehavior GameStartBehavior { get; init; } = GameStartBehavior.KeepOpen;
    public bool CheckForUpdatesAutomatically { get; init; } = true;
    public int CacheSizeLimitMb { get; init; } = DefaultCacheSizeLimitMb;
    public string DefaultInstanceLocation { get; init; } = string.Empty;
}
