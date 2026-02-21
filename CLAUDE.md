# Synology Photo Frame

A touch-friendly WPF desktop application that connects to a Synology NAS running Synology Photos to display a digital photo frame slideshow.

## Tech Stack

- **.NET 8.0** / **WPF** / **C# 12**
- **CommunityToolkit.Mvvm** (v8.4.0) — MVVM pattern with `[ObservableProperty]` and `[RelayCommand]`
- **Microsoft.Extensions.DependencyInjection** — DI container
- **Microsoft.Extensions.Http** — HttpClient factory
- **System.Security.Cryptography.ProtectedData** — DPAPI password encryption

## Project Structure

```
src/SynologyPhotoFrame/
├── App.xaml(.cs)                        # Entry point, DI setup, PowerHelper init
├── MainWindow.xaml(.cs)                 # Shell window with ContentControl navigation
├── SynologyPhotoFrame.csproj
├── Resources/
│   ├── Styles/
│   │   ├── Colors.xaml                  # Theme colors (light theme)
│   │   ├── ButtonStyles.xaml            # PrimaryButton, GhostButton, IconButton
│   │   └── TouchStyles.xaml             # Touch-friendly input controls
│   └── app.ico                          # Application icon
├── Views/
│   ├── LoginView.xaml(.cs)              # NAS authentication
│   ├── AlbumSelectionView.xaml(.cs)     # Browse & select albums/people
│   └── SlideshowView.xaml(.cs)          # Photo slideshow display
├── ViewModels/
│   ├── ViewModelBase.cs                 # Base class (IsLoading, ErrorMessage)
│   ├── LoginViewModel.cs               # Login logic, auto-reconnect
│   ├── AlbumSelectionViewModel.cs       # 3-tab browsing (Albums/People/TeamPeople)
│   ├── SlideshowViewModel.cs            # Slideshow engine, timers, scheduling
│   └── SettingsViewModel.cs             # Settings editing & persistence
├── Models/
│   ├── AppSettings.cs                   # All persisted configuration
│   ├── Album.cs                         # Album model with cover image
│   ├── Person.cs                        # Person model (face recognition)
│   ├── PhotoItem.cs                     # Photo metadata
│   ├── TransitionType.cs                # Transition enum
│   └── SynologyApiResponse.cs           # API response DTOs
├── Services/
│   ├── SynologyApiService.cs            # Synology Photos REST API client
│   ├── NavigationService.cs             # ViewModel-based navigation
│   ├── SettingsService.cs               # JSON settings + DPAPI encryption
│   ├── ImageCacheService.cs             # LRU image cache (500MB max)
│   └── Interfaces/                      # Service contracts
├── Controls/
│   └── TransitionPresenter.cs           # Animated image transition control
├── Converters/
│   ├── BoolToVisibilityConverter.cs
│   ├── NullToVisibilityConverter.cs
│   └── SimpleConverters.cs              # 5 specialized converters
└── Helpers/
    ├── PowerHelper.cs                   # Sleep/wake, display, lock screen (P/Invoke)
    ├── FullScreenHelper.cs              # Fullscreen toggle
    ├── OnScreenKeyboardHelper.cs        # Touch keyboard (TabTip.exe)
    └── TouchGestureHelper.cs            # Swipe & tap detection
```

## Architecture

- **MVVM** with CommunityToolkit.Mvvm — `[ObservableProperty]`, `[RelayCommand]`
- **Dependency Injection** — Singletons for services, Transient for ViewModels
- **Navigation** — `INavigationService` swaps ViewModels; `MainWindow` maps them to Views via DataTemplates
- **Async/await** throughout — Non-blocking UI

### DI Registration (App.xaml.cs)

```csharp
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<ISynologyApiService, SynologyApiService>();
services.AddSingleton<IImageCacheService, ImageCacheService>();
services.AddSingleton<INavigationService, NavigationService>();
services.AddTransient<LoginViewModel>();
services.AddTransient<AlbumSelectionViewModel>();
services.AddTransient<SlideshowViewModel>();
services.AddTransient<SettingsViewModel>();
```

## Features

### Authentication
- NAS address, port, HTTPS configuration
- Session-based login via Synology API
- "Remember Me" with DPAPI-encrypted password
- Auto session renewal on expiry (error 119)

### Album & People Browsing
- 3 tabs: Personal Albums, People (face recognition), Team Space People
- Card grid layout with cover images
- Multi-select for slideshow sources

