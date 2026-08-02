using FeatherLauncher.Core.Services;
namespace FeatherLauncher.Infrastructure.Storage;

public sealed class CacheService(IAppPaths paths) : ICacheService
{
    public Task<long> GetSizeBytesAsync(CancellationToken cancellationToken = default) => Task.Run(() => Directory.Exists(paths.Cache) ? Directory.EnumerateFiles(paths.Cache, "*", SearchOption.AllDirectories).Sum(file => { cancellationToken.ThrowIfCancellationRequested(); try { return new FileInfo(file).Length; } catch (IOException) { return 0; } }) : 0, cancellationToken);
}
