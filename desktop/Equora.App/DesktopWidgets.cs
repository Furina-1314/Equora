using System.Runtime.InteropServices;
using Equora.App.Services;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Equora.App;

/// <summary>
/// “桌面小组件”,Windows 7 桌面小工具式:窗口钉在桌面层(壁纸之上、图标与应用窗口之下),
/// 不占任务栏与 Alt+Tab,头部可拖动,随应用启动自动恢复。任务小组件列出未完成任务,
/// 打勾即完成(与任务页一致);日历小组件支持日视图、周视图与议程。
/// </summary>
internal sealed class DesktopWidgets : IDisposable
{
    public static DesktopWidgets? Current { get; private set; }
    private Window? _tasks;
    private Window? _calendar;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Action? _reloadTasks;
    private Action? _refreshCalendar;

    public static void EnsureStarted()
    {
        if (Current is not null) return;
        Current = new DesktopWidgets();
        if (Appearance.Current.TaskWidgetEnabled) Current.ShowTasks();
        if (Appearance.Current.CalendarWidgetEnabled) Current.ShowCalendar();
    }

    private DesktopWidgets()
    {
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    public bool TasksOpen => _tasks is not null;
    public bool CalendarOpen => _calendar is not null;

    public void ShowTasks()
    {
        if (_tasks is not null) return;
        _tasks = BuildTaskWidget();
        _tasks.Activate(); // 先以常规窗口完成内容加载
        PinToDesktop(_tasks, 340, 540, topRight: true);
        Appearance.Save(Appearance.Current with { TaskWidgetEnabled = true });
    }
    public void ShowCalendar()
    {
        if (_calendar is not null) return;
        _calendar = BuildCalendarWidget();
        _calendar.Activate();
        PinToDesktop(_calendar, 360, 560, topRight: false);
        Appearance.Save(Appearance.Current with { CalendarWidgetEnabled = true });
    }
    public void HideTasks() => _tasks?.Close();   // Closed 回调清理引用并保存偏好
    public void HideCalendar() => _calendar?.Close();

    // ---- Win32:桌面层挂载与拖动 ----

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point cursor);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point corner);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern bool SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out Rect value, uint winini);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool SetWindowPosChild(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WsChild = 0x40000000, WsPopup = unchecked((int)0x80000000);
    private const long WsExToolWindow = 0x80;
    private const uint SwpFrameChanged = 0x0020, SwpNoZOrder = 0x0004, SwpShowWindow = 0x0040, SwpNoSize = 0x0001, SwpNoMove = 0x0002;
    private const uint GwHwndNext = 2;

    private static IntPtr _desktopLayer;

    // 桌面层 = 广播 0x052C 生成 WorkerW(位于壁纸与桌面图标之后)。把小组件挂为它的子窗口,
    // 即获得 Windows 7 桌面小工具的层级:普通窗口打开时被盖住,“显示桌面”时可见。
    private static IntPtr DesktopLayer()
    {
        if (_desktopLayer != IntPtr.Zero) return _desktopLayer;
        SendMessageTimeout((IntPtr)0xFFFF, 0x052C, UIntPtr.Zero, (IntPtr)1, 2, 1000, out _);
        var progman = FindWindow("Progman", null);
        var worker = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            var name = new System.Text.StringBuilder(256);
            GetClassName(top, name, 256);
            if (name.ToString() != "SHELLDLL_DefView") return true;
            var next = GetWindow(top, GwHwndNext);
            var nextName = new System.Text.StringBuilder(256);
            GetClassName(next, nextName, 256);
            if (nextName.ToString() == "WorkerW") worker = next;
            return false;
        }, IntPtr.Zero);
        _desktopLayer = worker != IntPtr.Zero ? worker : progman;
        return _desktopLayer;
    }

    private static void PinToDesktop(Window window, int width, int height, bool topRight)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
        var physicalWidth = (int)Math.Ceiling(width * scale);
        var physicalHeight = (int)Math.Ceiling(height * scale);
        var margin = (int)Math.Ceiling(16 * scale);
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr((style & ~(WsCaption | WsThickFrame | WsPopup)) | WsChild));
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | WsExToolWindow));
        SystemParametersInfo(48 /* SPI_GETWORKAREA */, 0, out var work, 0);
        var x = work.Right - physicalWidth - margin;
        var y = topRight ? work.Top + margin : Math.Max(work.Top + margin, work.Bottom - physicalHeight - margin);
        SetParent(hwnd, DesktopLayer());
        SetWindowPosChild(hwnd, IntPtr.Zero, x, y, physicalWidth, physicalHeight, SwpFrameChanged | SwpNoZOrder | SwpShowWindow);
    }

    // 头部按住拖动整个小组件(窗口已无标题栏)。
    // 用 Win32 光标屏幕坐标做锚点(按下时记录 光标-窗口左上 偏移与桌面层父窗口原点),
    // 每次移动按新光标位置直接定靶,不做增量累加——不受显示缩放与重绘延迟影响,不会漂移。
    private static void MakeDraggable(FrameworkElement handle, Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var dragging = false;
        var offsetX = 0; var offsetY = 0; var parentX = 0; var parentY = 0;
        handle.PointerPressed += (_, e) =>
        {
            dragging = true;
            handle.CapturePointer(e.Pointer);
            GetCursorPos(out var cursor);
            GetWindowRect(hwnd, out var rect);
            offsetX = cursor.X - rect.Left;
            offsetY = cursor.Y - rect.Top;
            var origin = new Point();
            ClientToScreen(GetParent(hwnd), ref origin);
            parentX = origin.X; parentY = origin.Y;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            GetCursorPos(out var cursor);
            SetWindowPosChild(hwnd, IntPtr.Zero, cursor.X - offsetX - parentX, cursor.Y - offsetY - parentY,
                0, 0, SwpNoZOrder | SwpNoSize);
        };
        handle.PointerReleased += (_, e) => { dragging = false; handle.ReleasePointerCapture(e.Pointer); };
        handle.PointerCanceled += (_, _) => dragging = false;
    }

    // 右下角把手:按住拖动修改小组件尺寸(光标锚定,物理像素,随窗口缩放内容)。
    private static void MakeResizable(FrameworkElement grip, Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var resizing = false; var startCursorX = 0; var startCursorY = 0; var startWidth = 0; var startHeight = 0;
        grip.PointerPressed += (_, e) =>
        {
            resizing = true;
            grip.CapturePointer(e.Pointer);
            GetCursorPos(out var cursor);
            GetWindowRect(hwnd, out var rect);
            startCursorX = cursor.X; startCursorY = cursor.Y;
            startWidth = rect.Right - rect.Left; startHeight = rect.Bottom - rect.Top;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!resizing) return;
            GetCursorPos(out var cursor);
            var scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
            var width = Math.Max((int)Math.Ceiling(260 * scale), startWidth + cursor.X - startCursorX);
            var height = Math.Max((int)Math.Ceiling(300 * scale), startHeight + cursor.Y - startCursorY);
            SetWindowPosChild(hwnd, IntPtr.Zero, 0, 0, width, height, SwpNoZOrder | SwpNoMove);
        };
        grip.PointerReleased += (_, e) => { resizing = false; grip.ReleasePointerCapture(e.Pointer); };
        grip.PointerCanceled += (_, _) => resizing = false;
    }

    private static TextBlock ResizeGrip(Window window)
    {
        var grip = new TextBlock
        {
            Text = "◢", FontSize = 14, Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(4)
        };
        MakeResizable(grip, window);
        return grip;
    }

    // 小组件标题条:左侧标题,右侧关闭按钮;整条作为拖动把手。
    private static Grid TitleBar(Window window, string title, Action close)
    {
        var bar = new Grid { ColumnSpacing = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button
        {
            Content = "\uE711",
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2), MinWidth = 0, MinHeight = 0
        };
        button.Click += (_, _) => close();
        Grid.SetColumn(text, 0);
        Grid.SetColumn(button, 1);
        bar.Children.Add(text);
        bar.Children.Add(button);
        MakeDraggable(bar, window);
        return bar;
    }

    private Window BuildTaskWidget()
    {
        var window = new Window { Title = "任务小组件 · Equora" };
        var summary = new TextBlock { Opacity = 0.7 };
        var list = new ListView { SelectionMode = ListViewSelectionMode.None };
        void Reload()
        {
            var incomplete = AppServices.Data.ListTasks()
                .Where(t => t.Status != NativeInterop.TaskStatus.Done && t.Status != NativeInterop.TaskStatus.Cancelled)
                .ToList();
            var pending = incomplete
                .OrderBy(t => t.DueAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(t => t.CreatedAt)
                .Take(50).ToList();
            summary.Text = pending.Count == incomplete.Count
                ? $"未完成 {pending.Count} 项" : $"未完成 {incomplete.Count} 项(仅显示最近 50 项)";
            list.Items.Clear();
            foreach (var task in pending)
            {
                var captured = task;
                var label = task.DueAt is { } due ? $"{task.Title}  ·  {due.ToLocalTime():MM-dd HH:mm}" : task.Title;
                var box = new CheckBox { Content = label };
                box.Checked += (_, _) =>
                {
                    AppServices.Tasks.ToggleDone(captured); // 未完成 → 完成,与任务页打勾一致
                    Reload();
                };
                list.Items.Add(box);
            }
        }
        _reloadTasks = Reload;
        var refresh = new Button { Content = "刷新", HorizontalAlignment = HorizontalAlignment.Right };
        refresh.Click += (_, _) => Reload();
        var root = new Grid
        {
            Padding = new Thickness(14), RowSpacing = 10,
            Background = new SolidColorBrush(Color.FromArgb(238, 250, 250, 250))
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(TitleBar(window, "未完成任务", window.Close));
        header.Children.Add(summary);
        Grid.SetRow(header, 0);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(refresh, 2);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(refresh);
        root.Children.Add(ResizeGrip(window));
        window.Content = root;
        window.Closed += (_, _) =>
        {
            _tasks = null;
            _reloadTasks = null;
            Appearance.Save(Appearance.Current with { TaskWidgetEnabled = false });
        };
        Reload();
        return window;
    }

    private Window BuildCalendarWidget()
    {
        var window = new Window { Title = "日历小组件 · Equora" };
        var vm = new CalendarViewModel(AppServices.Data, AppServices.Data, AppServices.Undo);
        var viewChoice = new ComboBox
        {
            Header = "视图", ItemsSource = new[] { "周视图", "日视图", "议程" },
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        viewChoice.SelectionChanged += (_, _) => vm.ViewModeIndex = viewChoice.SelectedIndex;
        var prev = new Button { Content = "上一时段" };
        prev.Click += (_, _) => vm.PrevWeekCommand.Execute(null);
        var today = new Button { Content = "今天" };
        today.Click += (_, _) => vm.GoTodayCommand.Execute(null);
        var next = new Button { Content = "下一时段" };
        next.Click += (_, _) => vm.NextWeekCommand.Execute(null);
        var status = new TextBlock { Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
        var list = new ListView { SelectionMode = ListViewSelectionMode.None };
        // 与日历页相同的色块视图;注入小组件自己的视图模型,视图切换独立于主窗口。
        var week = new Controls.WeekView { ViewModelOverride = vm, CompressToViewport = true };
        var gridHost = new ScrollViewer
        {
            Content = week, MinZoomFactor = 1, MaxZoomFactor = 1,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        void Rebuild()
        {
            viewChoice.SelectedIndex = vm.ViewModeIndex;
            // 周视图/日视图用色块视图;议程用列表。
            var grid = vm.ViewModeIndex != 2;
            gridHost.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            list.Items.Clear();
            foreach (var item in vm.Items)
            {
                var start = item.Span.Start.ToLocalTime();
                var prefix = start.ToString("MM-dd HH:mm");
                list.Items.Add(new TextBlock
                {
                    Text = $"{prefix} · {item.DisplayTitle}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new SolidColorBrush(item.IsConflict ? Color.FromArgb(255, 195, 47, 55) : Color.FromArgb(255, 32, 32, 32))
                });
            }
            status.Text = vm.StatusText;
        }
        _refreshCalendar = () => vm.RefreshCommand.Execute(null);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CalendarViewModel.StatusText) or nameof(CalendarViewModel.ViewModeIndex))
                Rebuild();
        };
        vm.Items.CollectionChanged += (_, _) => Rebuild();
        var root = new Grid
        {
            Padding = new Thickness(14), RowSpacing = 10,
            Background = new SolidColorBrush(Color.FromArgb(238, 250, 250, 250))
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(TitleBar(window, "日程", window.Close));
        header.Children.Add(viewChoice);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nav.Children.Add(prev);
        nav.Children.Add(today);
        nav.Children.Add(next);
        Grid.SetRow(header, 0);
        Grid.SetRow(nav, 1);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(gridHost, 2);
        Grid.SetRow(scroll, 2);
        Grid.SetRow(status, 3);
        root.Children.Add(header);
        root.Children.Add(nav);
        root.Children.Add(gridHost);
        root.Children.Add(scroll);
        root.Children.Add(status);
        root.Children.Add(ResizeGrip(window));
        window.Content = root;
        window.Closed += (_, _) =>
        {
            _calendar = null;
            _refreshCalendar = null;
            Appearance.Save(Appearance.Current with { CalendarWidgetEnabled = false });
        };
        vm.Refresh();
        Rebuild();
        return window;
    }

    public void Refresh()
    {
        _reloadTasks?.Invoke();
        _refreshCalendar?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _tasks?.Close();
        _calendar?.Close();
        Current = null;
    }
}
