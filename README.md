# Synology Photo Frame

A touch-friendly Windows desktop application that turns any PC into a digital photo frame, powered by your Synology NAS and Synology Photos.

## Features

### Synology Photos Integration
- Connect to your Synology NAS via the Synology Photos API
- Browse and select from **personal albums**, **people** (face recognition), and **team space people**
- Multi-select albums and people as slideshow sources
- Filter photos by date range
- Periodically check for newly added photos

### Slideshow
- Configurable intervals (5 / 10 / 15 / 30 / 60 seconds)
- 6 transition effects: Fade, Slide Left, Slide Right, Zoom In, Dissolve, Random
- Shuffle mode with Fisher-Yates algorithm
- Asynchronous prefetch of upcoming photos
- LRU image cache (up to 500 MB in local storage)
- Optional clock and photo info overlay

### Touch & Keyboard Controls
- **Touch**: Swipe left/right to navigate, tap to show/hide overlay
- **Keyboard**: Arrow keys (navigate), Space (pause), F11/F (fullscreen), Escape (back)

### Daily Schedule (Unattended Operation)
- Set active hours with start/end time (supports overnight schedules that cross midnight)
- At end time: dims brightness to 0%, turns off display, allows the system to sleep
- At start time: automatically wakes the system from sleep, turns on display, resumes slideshow
- **No manual power button press or unlock required** -- ideal for always-on photo frame setups
- Dual wake mechanism: Task Scheduler (primary, reliable on Modern Standby) + Waitable Timer (backup for traditional S3 sleep)
- Lock screen automatically disabled on wake via `powercfg` and registry policy
- Tested on Modern Standby devices (e.g. Surface Go 2)

### Security
- Passwords encrypted at rest using Windows DPAPI
- Self-signed NAS certificates accepted only for private/local network addresses
- Session-based authentication with auto-renewal on expiry

## Requirements

- **Windows 10 or later** (x64)
- **Synology NAS** running **Synology Photos** (DSM 7+)
- Network connectivity between the PC and NAS
- Administrator privileges (required for power management features)

## Installation

### Option 1: Download Release

Download the latest standalone `.exe` from the [Releases](../../releases) page. No .NET runtime installation required.

### Option 2: Build from Source

Prerequisites: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Clone the repository
git clone https://github.com/hitsjt/PhotoFrame.git
cd PhotoFrame

# Run in debug mode
dotnet run --project src/SynologyPhotoFrame -c Debug

# Publish a standalone single-file executable
dotnet publish src/SynologyPhotoFrame -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:IncludeAllContentForSelfExtract=true
```

The published executable will be in `src/SynologyPhotoFrame/bin/Release/net8.0-windows/win-x64/publish/`.

## Usage

1. **Login** -- Enter your Synology NAS address, port, and credentials. Enable "Remember Me" to save for next launch.
2. **Select Sources** -- Browse the three tabs (Albums / People / Team People) and select one or more sources for the slideshow.
3. **Slideshow** -- Photos are displayed fullscreen with your configured transitions and interval.

### Settings

Access settings from the slideshow overlay (tap the screen or press any key):

| Setting | Description |
|---------|-------------|
| Interval | Time between photo transitions (5-60 seconds) |
| Transition | Animation effect between photos |
| Shuffle | Randomize photo order |
| Show Clock | Display current time on screen |
| Show Photo Info | Display filename on screen |
| Photo Size | Image quality (sm / m / xl) |
| Schedule | Enable daily on/off schedule with start and end times |
| Photo Date Filter | Only show photos from a specific date range |

### Configuration File

Settings are stored at `%APPDATA%\SynologyPhotoFrame\settings.json`. The image cache is stored at `%LOCALAPPDATA%\SynologyPhotoFrame\cache`.

## Setting Up as a Dedicated Photo Frame

For the best unattended photo frame experience (e.g. on a Surface Go):

1. **Enable the daily schedule** in settings with your desired active hours
2. **Set the app to run at startup** (add a shortcut to `shell:startup`)
3. The app will:
   - Keep the display on during active hours
   - Dim and turn off the display at the scheduled end time
   - Allow the system to sleep for minimal power consumption
   - Automatically wake the system and resume the slideshow at the scheduled start time
   - Bypass the lock screen on wake (no password/PIN prompt)

> **Note**: The application requires administrator privileges to configure power settings (wake timers, lock screen bypass). It will prompt for UAC elevation on launch.

## Tech Stack

- .NET 8.0 / WPF / C# 12
- CommunityToolkit.Mvvm -- MVVM pattern
- Microsoft.Extensions.DependencyInjection -- DI container
- Microsoft.Extensions.Http -- HttpClient factory
- System.Security.Cryptography.ProtectedData -- DPAPI encryption
- Win32 P/Invoke -- Power management, display control, input simulation

## License

MIT