### Slideshow
- Configurable intervals: 5, 10, 15, 30, 60 seconds
- 6 transitions: Fade, SlideLeft, SlideRight, ZoomIn, Dissolve, Random
- Shuffle mode
- Prefetch next 3 photos asynchronously
- Touch gestures: swipe left/right, tap for overlay
- Keyboard: arrows, space (pause), F11/F (fullscreen), Escape (back)

### Daily Schedule
- Set active hours (start/end time, supports midnight crossing)
- At end time: brightness set to 0, display turned off, slideshow paused
- At start time: display turned on, brightness set to 100%, slideshow resumes
- System stays awake 24/7 (no sleep/wake cycle)
- Lock screen disabled via `powercfg` (requires admin)

### Power Management (P/Invoke)
- `PreventSleep()` — `SetThreadExecutionState` (system + display) during active playback
- `PreventSleepKeepSystemOn()` — `SetThreadExecutionState` (system only) during inactive schedule
- `AllowSleep()` — Release on exit
- `TurnOnDisplay()` / `TurnOffDisplay()` — `SendMessage` `SC_MONITORPOWER`
- `SetBrightness(int)` — WMI (integrated panels) + DXVA2 DDC/CI (external monitors)
- `ActivateDisplay()` — Composite: PreventSleep + TurnOn + Brightness 100%
- `DeactivateDisplay()` — Composite: PreventSleepKeepSystemOn + Brightness 0 + TurnOff
- `DisableLockOnWake()` — `powercfg CONSOLELOCK 0`

### Image Cache
- Location: `%LOCALAPPDATA%/SynologyPhotoFrame/cache`
- Max 500MB with LRU eviction (keeps 80%)
- Max 3 concurrent downloads

## Synology Photos API

Base URL: `https://{nasAddress}:{port}/webapi`

| Operation | API | Method |
|-----------|-----|--------|
| Login | `SYNO.API.Auth` (auth.cgi) | login (v6) |
| List Albums | `SYNO.Foto.Browse.Album` | list |
| List People | `SYNO.Foto.Browse.Person` | list |
| List Team People | `SYNO.FotoTeam.Browse.Person` | list |
| Album Photos | `SYNO.Foto.Browse.Item` | list (v1, album_id, start_time?, end_time?) |
| Person Photos | `SYNO.Foto.Browse.Item` | list (v4, person_id, start_time?, end_time?) |
| Team Person Photos | `SYNO.FotoTeam.Browse.Item` | list (v4, person_id, start_time?, end_time?) |
| Thumbnail | `SYNO.Foto.Thumbnail` | get (size: sm/m/xl) |
| Download | `SYNO.Foto.Download` | download (unit_id) |

All API calls use POST to `/webapi/entry.cgi` with form-encoded params. Session ID (`_sid`) passed as query parameter.

## Settings

Stored at `%APPDATA%\SynologyPhotoFrame\settings.json`:

```jsonc
{
  "nasAddress": "192.168.1.100",
  "port": 5001,
  "useHttps": true,
  "username": "admin",
  "encryptedPassword": "[DPAPI base64]",
  "rememberMe": true,
  "selectedAlbumIds": [1, 2],
  "selectedPersonIds": [5],
  "selectedTeamPersonIds": [],
  "intervalSeconds": 10,
  "transitionType": 0,
  "transitionDurationSeconds": 1.0,
  "shufflePhotos": true,
  "showClock": true,
  "showPhotoInfo": false,
  "photoSizePreference": "xl",
  "scheduleEnabled": false,
  "scheduleStartTime": "08:00",
  "scheduleEndTime": "22:00",
  "photoFilterStartDate": "2025-01-01T00:00:00",
  "photoFilterEndDate": null
}
```

## Build & Run

```bash
# Debug
dotnet build -c Debug
dotnet run -c Debug

# Publish standalone single-file exe (no .NET required on target)
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true
```

## Color Theme

```
Primary Dark:    #F0F4F8  (light background)
Secondary Dark:  #FFFFFF  (white cards/panels)
Accent:          #3B82F6  (blue actions)
Highlight:       #14B8A6  (teal)
Text Primary:    #1E293B  (dark text)
Text Secondary:  #64748B  (gray text)
Border:          #E2E8F0  (card/separator borders)
```

## Application Flow

```
App.OnStartup → DisableLockOnWake → DI setup
  → LoginView: Enter NAS credentials → API login
  → AlbumSelectionView: Browse albums/people → select sources
  → SlideshowView: Load photos → start timers → display slideshow
    ├── Slideshow timer: advance photos with transitions
    ├── Clock timer: update time display + check schedule
    └── Overlay timer: auto-hide controls after 3s
```
