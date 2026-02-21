using System.Windows;

namespace SynologyPhotoFrame.Helpers;

public static class FullScreenHelper
{
    private static WindowState _previousState;
    private static WindowStyle _previousStyle;
    private static ResizeMode _previousResizeMode;

    public static bool IsFullScreen { get; private set; }

    public static void ToggleFullScreen(Window window)
    {
        if (IsFullScreen)
            ExitFullScreen(window);
        else
            EnterFullScreen(window);
    }

    public static void EnterFullScreen(Window window)
    {
        if (IsFullScreen) return;

        _previousState = window.WindowState;
        _previousStyle = window.WindowStyle;
        _previousResizeMode = window.ResizeMode;

        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.WindowState = WindowState.Maximized;
        window.Topmost = true;

        IsFullScreen = true;
    }

    public static void ExitFullScreen(Window window)
    {
        if (!IsFullScreen) return;

        window.WindowStyle = _previousStyle;
        window.ResizeMode = _previousResizeMode;
        window.WindowState = _previousState;
        window.Topmost = false;

        IsFullScreen = false;
    }
}
