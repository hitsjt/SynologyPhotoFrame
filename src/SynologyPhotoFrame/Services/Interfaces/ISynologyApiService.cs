using SynologyPhotoFrame.Models;

namespace SynologyPhotoFrame.Services.Interfaces;

public interface ISynologyApiService
{
    string? SessionId { get; }
    bool IsLoggedIn { get; }
    void Configure(string nasAddress, int port, bool useHttps);
    Task<bool> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<List<Album>> GetAlbumsAsync(int offset = 0, int limit = 100);
    Task<List<Person>> GetPeopleAsync(int offset = 0, int limit = 100);
    Task<List<Person>> GetTeamPeopleAsync(int offset = 0, int limit = 100);
    Task<List<PhotoItem>> GetAlbumPhotosAsync(int albumId, int offset = 0, int limit = 500);
    Task<List<PhotoItem>> GetPersonPhotosAsync(int personId, int offset = 0, int limit = 500);
    Task<List<PhotoItem>> GetTeamPersonPhotosAsync(int personId, int offset = 0, int limit = 500);
    Task<byte[]?> GetThumbnailAsync(int photoId, string cacheKey, string size = "xl", string type = "unit");
    Task<byte[]?> DownloadPhotoAsync(int photoId, string cacheKey);
}
