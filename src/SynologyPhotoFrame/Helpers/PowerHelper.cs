using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SynologyPhotoFrame.Helpers;

public static class PowerHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateWaitableTimer(IntPtr lpTimerAttributes, bool bManualReset, string? lpTimerName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(IntPtr hTimer, ref long pDueTime, int lPeriod,
        IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    private static IntPtr _wakeTimer = IntPtr.Zero;

    public static void PreventSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }

    public static void AllowSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS);
    }

    /// <summary>
    /// Turn on the display by sending SC_MONITORPOWER broadcast.
    /// </summary>
    public static void TurnOnDisplay()
    {
        SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)(-1));
    }

    /// <summary>
    /// Set a waitable timer to wake the system at the specified time, then put the system to sleep.
    /// </summary>
    public static void ScheduleWakeAndSleep(DateTime wakeTimeUtc)
    {
        CancelWakeTimer();

        _wakeTimer = CreateWaitableTimer(IntPtr.Zero, true, "SynologyPhotoFrameWakeTimer");
        if (_wakeTimer == IntPtr.Zero) return;

        // SetWaitableTimer expects absolute UTC time as FILETIME (positive value)
        long dueTime = wakeTimeUtc.ToFileTimeUtc();

        if (SetWaitableTimer(_wakeTimer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, true))
        {
            // Put system to sleep (not hibernate)
            SetSuspendState(false, false, false);
        }
    }

    /// <summary>
    /// Cancel any pending wake timer.
    /// </summary>
    public static void CancelWakeTimer()
    {
        if (_wakeTimer != IntPtr.Zero)
        {
            CloseHandle(_wakeTimer);
            _wakeTimer = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Disable lock screen on wake from sleep via powercfg.
    /// Requires admin privileges.
    /// </summary>
    public static void DisableLockOnWake()
    {
        try
        {
            RunPowerCfg("/SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0");
            RunPowerCfg("/SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0");
            RunPowerCfg("/SETACTIVE SCHEME_CURRENT");
        }
        catch
        {
            // Requires admin - silently ignore if not available
        }
    }

    private static void RunPowerCfg(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(3000);
    }
}
