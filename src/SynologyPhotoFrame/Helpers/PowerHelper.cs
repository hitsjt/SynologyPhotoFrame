using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace SynologyPhotoFrame.Helpers;

public static class PowerHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
    /// </summary>
    public static void ActivateDisplay()
    {
        PreventSleep();
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
                obj.InvokeMethod("WmiSetBrightness",
                    new object[] { (uint)1, (byte)percent });
                Debug.WriteLine($"[PowerHelper] WMI brightness set to {percent}%");
                return true;
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
