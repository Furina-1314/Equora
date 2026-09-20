using System.Runtime.InteropServices;

namespace Equora.App.Services;

public static class ForegroundAccess
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Time; }

    public static (IntPtr Window, string? Process) Current()
    {
        var idle = new LastInputInfo { Size = 8 };
        if (GetLastInputInfo(ref idle) && unchecked((uint)Environment.TickCount - idle.Time) > 60_000) return default;
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
