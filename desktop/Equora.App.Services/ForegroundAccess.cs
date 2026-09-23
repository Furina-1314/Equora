using System.Runtime.InteropServices;

namespace Equora.App.Services;

public static class ForegroundAccess
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Time; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowBounds { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    public static bool HasRecentInput()
    {
        var idle = new LastInputInfo { Size = 8 };
        return !GetLastInputInfo(ref idle) || unchecked((uint)Environment.TickCount - idle.Time) <= 60_000;
    }
    public static WindowBounds? Bounds(IntPtr window) => window != IntPtr.Zero && IsWindowVisible(window) && GetWindowRect(window, out var bounds) && bounds.Width > 0 && bounds.Height > 0 ? bounds : null;

    public static (IntPtr Window, string? Process) Current()
    {
        var window = GetForegroundWindow();
        var process = AppMonitor.GetProcessNameOfWindow(window);
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
        return (window, process);
    }
    public static void Minimize(IntPtr window) { if (window != IntPtr.Zero) ShowWindowAsync(window, 6); }
}
