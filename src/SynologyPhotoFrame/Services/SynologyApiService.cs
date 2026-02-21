using System.Net.Http;
using System.Text.Json;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.Services;

public class SynologyApiService : ISynologyApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _loginSemaphore = new(1, 1);
    private string _baseUrl = string.Empty;
    private string? _sessionId;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string? _lastError;

    public string? SessionId => _sessionId;
    public bool IsLoggedIn => !string.IsNullOrEmpty(_sessionId);

    public SynologyApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public void Configure(string nasAddress, int port, bool useHttps)
    {
        var scheme = useHttps ? "https" : "http";
        _baseUrl = $"{scheme}://{nasAddress}:{port}";
    }

    private HttpClient CreateClient()
    {
        return _httpClientFactory.CreateClient("SynologyApi");
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        _username = username;
        _password = password;

        var client = CreateClient();
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Auth",
            ["method"] = "login",
            ["version"] = "6",
            ["account"] = username,
            ["passwd"] = password,
            ["session"] = "SynologyPhotos",
            ["format"] = "sid"
        };

        var content = new FormUrlEncodedContent(parameters);
        using var response = await client.PostAsync($"{_baseUrl}/webapi/auth.cgi", content);

        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SynologyAPI] Login HTTP error: {response.StatusCode}");
            return false;
        }

        var json = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                _sessionId = root.GetProperty("data").GetProperty("sid").GetString();
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynologyAPI] Login response parse error: {ex.Message}");
        }
        return false;
    }

    public async Task LogoutAsync()
    {
        if (!IsLoggedIn) return;

        var client = CreateClient();
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Auth",
            ["method"] = "logout",
            ["version"] = "6",
            ["session"] = "SynologyPhotos",
            ["_sid"] = _sessionId!
        };

        try
        {
            var content = new FormUrlEncodedContent(parameters);
            using var response = await client.PostAsync($"{_baseUrl}/webapi/auth.cgi", content);
        }
        catch { }
        finally
        {
            _sessionId = null;
        }
    }

    private async Task<JsonElement?> PostApiAsync(Dictionary<string, string> parameters, string? endpoint = null)
    {
        if (_sessionId != null)
            parameters["_sid"] = _sessionId;

        var client = CreateClient();
        var url = endpoint ?? $"{_baseUrl}/webapi/entry.cgi";
        var content = new FormUrlEncodedContent(parameters);
        using var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SynologyAPI] POST HTTP error: {response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                _lastError = null;
                return root.GetProperty("data").Clone();
            }

            // Store error details for debugging
            if (root.TryGetProperty("error", out var error))
            {
                _lastError = error.GetRawText();
                var errorCode = error.TryGetProperty("code", out var code) ? code.GetInt32() : -1;

                // Check for session expiry (error code 119)
                if (errorCode == 119)
                {
                    if (await RenewSessionAsync())
                    {
                        parameters["_sid"] = _sessionId!;
                        var retryContent = new FormUrlEncodedContent(parameters);
                        using var retryResponse = await client.PostAsync(url, retryContent);

                        if (!retryResponse.IsSuccessStatusCode) return null;

                        json = await retryResponse.Content.ReadAsStringAsync();
                        using var doc2 = JsonDocument.Parse(json);
                        if (doc2.RootElement.GetProperty("success").GetBoolean())
                        {
                            _lastError = null;
                            return doc2.RootElement.GetProperty("data").Clone();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynologyAPI] POST response parse error: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine($"[SynologyAPI] Failed: api={parameters.GetValueOrDefault("api")}, method={parameters.GetValueOrDefault("method")}, error={_lastError}");
        return null;
    }

    /// <summary>
    /// Sends a GET request with query parameters (used for APIs that don't work well with POST form-encoding).
    /// </summary>
    private async Task<JsonElement?> GetApiAsync(Dictionary<string, string> parameters)
    {
        if (_sessionId != null)
            parameters["_sid"] = _sessionId;

        var client = CreateClient();
        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        var url = $"{_baseUrl}/webapi/entry.cgi?{queryString}";
        using var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SynologyAPI] GET HTTP error: {response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                return root.GetProperty("data").Clone();
            }

            if (root.TryGetProperty("error", out var error))
            {
                _lastError = error.GetRawText();
                var errorCode = error.TryGetProperty("code", out var code) ? code.GetInt32() : -1;

                if (errorCode == 119)
                {
                    if (await RenewSessionAsync())
                    {
                        parameters["_sid"] = _sessionId!;
                        queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
                        url = $"{_baseUrl}/webapi/entry.cgi?{queryString}";
                        using var retryResponse = await client.GetAsync(url);

                        if (!retryResponse.IsSuccessStatusCode) return null;

                        json = await retryResponse.Content.ReadAsStringAsync();
                        using var doc2 = JsonDocument.Parse(json);
                        if (doc2.RootElement.GetProperty("success").GetBoolean())
                        {
                            return doc2.RootElement.GetProperty("data").Clone();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynologyAPI] GET response parse error: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine($"[SynologyAPI] GET Failed: api={parameters.GetValueOrDefault("api")}, method={parameters.GetValueOrDefault("method")}, error={_lastError}");
        return null;
    }

    private async Task<bool> RenewSessionAsync()
    {
        await _loginSemaphore.WaitAsync();
        try
        {
            return await LoginAsync(_username, _password);
        }
        finally
        {
            _loginSemaphore.Release();
        }
    }

    public async Task<List<Album>> GetAlbumsAsync(int offset = 0, int limit = 100)
    {
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.Foto.Browse.Album",
            ["method"] = "list",
            ["version"] = "1",
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString(),
            ["additional"] = "[\"thumbnail\"]"
        };

        var data = await PostApiAsync(parameters);
        if (data == null) return new List<Album>();

        var list = data.Value.GetProperty("list");
        return JsonSerializer.Deserialize<List<Album>>(list.GetRawText()) ?? new List<Album>();
    }

    public async Task<List<Person>> GetPeopleAsync(int offset = 0, int limit = 100)
    {
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.Foto.Browse.Person",
            ["method"] = "list",
            ["version"] = "1",
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString(),
            ["additional"] = "[\"thumbnail\"]"
        };

        var data = await PostApiAsync(parameters);
        if (data == null) return new List<Person>();

        var list = data.Value.GetProperty("list");
        return JsonSerializer.Deserialize<List<Person>>(list.GetRawText()) ?? new List<Person>();
    }

    public async Task<List<Person>> GetTeamPeopleAsync(int offset = 0, int limit = 100)
    {
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.FotoTeam.Browse.Person",
            ["method"] = "list",
            ["version"] = "1",
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString(),
            ["additional"] = "[\"thumbnail\"]",
            ["show_more"] = "false",
            ["show_hidden"] = "false"
        };

        var data = await PostApiAsync(parameters);
        if (data == null) return new List<Person>();

        var list = data.Value.GetProperty("list");
        return JsonSerializer.Deserialize<List<Person>>(list.GetRawText()) ?? new List<Person>();
    }

    public async Task<List<PhotoItem>> GetAlbumPhotosAsync(int albumId, int offset = 0, int limit = 500, long? startTime = null, long? endTime = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.Foto.Browse.Item",
            ["method"] = "list",
            ["version"] = "1",
            ["album_id"] = albumId.ToString(),
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString(),
            ["type"] = "photo",
            ["additional"] = "[\"thumbnail\",\"resolution\",\"orientation\"]"
        };

        if (startTime.HasValue) parameters["start_time"] = startTime.Value.ToString();
        if (endTime.HasValue) parameters["end_time"] = endTime.Value.ToString();

        var data = await PostApiAsync(parameters);
        if (data == null) return new List<PhotoItem>();

        var list = data.Value.GetProperty("list");
        return JsonSerializer.Deserialize<List<PhotoItem>>(list.GetRawText()) ?? new List<PhotoItem>();
    }

    public async Task<List<PhotoItem>> GetPersonPhotosAsync(int personId, int offset = 0, int limit = 500, long? startTime = null, long? endTime = null)
    {
        // Use SYNO.Foto.Browse.Item method=list with person_id parameter (version 4)
        // This matches the actual Synology Photos web UI API format
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.Foto.Browse.Item",
            ["method"] = "list",
            ["version"] = "4",
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString(),
            ["person_id"] = personId.ToString(),
            ["additional"] = "[\"thumbnail\",\"resolution\",\"orientation\"]"
        };

        if (startTime.HasValue) parameters["start_time"] = startTime.Value.ToString();
        if (endTime.HasValue) parameters["end_time"] = endTime.Value.ToString();

        var data = await PostApiAsync(parameters);
        if (data != null && data.Value.TryGetProperty("list", out var list))
        {
            return JsonSerializer.Deserialize<List<PhotoItem>>(list.GetRawText()) ?? new List<PhotoItem>();
        }

        return new List<PhotoItem>();
    }

    public async Task<List<PhotoItem>> GetTeamPersonPhotosAsync(int personId, int offset = 0, int limit = 500, long? startTime = null, long? endTime = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["api"] = "SYNO.FotoTeam.Browse.Item",
            ["method"] = "list",
            ["version"] = "4",
            ["offset"] = offset.ToString(),
            ["limit"] = limit.ToString(),
            ["person_id"] = personId.ToString(),
            ["additional"] = "[\"thumbnail\",\"resolution\",\"orientation\"]"
        };

        if (startTime.HasValue) parameters["start_time"] = startTime.Value.ToString();
        if (endTime.HasValue) parameters["end_time"] = endTime.Value.ToString();

        var data = await PostApiAsync(parameters);
        if (data != null && data.Value.TryGetProperty("list", out var list))
        {
            return JsonSerializer.Deserialize<List<PhotoItem>>(list.GetRawText()) ?? new List<PhotoItem>();
        }

        return new List<PhotoItem>();
    }

    public async Task<byte[]?> GetThumbnailAsync(int photoId, string cacheKey, string size = "xl", string type = "unit")
    {
        return await GetThumbnailCoreAsync(photoId, cacheKey, size, type, retried: false);
    }

    private async Task<byte[]?> GetThumbnailCoreAsync(int photoId, string cacheKey, string size, string type, bool retried)
    {
        if (!IsLoggedIn) return null;

        // Parse team space prefix: "team_person" -> api=SYNO.FotoTeam.Thumbnail, type=person
        var isTeam = type.StartsWith("team_");
        var apiType = isTeam ? type.Substring(5) : type;
        var apiName = isTeam ? "SYNO.FotoTeam.Thumbnail" : "SYNO.Foto.Thumbnail";

        var client = CreateClient();
        var url = $"{_baseUrl}/webapi/entry.cgi/{apiName}" +
            $"?api={apiName}&version=1&method=get" +
            $"&id={photoId}&cache_key={Uri.EscapeDataString(cacheKey)}&type={Uri.EscapeDataString(apiType)}&size={Uri.EscapeDataString(size)}" +
            $"&_sid={Uri.EscapeDataString(_sessionId ?? "")}";

        using var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            // Check for valid image data (JPEG or PNG magic bytes)
            if (bytes.Length > 4)
            {
                if ((bytes[0] == 0xFF && bytes[1] == 0xD8) || // JPEG
                    (bytes[0] == 0x89 && bytes[1] == 0x50))   // PNG
                    return bytes;
            }
            // If content-type says image, trust it
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image") == true)
                return bytes;

            // Non-image response — check for session expiry and retry once
            if (!retried && IsSessionExpiry(bytes))
            {
                if (await RenewSessionAsync())
                    return await GetThumbnailCoreAsync(photoId, cacheKey, size, type, retried: true);
            }
        }

        return null;
    }

    public async Task<byte[]?> DownloadPhotoAsync(int photoId, string cacheKey)
    {
        return await DownloadPhotoCoreAsync(photoId, cacheKey, retried: false);
    }

    private async Task<byte[]?> DownloadPhotoCoreAsync(int photoId, string cacheKey, bool retried)
    {
        if (!IsLoggedIn) return null;

        var client = CreateClient();
        var url = $"{_baseUrl}/webapi/entry.cgi/SYNO.Foto.Download" +
            $"?api=SYNO.Foto.Download&version=1&method=download" +
            $"&unit_id=[{photoId}]&cache_key={Uri.EscapeDataString(cacheKey)}" +
            $"&_sid={Uri.EscapeDataString(_sessionId ?? "")}";

        using var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();

            // Check for session expiry (API returns JSON error instead of file data)
            if (!retried && bytes.Length < 1024 && IsSessionExpiry(bytes))
            {
                if (await RenewSessionAsync())
                    return await DownloadPhotoCoreAsync(photoId, cacheKey, retried: true);
            }

            return bytes;
        }
        return null;
    }

    private static bool IsSessionExpiry(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.GetBoolean())
                return false;
            return root.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("code", out var code) &&
                   code.GetInt32() == 119;
        }
        catch
        {
            return false;
        }
    }
}
