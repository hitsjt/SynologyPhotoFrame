using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SynologyPhotoFrame.Helpers;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.ViewModels;

public partial class SlideshowViewModel : ViewModelBase
{
    private readonly ISynologyApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly IImageCacheService _cacheService;
    private readonly INavigationService _navigationService;

    private List<PhotoItem> _photoList = new();
    private List<int> _displayOrder = new();
    private int _currentOrderIndex = -1;
    private DispatcherTimer? _slideshowTimer;
    private DispatcherTimer? _overlayTimer;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _refreshTimer;
    private AppSettings _settings = new();
    private readonly Random _random = Random.Shared;
    private readonly HashSet<int> _loadedPhotoIds = new();
    private bool _isAdvancing;
    private bool _isRefreshing;

    [ObservableProperty]
    private BitmapImage? _currentImage;

    [ObservableProperty]
    private BitmapImage? _nextImage;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isOverlayVisible;

    [ObservableProperty]
    private string _currentPhotoInfo = string.Empty;

    [ObservableProperty]
    private string _currentTimeDisplay = string.Empty;

    [ObservableProperty]
    private bool _showClock = true;

    [ObservableProperty]
    private bool _showPhotoInfo;

    [ObservableProperty]
    private TransitionType _currentTransition;

    [ObservableProperty]
    private double _transitionDurationSeconds = 1.0;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private int _totalPhotos;

    [ObservableProperty]
    private string _loadingProgress = string.Empty;

    [ObservableProperty]
    private bool _isSchedulePaused;

    public SlideshowViewModel(ISynologyApiService apiService, ISettingsService settingsService,
        IImageCacheService cacheService, INavigationService navigationService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _cacheService = cacheService;
        _navigationService = navigationService;
    }

    public override async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();
        ShowClock = _settings.ShowClock;
        ShowPhotoInfo = _settings.ShowPhotoInfo;
        CurrentTransition = _settings.TransitionType;
        TransitionDurationSeconds = _settings.TransitionDurationSeconds;

        // Convert date filter to Unix timestamps for API
        var startTime = _settings.PhotoFilterStartDate.HasValue
            ? new DateTimeOffset(_settings.PhotoFilterStartDate.Value.Date).ToUnixTimeSeconds()
            : (long?)null;
        var endTime = _settings.PhotoFilterEndDate.HasValue
            ? new DateTimeOffset(_settings.PhotoFilterEndDate.Value.Date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds()
            : (long?)null;

        IsLoading = true;
        LoadingProgress = "Loading photos...";

        try
        {
            // Load first batch from each source in parallel for fast startup
            var firstBatchTasks = new List<Task<List<PhotoItem>>>();

            foreach (var albumId in _settings.SelectedAlbumIds)
                firstBatchTasks.Add(_apiService.GetAlbumPhotosAsync(albumId, 0, 500, startTime, endTime));

            foreach (var personId in _settings.SelectedPersonIds)
                firstBatchTasks.Add(_apiService.GetPersonPhotosAsync(personId, 0, 500, startTime, endTime));

            foreach (var personId in _settings.SelectedTeamPersonIds)
                firstBatchTasks.Add(_apiService.GetTeamPersonPhotosAsync(personId, 0, 500, startTime, endTime));

            var results = await Task.WhenAll(firstBatchTasks);

            foreach (var batch in results)
            {
                AddPhotosToList(batch);
            }

            if (_photoList.Count == 0)
            {
                ErrorMessage = "No photos found in selected albums/people.";
                LoadingProgress = string.Empty;
                return;
            }

            // Build display order and start slideshow immediately
            RebuildDisplayOrder();
            StartTimers();
            await ShowNextPhotoAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load photos: {ex.Message}";
            LoadingProgress = string.Empty;
            return;
        }
        finally
        {
            IsLoading = false;
        }

        // Continue loading remaining photos in the background
        _ = LoadRemainingPhotosAsync();
    }

    private void AddPhotosToList(List<PhotoItem> photos)
    {
        foreach (var photo in photos)
        {
            // Skip photos without valid cache key (no thumbnail available)
            if (string.IsNullOrEmpty(photo.CacheKey)) continue;
            if (_loadedPhotoIds.Add(photo.Id))
            {
                _photoList.Add(photo);
            }
        }
        TotalPhotos = _photoList.Count;
    }

    private void RebuildDisplayOrder()
    {
        var currentCount = _displayOrder.Count;
        var newCount = _photoList.Count;

        // Add new indices
        for (int i = currentCount; i < newCount; i++)
        {
            _displayOrder.Add(i);
        }

        if (_settings.ShufflePhotos && _displayOrder.Count > 0)
        {
            // Only shuffle the newly added portion to avoid disrupting the current playback
            for (int i = _displayOrder.Count - 1; i > currentCount; i--)
            {
                int j = currentCount + _random.Next(i - currentCount + 1);
                (_displayOrder[i], _displayOrder[j]) = (_displayOrder[j], _displayOrder[i]);
            }
        }

        TotalPhotos = _photoList.Count;
    }

