using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

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
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

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

    private const byte VK_SHIFT = 0x10;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
    private const int PowerRequestExecutionRequired = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string SimpleReasonString;
    }

    private static IntPtr _executionRequestHandle = IntPtr.Zero;

    private static IntPtr _wakeTimerHandle = IntPtr.Zero;
    private static DateTime _scheduledWakeTime;

    private const string WakeTaskName = "SynologyPhotoFrameWake";
    private static bool _taskWakeScheduled;

    private static readonly Lazy<bool> _isModernStandby = new(() =>
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Power");
            if (key?.GetValue("CsEnabled") is int csEnabled)
            {
                Debug.WriteLine($"[PowerHelper] CsEnabled={csEnabled} (Modern Standby: {csEnabled == 1})");
                return csEnabled == 1;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerHelper] Failed to read CsEnabled: {ex.Message}");
        }
        return false;
    });

    /// <summary>
    /// True if the system uses Modern Standby (S0 Low Power Idle / Connected Standby).
    /// On these devices, SetWaitableTimer(fResume) and Task Scheduler WakeToRun are
    /// unreliable — the system should be kept awake with display off instead.
    /// </summary>
    public static bool IsModernStandby => _isModernStandby.Value;

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
    /// Request that the process keeps running during Modern Standby (S0 Low Power Idle).
    /// Without this, PLM will suspend the process and timers will stop firing.
    /// </summary>
    public static void RequestExecutionRequired()
    {
        if (_executionRequestHandle != IntPtr.Zero) return; // Already active

        var context = new REASON_CONTEXT
        {
            Version = POWER_REQUEST_CONTEXT_VERSION,
            Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            SimpleReasonString = "Photo frame scheduled to turn on display"
        };
        _executionRequestHandle = PowerCreateRequest(ref context);
        if (_executionRequestHandle == IntPtr.Zero || _executionRequestHandle == new IntPtr(-1))
        {
            _executionRequestHandle = IntPtr.Zero;
            Debug.WriteLine("[PowerHelper] PowerCreateRequest failed");
            return;
        }

        if (PowerSetRequest(_executionRequestHandle, PowerRequestExecutionRequired))
        {
            Debug.WriteLine("[PowerHelper] ExecutionRequired set — process will stay active during Modern Standby");
        }
        else
        {
            Debug.WriteLine("[PowerHelper] PowerSetRequest(ExecutionRequired) failed");
            CloseHandle(_executionRequestHandle);
            _executionRequestHandle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Release the execution-required power request, allowing the system to
    /// suspend the process during Modern Standby again.
    /// </summary>
    public static void ClearExecutionRequired()
    {
        if (_executionRequestHandle == IntPtr.Zero) return;

        PowerClearRequest(_executionRequestHandle, PowerRequestExecutionRequired);
        CloseHandle(_executionRequestHandle);
        _executionRequestHandle = IntPtr.Zero;
        Debug.WriteLine("[PowerHelper] ExecutionRequired cleared");
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
    /// Uses mouse and keyboard simulation for reliable wake on Modern Standby devices (e.g. Surface).
    /// Multiple attempts are made because Modern Standby displays may need time to respond.
    /// </summary>
    public static void ActivateDisplay()
    {
        PreventSleep();

        // On Modern Standby (e.g. Surface Go 2), SC_MONITORPOWER alone is unreliable.
        // Use input simulation first — this is the most reliable way to wake the display.
        SimulateMouseMove();
        SimulateKeyPress();
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

            // Disable lock screen UI (Pro/Enterprise only, silently ignored on Home)
            // Helps on Modern Standby where CONSOLELOCK alone may not suppress Windows Hello
            try
            {
                Microsoft.Win32.Registry.SetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Personalization",
                    "NoLockScreen", 1, Microsoft.Win32.RegistryValueKind.DWord);
                Debug.WriteLine("[PowerHelper] NoLockScreen registry key set");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PowerHelper] NoLockScreen registry write failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // Requires admin - log and continue
            Debug.WriteLine($"[PowerHelper] DisableLockOnWake error: {ex.Message}");
        }
    }

    /// <summary>
    /// Schedule a system wake-up at the specified time using dual mechanisms:
    /// 1. Task Scheduler with WakeToRun (primary, reliable on Modern Standby / S0ix)
    /// 2. Waitable timer with fResume=true (backup, reliable on traditional S3 sleep)
    /// Returns false if both mechanisms failed — caller should keep the system awake as fallback.
    /// </summary>
    public static bool ScheduleSystemWake(DateTime wakeTime)
    {
        if (_scheduledWakeTime == wakeTime && (_wakeTimerHandle != IntPtr.Zero || _taskWakeScheduled))
            return true; // Already set for this time

        CancelScheduledWake();

        // Primary: Task Scheduler (reliable on Modern Standby / S0ix)
        bool taskOk = ScheduleTaskWake(wakeTime);

        // Backup: Waitable timer (reliable on traditional S3 sleep)
        bool timerOk = ScheduleWaitableTimerWake(wakeTime);

        if (taskOk || timerOk)
        {
            _scheduledWakeTime = wakeTime;
            Debug.WriteLine($"[PowerHelper] Wake scheduled for {wakeTime:yyyy-MM-dd HH:mm:ss} (task={taskOk}, timer={timerOk})");
            return true;
        }

        Debug.WriteLine("[PowerHelper] Both wake mechanisms failed");
        return false;
    }

    /// <summary>
    /// Cancel any pending scheduled wake-up from both mechanisms.
    /// </summary>
    public static void CancelScheduledWake()
    {
        CancelWaitableTimerWake();
        CancelTaskWake();
        _scheduledWakeTime = default;
    }

    private static bool ScheduleWaitableTimerWake(DateTime wakeTime)
    {
        _wakeTimerHandle = CreateWaitableTimer(IntPtr.Zero, true, null);
        if (_wakeTimerHandle == IntPtr.Zero)
        {
            Debug.WriteLine("[PowerHelper] Failed to create waitable timer");
            return false;
        }

        long dueTime = wakeTime.ToFileTime();
        if (SetWaitableTimer(_wakeTimerHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, true))
        {
            Debug.WriteLine($"[PowerHelper] Waitable timer set for {wakeTime:yyyy-MM-dd HH:mm:ss}");
            return true;
        }

        Debug.WriteLine("[PowerHelper] Failed to set waitable timer");
        CloseHandle(_wakeTimerHandle);
        _wakeTimerHandle = IntPtr.Zero;
        return false;
    }

    private static void CancelWaitableTimerWake()
    {
        if (_wakeTimerHandle != IntPtr.Zero)
        {
            CancelWaitableTimer(_wakeTimerHandle);
            CloseHandle(_wakeTimerHandle);
            _wakeTimerHandle = IntPtr.Zero;
            Debug.WriteLine("[PowerHelper] Waitable timer cancelled");
        }
    }

    private static bool ScheduleTaskWake(DateTime wakeTime)
    {
        try
        {
            var xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <RegistrationInfo>
                    <Description>Synology Photo Frame scheduled wake</Description>
                  </RegistrationInfo>
                  <Triggers>
                    <TimeTrigger>
                      <StartBoundary>{wakeTime:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
                      <Enabled>true</Enabled>
                    </TimeTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>LeastPrivilege</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>false</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <Enabled>true</Enabled>
                    <Hidden>true</Hidden>
                    <DeleteExpiredTaskAfter>PT5M</DeleteExpiredTaskAfter>
                    <WakeToRun>true</WakeToRun>
                    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
                    <Priority>7</Priority>
                    <IdleSettings>
                      <StopOnIdleEnd>false</StopOnIdleEnd>
                      <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>cmd.exe</Command>
                      <Arguments>/c exit 0</Arguments>
                    </Exec>
                  </Actions>
                </Task>
                """;

            var xmlPath = Path.Combine(Path.GetTempPath(), "SynologyPhotoFrame_Wake.xml");
            try
            {
                File.WriteAllText(xmlPath, xml, Encoding.Unicode);

                var (exitCode, output) = RunProcess("schtasks",
                    $"/create /tn \"{WakeTaskName}\" /xml \"{xmlPath}\" /f");

                if (exitCode == 0)
                {
                    _taskWakeScheduled = true;
                    Debug.WriteLine($"[PowerHelper] Task Scheduler wake set for {wakeTime:yyyy-MM-dd HH:mm:ss}");
                    return true;
                }

                Debug.WriteLine($"[PowerHelper] schtasks create failed (exit {exitCode}): {output}");
                return false;
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerHelper] Task Scheduler wake error: {ex.Message}");
            return false;
        }
    }

    private static void CancelTaskWake()
    {
        if (!_taskWakeScheduled) return;

        try
        {
            RunProcess("schtasks", $"/delete /tn \"{WakeTaskName}\" /f");
            Debug.WriteLine("[PowerHelper] Task Scheduler wake cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerHelper] Task Scheduler cancel error: {ex.Message}");
        }
        _taskWakeScheduled = false;
    }

    /// <summary>
    /// Simulate a small mouse movement to wake display on Modern Standby devices.
    /// SC_MONITORPOWER alone is unreliable on Surface and other Modern Standby hardware.
    /// </summary>
    public static void SimulateMouseMove()
    {
        var inputs = new INPUT[]
        {
            new()
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = 1, dy = 0, dwFlags = MOUSEEVENTF_MOVE }
            },
            new()
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = -1, dy = 0, dwFlags = MOUSEEVENTF_MOVE }
            }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        Debug.WriteLine("[PowerHelper] Simulated wake mouse move (+1/-1) for display wake");
    }

    /// <summary>
    /// Simulate a keyboard press/release (Shift key) to wake display on Modern Standby devices.
    /// Some Modern Standby devices (e.g. Surface Go 2) respond more reliably to keyboard
    /// input than mouse movement for waking the display.
    /// </summary>
    public static void SimulateKeyPress()
    {
        keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
        keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        Debug.WriteLine("[PowerHelper] Simulated Shift key press/release for display wake");
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

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, "Failed to start process");

        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        return (p.ExitCode, string.IsNullOrEmpty(stderr) ? stdout : stderr);
    }
}
