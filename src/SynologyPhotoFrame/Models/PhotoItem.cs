using System.Text.Json.Serialization;

namespace SynologyPhotoFrame.Models;

public class PhotoItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("filesize")]
    public long Filesize { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public int OwnerId { get; set; }

    [JsonPropertyName("additional")]
    public PhotoAdditional? Additional { get; set; }

    /// <summary>
    /// Synology Photos thumbnail/download token for this photo.
    /// Must be sent as query parameter "cache_key" when requesting image bytes.
    /// </summary>
    [JsonIgnore]
    public string CacheKey => Additional?.Thumbnail?.CacheKey ?? string.Empty;

    [JsonIgnore]
    public int UnitId => Additional?.Thumbnail?.UnitId ?? Id;
}

public class PhotoAdditional
{
    [JsonPropertyName("thumbnail")]
    public ThumbnailInfo? Thumbnail { get; set; }

    [JsonPropertyName("resolution")]
    public ResolutionInfo? Resolution { get; set; }

    [JsonPropertyName("orientation")]
    public int? Orientation { get; set; }
}

public class ThumbnailInfo
{
    /// <summary>
    /// Server-generated token used with thumbnail/download APIs to retrieve image data.
    /// </summary>
    [JsonPropertyName("cache_key")]
    public string CacheKey { get; set; } = string.Empty;

    [JsonPropertyName("unit_id")]
    public int UnitId { get; set; }

    [JsonPropertyName("sm")]
    public string? Sm { get; set; }

    [JsonPropertyName("m")]
    public string? M { get; set; }

    [JsonPropertyName("xl")]
    public string? Xl { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }
}

public class ResolutionInfo
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