    private async Task LoadRemainingPhotosAsync()
    {
        // Convert date filter to Unix timestamps for API
        var startTime = _settings.PhotoFilterStartDate.HasValue
            ? new DateTimeOffset(_settings.PhotoFilterStartDate.Value.Date).ToUnixTimeSeconds()
            : (long?)null;
        var endTime = _settings.PhotoFilterEndDate.HasValue
            ? new DateTimeOffset(_settings.PhotoFilterEndDate.Value.Date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds()
            : (long?)null;

        try
        {
            var tasks = new List<Task>();

            foreach (var albumId in _settings.SelectedAlbumIds)
                tasks.Add(LoadRemainingFromSourceAsync(
                    (offset, limit) => _apiService.GetAlbumPhotosAsync(albumId, offset, limit, startTime, endTime)));

            foreach (var personId in _settings.SelectedPersonIds)
                tasks.Add(LoadRemainingFromSourceAsync(
                    (offset, limit) => _apiService.GetPersonPhotosAsync(personId, offset, limit, startTime, endTime)));

            foreach (var personId in _settings.SelectedTeamPersonIds)
                tasks.Add(LoadRemainingFromSourceAsync(
                    (offset, limit) => _apiService.GetTeamPersonPhotosAsync(personId, offset, limit, startTime, endTime)));

            await Task.WhenAll(tasks);

            // Full shuffle after all photos are loaded to mix across sources/batches
            if (_settings.ShufflePhotos && _displayOrder.Count > 0)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ShuffleList(_displayOrder);
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Slideshow] Background loading error: {ex.Message}");
        }
        finally
        {
            LoadingProgress = string.Empty;
        }
    }

    private async Task LoadRemainingFromSourceAsync(Func<int, int, Task<List<PhotoItem>>> fetchFunc)
    {
        int offset = 500; // First batch (0-499) already loaded
        const int limit = 500;

        while (true)
        {
            var batch = await fetchFunc(offset, limit);
            if (batch.Count == 0) break;

            // Marshal back to UI thread to update collections
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var beforeCount = _photoList.Count;
                AddPhotosToList(batch);

                if (_photoList.Count > beforeCount)
                {
                    RebuildDisplayOrder();
                    LoadingProgress = $"Loading... {_photoList.Count} photos";
                }
            });

