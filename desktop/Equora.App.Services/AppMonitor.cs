using System.Runtime.InteropServices;

namespace Equora.App.Services;

/// <summary>
/// 前台应用监测(温和提醒级):WinEvent 钩子监听前台窗口变化,
/// 进程名匹配黑/白名单(AppRuleMatcher),命中时回调(不强制、不需管理员)。
/// 不采集窗口标题与内容 —— 仅进程名。
/// </summary>
public sealed class AppMonitor : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
        uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate void WinEventDelegate(IntPtr hHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private IntPtr _hook;
    private readonly WinEventDelegate _delegate; // 防 GC
    private readonly Action<string> _onForegroundChanged;

    public IReadOnlyList<string> AllowedApps { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> BlockedApps { get; set; } = Array.Empty<string>();

    public AppMonitor(Action<string> onForegroundChanged)
    {
        _delegate = OnWinEvent;
        _onForegroundChanged = onForegroundChanged;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 /* OBJID_WINDOW */) return;
        var processName = GetProcessNameOfWindow(hwnd);
        if (processName is null) return;
        _onForegroundChanged(processName);
    }

    public static string? GetProcessNameOfWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>当前前台进程名(轮询备用;主路径为事件回调)。</summary>
    public string? CurrentForegroundProcess()
    {
        var hwnd = GetForegroundWindow();
        return GetProcessNameOfWindow(hwnd);
    }

    public bool IsCurrentForegroundBlocked() =>
        CurrentForegroundProcess() is { } name &&
        AppRuleMatcher.IsBlocked(name, AllowedApps, BlockedApps);

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
