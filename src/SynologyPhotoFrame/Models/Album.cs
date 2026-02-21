using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace SynologyPhotoFrame.Models;

public class Album : INotifyPropertyChanged
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("item_count")]
    public int ItemCount { get; set; }

    [JsonPropertyName("passphrase")]
    public string? Passphrase { get; set; }

    [JsonPropertyName("additional")]
    public PhotoAdditional? Additional { get; set; }

    [JsonIgnore]
    public string CoverCacheKey => Additional?.Thumbnail?.CacheKey ?? string.Empty;

    [JsonIgnore]
    public int CoverUnitId => Additional?.Thumbnail?.UnitId ?? 0;

    private bool _isSelected;

    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private BitmapImage? _coverImage;

    [JsonIgnore]
    public BitmapImage? CoverImage
    {
        get => _coverImage;
        set
        {
            if (_coverImage != value)
            {
                _coverImage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoverImage)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