            if (batch.Count < limit) break;
            offset += limit;
        }
    }

    private async Task RefreshPhotosAsync()
    {
        if (_isRefreshing || IsSchedulePaused) return;
        _isRefreshing = true;

        try
        {
            var startTime = _settings.PhotoFilterStartDate.HasValue
                ? new DateTimeOffset(_settings.PhotoFilterStartDate.Value.Date).ToUnixTimeSeconds()
                : (long?)null;
            var endTime = _settings.PhotoFilterEndDate.HasValue
                ? new DateTimeOffset(_settings.PhotoFilterEndDate.Value.Date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds()
                : (long?)null;

            var beforeCount = _photoList.Count;
            var tasks = new List<Task>();

            foreach (var albumId in _settings.SelectedAlbumIds)
                tasks.Add(RefreshFromSourceAsync(
                    (offset, limit) => _apiService.GetAlbumPhotosAsync(albumId, offset, limit, startTime, endTime)));

            foreach (var personId in _settings.SelectedPersonIds)
                tasks.Add(RefreshFromSourceAsync(
                    (offset, limit) => _apiService.GetPersonPhotosAsync(personId, offset, limit, startTime, endTime)));

            foreach (var personId in _settings.SelectedTeamPersonIds)
                tasks.Add(RefreshFromSourceAsync(
                    (offset, limit) => _apiService.GetTeamPersonPhotosAsync(personId, offset, limit, startTime, endTime)));

            await Task.WhenAll(tasks);

            var newCount = _photoList.Count - beforeCount;
            if (newCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Slideshow] Refresh found {newCount} new photo(s), total: {_photoList.Count}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Slideshow] Refresh error: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task RefreshFromSourceAsync(Func<int, int, Task<List<PhotoItem>>> fetchFunc)
    {
        int offset = 0;
        const int limit = 500;

        while (true)
        {
            var batch = await fetchFunc(offset, limit);
            if (batch.Count == 0) break;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var beforeCount = _photoList.Count;
                AddPhotosToList(batch);

                if (_photoList.Count > beforeCount)
                {
                    RebuildDisplayOrder();
                }
            });

            if (batch.Count < limit) break;
            offset += limit;
        }
    }

    private void StartTimers()
    {
        PowerHelper.PreventSleep();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _slideshowTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings.IntervalSeconds)
        };
        _slideshowTimer.Tick += async (s, e) =>
        {
            try
            {
                if (!IsPaused && !IsSchedulePaused)
                    await ShowNextPhotoAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Slideshow] Timer tick error: {ex.Message}");
            }
        };
        _slideshowTimer.Start();

        _overlayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _overlayTimer.Tick += (s, e) =>
        {
            IsOverlayVisible = false;
            _overlayTimer.Stop();
        };

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) =>
        {
            CurrentTimeDisplay = DateTime.Now.ToString("HH:mm");
            CheckSchedule();
        };
        _clockTimer.Start();
        CurrentTimeDisplay = DateTime.Now.ToString("HH:mm");
        CheckSchedule();

        // Photo refresh timer - periodically check for new photos
        if (_settings.PhotoRefreshIntervalMinutes > 0)
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(_settings.PhotoRefreshIntervalMinutes)
            };
            _refreshTimer.Tick += async (s, e) =>
            {
                try
                {
                    await RefreshPhotosAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Slideshow] Refresh timer error: {ex.Message}");
                }
            };
            _refreshTimer.Start();
        }
    }

    private void CheckSchedule()
    {
        if (!_settings.ScheduleEnabled)
        {
            if (IsSchedulePaused)
            {
                IsSchedulePaused = false;
                PowerHelper.CancelScheduledWake();
                PowerHelper.ActivateDisplay();
            }
            return;
        }

        if (!TimeSpan.TryParse(_settings.ScheduleStartTime, out var start) ||
            !TimeSpan.TryParse(_settings.ScheduleEndTime, out var end))
            return;

        var now = DateTime.Now.TimeOfDay;
        bool inSchedule = start <= end
            ? now >= start && now < end
            : now >= start || now < end; // crosses midnight

        if (inSchedule)
        {
            if (IsSchedulePaused)
            {
                // Entering active period: cancel wake timer, turn on display
                IsSchedulePaused = false;
                PowerHelper.CancelScheduledWake();
                PowerHelper.ActivateDisplay();
            }
        }
        else
        {
            if (!IsSchedulePaused)
            {
                // Entering inactive period: turn off display
                IsSchedulePaused = true;
                PowerHelper.DeactivateDisplay();
            }
            // Ensure wake timer is set and system is allowed to sleep.
            // Runs every tick while awake during inactive period, but
            // ScheduleSystemWake skips re-creation if already set for the same time.
            var nextWake = CalculateNextWakeTime(start);
            if (PowerHelper.ScheduleSystemWake(nextWake))
            {
                PowerHelper.AllowSleep();
            }
            // else: wake timer failed — DeactivateDisplay already set
            // PreventSleepKeepSystemOn as fallback (system stays on with display off)
        }
    }

    private static DateTime CalculateNextWakeTime(TimeSpan startTime)
    {
        var wakeToday = DateTime.Today + startTime;
        return wakeToday > DateTime.Now ? wakeToday : wakeToday.AddDays(1);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        System.Diagnostics.Debug.WriteLine("[Slideshow] System resumed from sleep");
        PowerHelper.ActivateDisplay();

        // Dispatch to UI thread since PowerModeChanged fires on a system thread
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // Re-check schedule first — if it's now active period, activate display
            CheckSchedule();

            // If still in inactive period, the display turned on during resume.
            // Turn it back off. Don't call DeactivateDisplay() here because its
            // PreventSleepKeepSystemOn() would override AllowSleep() from CheckSchedule,
            // preventing the system from going back to sleep.
            // AllowSleep already cleared ES_DISPLAY_REQUIRED, so TurnOffDisplay works.
            if (IsSchedulePaused)
            {
                PowerHelper.SetBrightness(0);
                PowerHelper.TurnOffDisplay();
            }
        });
    }

    [RelayCommand]
    public async Task ShowNextPhotoAsync()
    {
        if (_isAdvancing || _photoList.Count == 0) return;
        _isAdvancing = true;
        try
        {
            _currentOrderIndex++;
            if (_currentOrderIndex >= _displayOrder.Count)
            {
                // Completed a full cycle — re-shuffle for next round
                _currentOrderIndex = 0;
                if (_settings.ShufflePhotos)
                    ShuffleList(_displayOrder);
            }
            await DisplayPhotoAtCurrentIndexAsync();
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    [RelayCommand]
    public async Task ShowPreviousPhotoAsync()
    {
        if (_isAdvancing || _photoList.Count == 0) return;
        _isAdvancing = true;
        try
        {
            _currentOrderIndex--;
            if (_currentOrderIndex < 0) _currentOrderIndex = _displayOrder.Count - 1;
            await DisplayPhotoAtCurrentIndexAsync(direction: -1);
        }
        finally
        {
            _isAdvancing = false;
        }
    }

    private async Task DisplayPhotoAtCurrentIndexAsync(int retryCount = 0, int direction = 1)
    {
        if (retryCount >= 5 || _photoList.Count == 0) return;

        var photoIndex = _displayOrder[_currentOrderIndex];
        var photo = _photoList[photoIndex];
        CurrentIndex = _currentOrderIndex + 1;

        if (_settings.TransitionType == TransitionType.Random)
        {
            var types = Enum.GetValues<TransitionType>().Where(t => t != TransitionType.Random).ToArray();
            CurrentTransition = types[_random.Next(types.Length)];
        }

        try
        {
            var path = await _cacheService.GetOrDownloadAsync(photo.Id, photo.CacheKey, _settings.PhotoSizePreference);
            if (path != null)
            {
                var bitmap = await Task.Run(() =>
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(path, UriKind.Absolute);
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                });

                CurrentImage = bitmap;
                CurrentPhotoInfo = photo.Filename;
            }
            else
            {
                // Download failed, skip to next photo in current direction
                System.Diagnostics.Debug.WriteLine($"[Slideshow] Skipping photo {photo.Id} ({photo.Filename}): download returned null");
                _currentOrderIndex = (_currentOrderIndex + direction + _displayOrder.Count) % _displayOrder.Count;
                await DisplayPhotoAtCurrentIndexAsync(retryCount + 1, direction);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Slideshow] Skipping photo {photo.Id}: {ex.Message}");
            _currentOrderIndex = (_currentOrderIndex + direction + _displayOrder.Count) % _displayOrder.Count;
            await DisplayPhotoAtCurrentIndexAsync(retryCount + 1, direction);
            return;
        }

        var nextPhotos = new List<PhotoItem>();
        for (int i = 1; i <= 3; i++)
        {
            var nextOrderIndex = (_currentOrderIndex + i) % _displayOrder.Count;
            nextPhotos.Add(_photoList[_displayOrder[nextOrderIndex]]);
        }
        _ = _cacheService.PreFetchAsync(nextPhotos, _settings.PhotoSizePreference);

        _slideshowTimer?.Stop();
        _slideshowTimer?.Start();
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
            _slideshowTimer?.Stop();
        else
            _slideshowTimer?.Start();
    }

    [RelayCommand]
    public void ToggleOverlay()
    {
        IsOverlayVisible = !IsOverlayVisible;
        if (IsOverlayVisible)
        {
            _overlayTimer?.Stop();
            _overlayTimer?.Start();
        }
    }

    public void ResetOverlayTimer()
    {
        if (IsOverlayVisible)
        {
            _overlayTimer?.Stop();
            _overlayTimer?.Start();
        }
    }

    [RelayCommand]
    private void BackToSelection()
    {
        StopTimers();
        _navigationService.NavigateTo<AlbumSelectionViewModel>();
    }

    public override void Cleanup() => StopTimers();

    public void StopTimers()
    {
        _slideshowTimer?.Stop();
        _overlayTimer?.Stop();
        _clockTimer?.Stop();
        _refreshTimer?.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        PowerHelper.CancelScheduledWake();

        if (IsSchedulePaused)
        {
            PowerHelper.ActivateDisplay();
        }

        PowerHelper.AllowSleep();
    }

    public void UpdateSettings(AppSettings newSettings)
    {
        var wasShuffled = _settings.ShufflePhotos;
        _settings = newSettings;
        ShowClock = newSettings.ShowClock;
        ShowPhotoInfo = newSettings.ShowPhotoInfo;
        CurrentTransition = newSettings.TransitionType;
        TransitionDurationSeconds = newSettings.TransitionDurationSeconds;

        if (_slideshowTimer != null)
            _slideshowTimer.Interval = TimeSpan.FromSeconds(newSettings.IntervalSeconds);

        // Update refresh timer
        if (newSettings.PhotoRefreshIntervalMinutes > 0)
        {
            if (_refreshTimer == null)
            {
                _refreshTimer = new DispatcherTimer();
                _refreshTimer.Tick += async (s, e) =>
                {
                    try
                    {
                        await RefreshPhotosAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Slideshow] Refresh timer error: {ex.Message}");
                    }
                };
            }
            _refreshTimer.Interval = TimeSpan.FromMinutes(newSettings.PhotoRefreshIntervalMinutes);
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer?.Stop();
        }

        if (newSettings.ShufflePhotos && !wasShuffled && _displayOrder.Count > 0)
        {
            ShuffleList(_displayOrder);
        }

        CheckSchedule();
    }

    private void ShuffleList<T>(List<T> list)
    {
        // Fisher-Yates shuffle: each permutation has equal probability.
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
