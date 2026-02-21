using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.ViewModels;

public partial class AlbumSelectionViewModel : ViewModelBase
{
    private readonly ISynologyApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IImageCacheService _cacheService;

    [ObservableProperty]
    private ObservableCollection<Album> _albums = new();

    [ObservableProperty]
    private ObservableCollection<Person> _people = new();

    [ObservableProperty]
    private ObservableCollection<Person> _teamPeople = new();

    [ObservableProperty]
    private int _selectedTab;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private DateTime? _photoFilterStartDate;

    [ObservableProperty]
    private DateTime? _photoFilterEndDate;

    public AlbumSelectionViewModel(ISynologyApiService apiService, ISettingsService settingsService,
        INavigationService navigationService, IImageCacheService cacheService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _cacheService = cacheService;
    }

    public override async Task InitializeAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var settings = await _settingsService.LoadAsync();

            PhotoFilterStartDate = settings.PhotoFilterStartDate;
            PhotoFilterEndDate = settings.PhotoFilterEndDate;

            var albums = await _apiService.GetAlbumsAsync(0, 500);
            Albums = new ObservableCollection<Album>(albums);
            foreach (var album in Albums)
            {
                if (settings.SelectedAlbumIds.Contains(album.Id))
                    album.IsSelected = true;
            }

            var people = await _apiService.GetPeopleAsync(0, 500);
            People = new ObservableCollection<Person>(people);
            foreach (var person in People)
            {
                if (settings.SelectedPersonIds.Contains(person.Id))
                    person.IsSelected = true;
            }

            // Load team space people
            try
            {
                var teamPeople = await _apiService.GetTeamPeopleAsync(0, 500);
                foreach (var p in teamPeople)
                    p.IsTeamSpace = true;
                TeamPeople = new ObservableCollection<Person>(teamPeople);
                foreach (var person in TeamPeople)
                {
                    if (settings.SelectedTeamPersonIds.Contains(person.Id))
                        person.IsSelected = true;
                }
            }
            catch
            {
                TeamPeople = new ObservableCollection<Person>();
            }

            UpdateSelectedCount();

            // Load cover images in background
            _ = LoadCoversAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCoversAsync()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        var albumTasks = Albums
            .Where(a => !string.IsNullOrEmpty(a.CoverCacheKey))
            .Select(album => LoadCoverImageAsync(album.CoverUnitId, album.CoverCacheKey, "sm", "unit", img => album.CoverImage = img, dispatcher));

        var personTasks = People.Select(person =>
            LoadPersonCoverAsync(person, "person", dispatcher));

        var teamPersonTasks = TeamPeople.Select(person =>
            LoadPersonCoverAsync(person, "team_person", dispatcher));

        await Task.WhenAll(albumTasks.Concat(personTasks).Concat(teamPersonTasks));
    }

    private async Task LoadPersonCoverAsync(Person person, string personType, Dispatcher dispatcher)
    {
        var cacheKey = person.CoverCacheKey;

        // If no cover info from API, fetch first photo as fallback
        if (string.IsNullOrEmpty(cacheKey))
        {
            try
            {
                var photos = person.IsTeamSpace
                    ? await _apiService.GetTeamPersonPhotosAsync(person.Id, 0, 1)
                    : await _apiService.GetPersonPhotosAsync(person.Id, 0, 1);
                if (photos.Count > 0 && !string.IsNullOrEmpty(photos[0].CacheKey))
                {
                    var unitType = person.IsTeamSpace ? "team_unit" : "unit";
                    await LoadCoverImageAsync(photos[0].UnitId, photos[0].CacheKey, "sm", unitType, img => person.CoverImage = img, dispatcher);
                    return;
                }
            }
            catch { }
            return;
        }

        // Use person.Id with type=person for face thumbnails
        await LoadCoverImageAsync(person.Id, cacheKey, "sm", personType, img => person.CoverImage = img, dispatcher);
    }

    private async Task LoadCoverImageAsync(int id, string cacheKey, string size, string type, Action<BitmapImage> setter, Dispatcher dispatcher)
    {
        try
        {
            var path = await _cacheService.GetOrDownloadAsync(id, cacheKey, size, type);
            if (path != null)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(path, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 220;
                    bi.EndInit();
                    bi.Freeze();
                    setter(bi);
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    private void SelectTab(int tab)
    {
        SelectedTab = tab;
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = Albums.Count(a => a.IsSelected)
            + People.Count(p => p.IsSelected)
            + TeamPeople.Count(p => p.IsSelected);
    }

    [RelayCommand]
    private async Task StartSlideshowAsync()
    {
        if (SelectedCount == 0)
        {
            ErrorMessage = "Please select at least one album or person.";
            return;
        }

        var settings = await _settingsService.LoadAsync();
        settings.SelectedAlbumIds = Albums.Where(a => a.IsSelected).Select(a => a.Id).ToList();
        settings.SelectedPersonIds = People.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        settings.SelectedTeamPersonIds = TeamPeople.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        settings.PhotoFilterStartDate = PhotoFilterStartDate;
        settings.PhotoFilterEndDate = PhotoFilterEndDate;
        await _settingsService.SaveAsync(settings);

        _navigationService.NavigateTo<SlideshowViewModel>();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _apiService.LogoutAsync();
        _navigationService.NavigateTo<LoginViewModel>();
    }
}
