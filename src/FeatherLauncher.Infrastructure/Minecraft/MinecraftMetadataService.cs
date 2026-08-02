using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using Microsoft.Extensions.Logging;

namespace FeatherLauncher.Infrastructure.Minecraft;

public sealed class MinecraftMetadataService(HttpClient http, IAppPaths paths, ILogger<MinecraftMetadataService> logger, TimeProvider? clock = null, TimeSpan? lifetime = null) : IMinecraftMetadataService
{
    public static readonly Uri OfficialManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private const int MaxDocumentBytes = 8 * 1024 * 1024;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly TimeSpan cacheLifetime = lifetime ?? TimeSpan.FromHours(6);
    private string CacheFile => Path.Combine(paths.Cache, "minecraft", "version-manifest-v2.json");

    public async Task<ManifestResult> GetManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var before = await GetCacheStatusAsync(cancellationToken);
        if (!forceRefresh && before.Exists && !before.IsExpired) return new(Parse(await File.ReadAllTextAsync(CacheFile, cancellationToken)), before);
        try
        {
            logger.LogInformation("Requesting Minecraft version manifest from host {Host}", OfficialManifestUri.Host);
            using var response = await http.GetAsync(OfficialManifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Manifest request returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength is > MaxDocumentBytes) throw new InvalidDataException("Manifest is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var limited = new MemoryStream(); await CopyLimitedAsync(stream, limited, cancellationToken);
            var json = Encoding.UTF8.GetString(limited.ToArray()); var manifest = Parse(json);
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!); var temp = CacheFile + ".tmp"; await File.WriteAllTextAsync(temp, json, cancellationToken); File.Move(temp, CacheFile, true);
            logger.LogInformation("Minecraft version manifest refreshed with {Count} entries", manifest.Versions.Count);
            return new(manifest, await GetCacheStatusAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            if (!File.Exists(CacheFile)) throw;
            logger.LogWarning("Manifest request failed ({FailureType}); using validated offline cache", ex.GetType().Name);
            var manifest = Parse(await File.ReadAllTextAsync(CacheFile, cancellationToken)); var status = await GetCacheStatusAsync(cancellationToken);
            return new(manifest, status with { UsedOfflineFallback = true });
        }
    }

    public async Task<VersionMetadata> GetVersionAsync(MinecraftVersion version, CancellationToken cancellationToken = default)
    {
        ValidateHttps(version.MetadataUrl); using var response = await http.GetAsync(version.MetadataUrl, cancellationToken); response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken); if (bytes.Length > MaxDocumentBytes) throw new InvalidDataException("Version metadata is too large.");
        if (version.Sha1 is not null && !Convert.ToHexString(SHA1.HashData(bytes)).Equals(version.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Version metadata hash mismatch.");
        var json = Encoding.UTF8.GetString(bytes); using var document = JsonDocument.Parse(json); if (!document.RootElement.TryGetProperty("id", out var id) || id.GetString() != version.Id) throw new InvalidDataException("Version metadata identifier is missing or mismatched.");
        return new(version.Id, version.Type, json);
    }
    public Task ClearCacheAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); if (File.Exists(CacheFile)) File.Delete(CacheFile); return Task.CompletedTask; }
    public Task<MetadataCacheStatus> GetCacheStatusAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); if (!File.Exists(CacheFile)) return Task.FromResult(new MetadataCacheStatus(false, false, false, null, 0)); var info = new FileInfo(CacheFile); var age = time.GetUtcNow() - info.LastWriteTimeUtc; return Task.FromResult(new MetadataCacheStatus(true, age > cacheLifetime, false, age, info.Length)); }

    public static MinecraftVersionManifest Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }); var root = doc.RootElement;
            var latest = root.GetProperty("latest"); var release = RequiredString(latest, "release"); var snapshot = RequiredString(latest, "snapshot"); var versions = root.GetProperty("versions"); if (versions.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Versions must be an array.");
            List<MinecraftVersion> parsed = [];
            foreach (var item in versions.EnumerateArray())
            {
                var id = RequiredString(item, "id"); if (id.Length > 100 || id.Any(char.IsControl)) throw new InvalidDataException("Invalid version identifier.");
                var type = RequiredString(item, "type") switch { "release" => MinecraftVersionType.Release, "snapshot" => MinecraftVersionType.Snapshot, "old_beta" => MinecraftVersionType.OldBeta, "old_alpha" => MinecraftVersionType.OldAlpha, _ => throw new InvalidDataException("Unknown version type.") };
                var url = new Uri(RequiredString(item, "url"), UriKind.Absolute); ValidateHttps(url); var sha1 = item.TryGetProperty("sha1", out var hash) ? hash.GetString() : null; if (sha1 is not null && (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid SHA-1.");
                parsed.Add(new(id, type, url, RequiredDate(item, "releaseTime"), RequiredDate(item, "time"), sha1));
            }
            if (parsed.Count == 0) throw new InvalidDataException("Manifest contains no versions."); return new(parsed, release, snapshot);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException or FormatException) { throw new InvalidDataException("Malformed or incomplete Minecraft version manifest.", ex); }
    }
    private static string RequiredString(JsonElement value, string name) { if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString())) throw new InvalidDataException($"Missing {name}."); return property.GetString()!; }
    private static DateTimeOffset RequiredDate(JsonElement value, string name) => DateTimeOffset.Parse(RequiredString(value, name), System.Globalization.CultureInfo.InvariantCulture);
    private static void ValidateHttps(Uri uri) { if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host)) throw new InvalidDataException("Only absolute HTTPS metadata URLs are accepted."); }
    private static async Task CopyLimitedAsync(Stream source, Stream target, CancellationToken token) { var buffer = new byte[81920]; var total = 0; int read; while ((read = await source.ReadAsync(buffer, token)) > 0) { total += read; if (total > MaxDocumentBytes) throw new InvalidDataException("Manifest is too large."); await target.WriteAsync(buffer.AsMemory(0, read), token); } }
}
