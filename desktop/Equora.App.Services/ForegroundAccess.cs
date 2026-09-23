using System.Runtime.InteropServices;

namespace Equora.App.Services;

public static class ForegroundAccess
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out WindowBounds bounds);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point pixel);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Time; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowBounds { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const int DwmwaCloaked = 14;
    private const uint WmClose = 0x0010;
    private const uint GwHwndPrev = 3;

    public static bool HasRecentInput()
    {
        var idle = new LastInputInfo { Size = 8 };
        return !GetLastInputInfo(ref idle) || unchecked((uint)Environment.TickCount - idle.Time) <= 60_000;
    }
    public static WindowBounds? Bounds(IntPtr window) => window != IntPtr.Zero && IsWindowVisible(window) && GetWindowRect(window, out var bounds) && bounds.Width > 0 && bounds.Height > 0 ? bounds : null;

    // 客户区矩形(换算为屏幕坐标):封锁页只盖住工作区,保留标题栏供用户关闭或最小化窗口。
    public static WindowBounds? ClientBounds(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window) || !GetClientRect(window, out var bounds))
            return null;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var corner = new Point { X = bounds.Left, Y = bounds.Top };
        if (!ClientToScreen(window, ref corner)) return null;
        return new WindowBounds { Left = corner.X, Top = corner.Y, Right = corner.X + bounds.Width, Bottom = corner.Y + bounds.Height };
    }
    public static bool IsMinimized(IntPtr window) => window != IntPtr.Zero && IsIconic(window);
    public static bool IsAlive(IntPtr window) => window != IntPtr.Zero && IsWindow(window);
    public static void Close(IntPtr window) { if (window != IntPtr.Zero) PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero); }
    public static void Minimize(IntPtr window) { if (window != IntPtr.Zero) ShowWindowAsync(window, 6); }

    // 限定遮罩跟随的插入点:目标窗口的正上方,而不是全局置顶 —— 其他窗口盖住目标时也一并盖住遮罩。
    public static IntPtr ZOrderAbove(IntPtr window) =>
        window == IntPtr.Zero || !IsWindow(window) ? IntPtr.Zero : GetWindow(window, GwHwndPrev);

    /// <summary>屏幕上可见的顶层窗口(排除工具窗与 DWM 遮蔽的挂起窗口)。includeMinimized
    /// 时连同最小化窗口一起返回——封锁目标最小化后仍需跟踪,以便解除时恢复可见。</summary>
    public static List<IntPtr> VisibleTopWindows(bool includeMinimized = false)
    {
        var result = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || (!includeMinimized && IsIconic(window))) return true;
            if ((GetWindowLongPtr(window, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return true;
            if (DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (GetClientRect(window, out var client) && client.Width >= 96 && client.Height >= 48)
                result.Add(window);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static (IntPtr Window, string? Process) Current()
    {
        var window = GetForegroundWindow();
        var process = ResolveWindowProcess(window);
        return (window, process);
    }

    /// <summary>解析窗口归属进程;UWP 宿主窗口(ApplicationFrameHost)进一步解析其子窗口。</summary>
    public static string? ResolveWindowProcess(IntPtr window)
    {
        var process = AppMonitor.GetProcessNameOfWindow(window);
        if (process is null) return null;
        if (string.Equals(process, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(process, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            EnumChildWindows(window, (child, _) =>
            {
                var candidate = AppMonitor.GetProcessNameOfWindow(child);
                if (candidate is not null && !candidate.StartsWith("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                { process = candidate; return false; }
                return true;
            }, IntPtr.Zero);
        }
        return process;
    }
}

/// <summary>
/// 窗口句柄 → 进程名缓存。Process.GetProcessById 逐次查询较重,枚举全部顶层窗口时
/// 先用 GetWindowThreadProcessId(纯 user32 调用)校验缓存命中,仅对新句柄解析进程名。
/// </summary>
public sealed class WindowProcessCache
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly Dictionary<IntPtr, (uint Pid, string? Name)> _map = new();

    public string? Resolve(IntPtr window)
    {
        if (!ForegroundAccess.IsAlive(window)) return null;
        if (GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0) return null;
        if (_map.TryGetValue(window, out var cached) && cached.Pid == pid) return cached.Name;
        var name = AppMonitor.GetProcessNameOfWindow(window);
        _map[window] = (pid, name);
        if (_map.Count > 512) Prune();
        return name;
    }

    private void Prune()
    {
        foreach (var key in _map.Keys.Where(key => !ForegroundAccess.IsAlive(key)).ToList())
            _map.Remove(key);
    }
}
