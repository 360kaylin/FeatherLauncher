namespace FeatherLauncher.Core.Models;

public enum MinecraftVersionType { Release, Snapshot, OldBeta, OldAlpha }
public sealed record MinecraftVersion(string Id, MinecraftVersionType Type, Uri MetadataUrl, DateTimeOffset ReleaseTime, DateTimeOffset UpdatedTime, string? Sha1);
public sealed record MinecraftVersionManifest(IReadOnlyList<MinecraftVersion> Versions, string LatestRelease, string LatestSnapshot);
public sealed record VersionMetadata(string Id, MinecraftVersionType Type, string Json);
public sealed record MetadataCacheStatus(bool Exists, bool IsExpired, bool UsedOfflineFallback, TimeSpan? Age, long SizeBytes);
public sealed record ManifestResult(MinecraftVersionManifest Manifest, MetadataCacheStatus Cache);
