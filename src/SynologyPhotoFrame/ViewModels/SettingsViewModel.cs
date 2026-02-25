using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IImageCacheService _cacheService;

    [ObservableProperty]
    private int _intervalSeconds = 10;

    [ObservableProperty]
    private TransitionType _selectedTransition = TransitionType.Fade;

    [ObservableProperty]
    private double _transitionDuration = 1.0;

    [ObservableProperty]
    private bool _shufflePhotos = true;

    [ObservableProperty]
    private bool _showClock = true;

    [ObservableProperty]
    private bool _showPhotoInfo;

    [ObservableProperty]
    private bool _scheduleEnabled;

    [ObservableProperty]
    private string _scheduleStartTime = "08:00";

    [ObservableProperty]
    private string _scheduleEndTime = "22:00";

    [ObservableProperty]
    private int _inactiveBrightness;

    [ObservableProperty]
    private int _photoRefreshIntervalMinutes = 30;

    [ObservableProperty]
    private string _cacheSizeDisplay = "0 MB";

    public List<int> IntervalPresets { get; } = new() { 5, 10, 15, 30, 60 };
    public List<int> RefreshIntervalPresets { get; } = new() { 0, 15, 30, 60, 120, 360, 720, 1440 };
    public TransitionType[] TransitionTypes => Enum.GetValues<TransitionType>();

    public SettingsViewModel(ISettingsService settingsService, IImageCacheService cacheService)
    {
        _settingsService = settingsService;
        _cacheService = cacheService;
    }

    public override async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadAsync();
        IntervalSeconds = settings.IntervalSeconds;
        SelectedTransition = settings.TransitionType;
        TransitionDuration = settings.TransitionDurationSeconds;
        ShufflePhotos = settings.ShufflePhotos;
        ShowClock = settings.ShowClock;
        ShowPhotoInfo = settings.ShowPhotoInfo;
        ScheduleEnabled = settings.ScheduleEnabled;
        ScheduleStartTime = settings.ScheduleStartTime;
        ScheduleEndTime = settings.ScheduleEndTime;
        InactiveBrightness = settings.InactiveBrightness;
        PhotoRefreshIntervalMinutes = settings.PhotoRefreshIntervalMinutes;
        UpdateCacheSize();
    }

    private void UpdateCacheSize()
    {
        var bytes = _cacheService.GetCacheSizeBytes();
        CacheSizeDisplay = bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    [RelayCommand]
    private async Task SaveAndCloseAsync()
    {
        var settings = await _settingsService.LoadAsync();
        settings.IntervalSeconds = IntervalSeconds;
        settings.TransitionType = SelectedTransition;
        settings.TransitionDurationSeconds = TransitionDuration;
        settings.ShufflePhotos = ShufflePhotos;
        settings.ShowClock = ShowClock;
        settings.ShowPhotoInfo = ShowPhotoInfo;
        settings.ScheduleEnabled = ScheduleEnabled;
        settings.ScheduleStartTime = ScheduleStartTime;
        settings.ScheduleEndTime = ScheduleEndTime;
        settings.InactiveBrightness = InactiveBrightness;
        settings.PhotoRefreshIntervalMinutes = PhotoRefreshIntervalMinutes;
        await _settingsService.SaveAsync(settings);
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await _cacheService.ClearCacheAsync();
        UpdateCacheSize();
    }
}
