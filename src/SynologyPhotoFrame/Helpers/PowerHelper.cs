using System.Diagnostics;
using System.Management;
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
    private static extern bool CancelWaitableTimer(IntPtr hTimer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint dwPhysicalMonitorArraySize,
        [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(
        IntPtr hMonitor, out uint pdwMinimumBrightness,
        out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    private static IntPtr _wakeTimerHandle = IntPtr.Zero;
    private static DateTime _scheduledWakeTime;

    /// <summary>
    /// Prevent system sleep and keep display on.
    /// </summary>
    public static void PreventSleep()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    }

    /// <summary>
    /// Prevent system sleep but allow display to turn off.
    /// </summary>
    public static void PreventSleepKeepSystemOn()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
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
    /// Turn off the display by sending SC_MONITORPOWER broadcast.
    /// </summary>
    public static void TurnOffDisplay()
    {
        SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)2);
    }

    /// <summary>
    /// Set display brightness (0-100). Tries WMI first (integrated panels),
    /// then DXVA2 DDC/CI (external monitors). Returns true if any method succeeded.
    /// </summary>
    public static bool SetBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        if (TrySetBrightnessWmi(percent))
            return true;

        if (TrySetBrightnessDxva2(percent))
            return true;

        Debug.WriteLine("[PowerHelper] Brightness control not available on this hardware");
        return false;
    }

    /// <summary>
    /// Activate display for slideshow: turn on, max brightness, prevent sleep + display off.
    /// Uses mouse simulation for reliable wake on Modern Standby devices (e.g. Surface).
    /// </summary>
    public static void ActivateDisplay()
    {
        PreventSleep();
        SimulateMouseMove();
        TurnOnDisplay();
        SimulateMouseMove();
        TurnOnDisplay();
        SetBrightness(100);
    }

    /// <summary>
    /// Deactivate display for schedule off: dim to zero, turn off display, keep system awake.
    /// </summary>
    public static void DeactivateDisplay()
    {
        // Must remove ES_DISPLAY_REQUIRED first so display can turn off
        PreventSleepKeepSystemOn();
        SetBrightness(0);
        TurnOffDisplay();
    }

    /// <summary>
    /// Configure power settings for unattended operation:
    /// - Disable lock screen on wake from sleep (CONSOLELOCK)
    /// - Enable wake timers so ScheduleSystemWake can wake from sleep (RTCWAKE)
    /// Requires admin privileges.
    /// </summary>
    public static void DisableLockOnWake()
    {
        try
        {
            // Disable console lock on wake
            RunPowerCfg("/SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0");
            RunPowerCfg("/SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0");

            // Enable wake timers (Sleep subgroup / Allow wake timers setting)
            // Without this, SetWaitableTimer(fResume=true) cannot wake the system
            const string sleepSubgroup = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
            const string wakeTimerSetting = "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d";
            RunPowerCfg($"/SETACVALUEINDEX SCHEME_CURRENT {sleepSubgroup} {wakeTimerSetting} 1");
            RunPowerCfg($"/SETDCVALUEINDEX SCHEME_CURRENT {sleepSubgroup} {wakeTimerSetting} 1");

            RunPowerCfg("/SETACTIVE SCHEME_CURRENT");
        }
        catch
        {
            // Requires admin - silently ignore if not available
        }
    }

    /// <summary>
    /// Schedule a system wake-up at the specified time using a waitable timer with fResume=true.
    /// The timer will wake the system from sleep (S3 or Modern Standby).
    /// Skips re-creation if already set for the same time.
    /// Returns false if timer creation/setting failed — caller should keep the system awake as fallback.
    /// </summary>
    public static bool ScheduleSystemWake(DateTime wakeTime)
    {
        if (_wakeTimerHandle != IntPtr.Zero && _scheduledWakeTime == wakeTime)
            return true; // Already set for this time

        CancelScheduledWake();

        _wakeTimerHandle = CreateWaitableTimer(IntPtr.Zero, true, null);
        if (_wakeTimerHandle == IntPtr.Zero)
        {
            Debug.WriteLine("[PowerHelper] Failed to create wake timer");
            return false;
        }

        long dueTime = wakeTime.ToFileTime();
        if (SetWaitableTimer(_wakeTimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, true))
        {
            _scheduledWakeTime = wakeTime;
            Debug.WriteLine($"[PowerHelper] Wake timer set for {wakeTime:yyyy-MM-dd HH:mm:ss}");
            return true;
        }
        else
        {
            Debug.WriteLine("[PowerHelper] Failed to set wake timer");
            CloseHandle(_wakeTimerHandle);
            _wakeTimerHandle = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>
    /// Cancel any pending scheduled wake-up and release the timer handle.
    /// </summary>
    public static void CancelScheduledWake()
    {
        if (_wakeTimerHandle != IntPtr.Zero)
        {
            CancelWaitableTimer(_wakeTimerHandle);
            CloseHandle(_wakeTimerHandle);
            _wakeTimerHandle = IntPtr.Zero;
            _scheduledWakeTime = default;
            Debug.WriteLine("[PowerHelper] Wake timer cancelled");
        }
    }

    /// <summary>
    /// Simulate a small mouse movement to wake display on Modern Standby devices.
    /// SC_MONITORPOWER alone is unreliable on Surface and other Modern Standby hardware.
    /// </summary>
    private static void SimulateMouseMove()
    {
        var inputs = new INPUT[]
        {
            new()
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = 1, dy = 0, dwFlags = MOUSEEVENTF_MOVE }
            }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        Debug.WriteLine("[PowerHelper] Simulated mouse move for display wake");
    }

    private static bool TrySetBrightnessWmi(int percent)
    {
        try
        {
            var scope = new ManagementScope("root\\WMI");
            var query = new SelectQuery("WmiMonitorBrightnessMethods");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    obj.InvokeMethod("WmiSetBrightness",
                        new object[] { (uint)1, (byte)percent });
                    Debug.WriteLine($"[PowerHelper] WMI brightness set to {percent}%");
                    return true;
                }
            }
        }
        catch (ManagementException ex)
        {
            Debug.WriteLine($"[PowerHelper] WMI brightness not supported: {ex.Message}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerHelper] WMI brightness error: {ex.Message}");
        }
        return false;
    }

    private static bool TrySetBrightnessDxva2(int percent)
    {
        try
        {
            var hMonitor = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
            if (hMonitor == IntPtr.Zero) return false;

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint monitorCount) || monitorCount == 0)
                return false;

            var monitors = new PHYSICAL_MONITOR[monitorCount];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, monitorCount, monitors))
                return false;

            bool success = false;
            try
            {
                foreach (var monitor in monitors)
                {
                    if (GetMonitorBrightness(monitor.hPhysicalMonitor,
                        out uint minBrightness, out uint _, out uint maxBrightness))
                    {
                        uint scaledValue = (uint)(minBrightness +
                            (maxBrightness - minBrightness) * percent / 100.0);
                        if (SetMonitorBrightness(monitor.hPhysicalMonitor, scaledValue))
                        {
                            Debug.WriteLine($"[PowerHelper] DXVA2 brightness set to {percent}% (value: {scaledValue})");
                            success = true;
                        }
                    }
                }
            }
            finally
            {
                foreach (var monitor in monitors)
                {
                    if (monitor.hPhysicalMonitor != IntPtr.Zero)
                        DestroyPhysicalMonitor(monitor.hPhysicalMonitor);
                }
            }
            return success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerHelper] DXVA2 brightness error: {ex.Message}");
            return false;
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
