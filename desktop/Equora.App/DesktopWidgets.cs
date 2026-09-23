using System.Runtime.InteropServices;
using Equora.App.Services;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Equora.App;

/// <summary>
/// “桌面”小组件:两个常驻小窗。任务小组件列出未完成任务,打勾即完成(与任务页一致);
/// 日历小组件支持日视图、周视图与议程,可与主窗口独立切换。打开状态随偏好保存,
/// 启动时自动恢复;关闭小组件窗口即记录为关闭。
/// </summary>
internal sealed class DesktopWidgets : IDisposable
{
    public static DesktopWidgets? Current { get; private set; }
    private Window? _tasks;
    private Window? _calendar;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private Action? _reloadTasks;
    private Action? _refreshCalendar;
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

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
        _tasks.Activate();
        Appearance.Save(Appearance.Current with { TaskWidgetEnabled = true });
    }
    public void ShowCalendar()
    {
        if (_calendar is not null) return;
        _calendar = BuildCalendarWidget();
        _calendar.Activate();
        Appearance.Save(Appearance.Current with { CalendarWidgetEnabled = true });
    }
    public void HideTasks()
    {
        if (_tasks is null) return;
        _tasks.Close(); // Closed 回调负责清引用并保存偏好
    }
    public void HideCalendar()
    {
        if (_calendar is null) return;
        _calendar.Close();
    }

    private static void SizeWindow(Window window, int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)));
    }

    private Window BuildTaskWidget()
    {
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
                var box = new CheckBox { Content = label, Tag = captured };
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
        var root = new Grid { Padding = new Thickness(14), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(new TextBlock { Text = "未完成任务", FontSize = 18 });
        header.Children.Add(summary);
        Grid.SetRow(header, 0);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(refresh, 2);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(refresh);
        var window = new Window { Title = "任务小组件 · Equora", Content = root };
        SizeWindow(window, 340, 540);
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
        void Rebuild()
        {
            viewChoice.SelectedIndex = vm.ViewModeIndex;
            list.Items.Clear();
            foreach (var item in vm.Items)
            {
                var start = item.Span.Start.ToLocalTime();
                var prefix = vm.ViewModeIndex == 1 ? start.ToString("HH:mm") : start.ToString("MM-dd HH:mm");
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
        var root = new Grid { Padding = new Thickness(14), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(new TextBlock { Text = "日程", FontSize = 18 });
        header.Children.Add(viewChoice);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nav.Children.Add(prev);
        nav.Children.Add(today);
        nav.Children.Add(next);
        Grid.SetRow(header, 0);
        Grid.SetRow(nav, 1);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        Grid.SetRow(status, 3);
        root.Children.Add(header);
        root.Children.Add(nav);
        root.Children.Add(scroll);
        root.Children.Add(status);
        var window = new Window { Title = "日历小组件 · Equora", Content = root };
        SizeWindow(window, 360, 560);
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
