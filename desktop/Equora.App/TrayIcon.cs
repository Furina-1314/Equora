using System.Runtime.InteropServices;

namespace Equora.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly IntPtr _window;
    private readonly Action _restore, _exit;
    private readonly SubclassProc _callback;
    private NotifyData _data;
    private const uint Message = 0x8000 + 71;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly uint _activation = ActivationMessage;
    private static uint ActivationMessage => RegisterWindowMessage("Equora.Restore." + InstanceKey);
    internal static string InstanceKey => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Services.AppPaths.DataDirectory.ToUpperInvariant())))[..24];
    internal static void ActivateExisting() => PostMessage(new IntPtr(0xffff), ActivationMessage, IntPtr.Zero, IntPtr.Zero);
    public TrayIcon(IntPtr window, string iconPath, Action restore, Action exit)
    {
        _window = window; _restore = restore; _exit = exit; _callback = OnMessage;
        _data = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Flags = 3, Callback = Message,
            Icon = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x10), Tip = "", Info = "", InfoTitle = "" };
        if (!SetWindowSubclass(window, _callback, 71, 0)) throw new InvalidOperationException("无法初始化托盘菜单。");
        if (!Shell_NotifyIcon(0, ref _data)) { Dispose(); throw new InvalidOperationException("无法创建托盘图标。"); }
    }
    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, nuint id, nuint data)
    {
        if (message == _taskbarCreated) Shell_NotifyIcon(0, ref _data);
        if (message == _activation) _restore();
        if (message == Message)
        {
            if (lparam.ToInt64() is 0x202 or 0x203) _restore();
            if (lparam.ToInt64() == 0x205)
            {
                var menu = CreatePopupMenu();
                try
                {
                    AppendMenu(menu, 0, 1, "打开 Equora"); AppendMenu(menu, 0, 2, "退出 Equora");
                    GetCursorPos(out var point); SetForegroundWindow(window);
                    var choice = TrackPopupMenu(menu, 0x100 | 2, point.X, point.Y, 0, window, IntPtr.Zero);
                    if (choice == 1) _restore(); else if (choice == 2) _exit();
                }
                finally { DestroyMenu(menu); }
            }
        }
        return DefSubclassProc(window, message, wparam, lparam);
    }
    public void Dispose()
    { Shell_NotifyIcon(2, ref _data); RemoveWindowSubclass(_window, _callback, 71); if (_data.Icon != IntPtr.Zero) { DestroyIcon(_data.Icon); _data.Icon = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr h, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr h, string name, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
}
