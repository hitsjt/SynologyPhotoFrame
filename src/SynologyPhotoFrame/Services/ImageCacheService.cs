using System.IO;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.Services;

public class ImageCacheService : IImageCacheService
{
    private readonly ISynologyApiService _apiService;
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _downloadSemaphore = new(3, 3);
    private const long MaxCacheSizeBytes = 500 * 1024 * 1024; // 500MB

    public ImageCacheService(ISynologyApiService apiService)
    {
        _apiService = apiService;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynologyPhotoFrame", "cache");
        Directory.CreateDirectory(_cacheDir);
    }

    private string GetCachePath(int photoId, string size, string type = "unit") =>
        Path.Combine(_cacheDir, $"{type}_{photoId}_{size}.jpg");

    public async Task<string?> GetOrDownloadAsync(int photoId, string cacheKey, string size = "xl", string type = "unit")
    {
        var path = GetCachePath(photoId, size, type);
        if (File.Exists(path))
        {
            File.SetLastAccessTime(path, DateTime.Now);
            return path;
        }

        await _downloadSemaphore.WaitAsync();
        try
        {
            if (File.Exists(path)) return path;

            var data = await _apiService.GetThumbnailAsync(photoId, cacheKey, size, type);
            if (data == null || data.Length == 0) return null;

            await File.WriteAllBytesAsync(path, data);
            await EvictIfNeededAsync();
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    public async Task PreFetchAsync(IEnumerable<PhotoItem> photos, string size = "xl")
    {
        var tasks = photos.Select(p => GetOrDownloadAsync(p.Id, p.CacheKey, size));
        await Task.WhenAll(tasks);
    }

    public Task ClearCacheAsync()
    {
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir))
            {
                try { File.Delete(file); } catch { }
            }
        }
        return Task.CompletedTask;
    }

    public long GetCacheSizeBytes()
    {
        if (!Directory.Exists(_cacheDir)) return 0;
        return new DirectoryInfo(_cacheDir)
            .GetFiles()
            .Sum(f => f.Length);
    }

    private Task EvictIfNeededAsync()
    {
        var currentSize = GetCacheSizeBytes();
        if (currentSize <= MaxCacheSizeBytes) return Task.CompletedTask;

        var files = new DirectoryInfo(_cacheDir)
            .GetFiles()
            .OrderBy(f => f.LastAccessTime)
            .ToList();

        foreach (var file in files)
        {
            if (currentSize <= MaxCacheSizeBytes * 0.8) break;
            try
            {
                currentSize -= file.Length;
                file.Delete();
            }
            catch { }
        }
        return Task.CompletedTask;
    }
}
