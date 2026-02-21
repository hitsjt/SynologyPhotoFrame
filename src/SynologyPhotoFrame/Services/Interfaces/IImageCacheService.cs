using SynologyPhotoFrame.Models;

namespace SynologyPhotoFrame.Services.Interfaces;

public interface IImageCacheService
{
    Task<string?> GetOrDownloadAsync(int photoId, string cacheKey, string size = "xl", string type = "unit");
    Task PreFetchAsync(IEnumerable<PhotoItem> photos, string size = "xl");
    Task ClearCacheAsync();
    long GetCacheSizeBytes();
}
