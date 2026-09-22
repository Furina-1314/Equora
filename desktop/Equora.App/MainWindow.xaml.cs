using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Equora.App.Pages;

namespace Equora.App;

public sealed partial class MainWindow : Window
{
    private readonly RestrictionRuntime _restrictions;
    private TrayIcon? _tray;
    private bool _exiting;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    public void ExitApplication() { _exiting = true; Close(); }
    public void PrepareDataReset()
    {
        _clock.Stop();
        _restrictions.Dispose();
        AppServices.Data.Dispose();
    }
    public void RestoreWindow() { AppWindow.Show(); Activate(); }

    public MainWindow()
    {
        InitializeComponent();
        Title = "衡序 Equora";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Equora.ico"));
        ExtendsContentIntoTitleBar = false;
        Appearance.Apply((FrameworkElement)Content);
        ContentFrame.Navigated += (_, args) => SyncNavigation(args.SourcePageType);
        ContentFrame.Navigate(typeof(TasksPage));
        Nav.SelectedItem = Nav.Items[1];
        _restrictions = new RestrictionRuntime(message => { RestrictionNotice.Message = message; RestrictionNotice.IsOpen = true; });
        _restrictions.PauseChanged += UpdatePauseNotice;
        try { _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), Path.Combine(AppContext.BaseDirectory, "Assets", "Equora.ico"), RestoreWindow, ExitApplication); }
        catch (Exception ex) { RestrictionNotice.Message = ex.Message; RestrictionNotice.IsOpen = true; }
        _clock.Tick += (_, _) => { AppServices.FocusVm.Clock = DateTimeOffset.Now; UpdatePauseNotice();
            try { Appearance.ArchiveExpiredSemester(); } catch (Exception ex) { RestrictionNotice.Message = ex.Message; RestrictionNotice.IsOpen = true; } };
        _clock.Start();
        AppWindow.Closing += (_, e) =>
        {
            if (!_exiting && Appearance.Current.CloseToTray && _tray is not null) { e.Cancel = true; AppWindow.Hide(); }
        };
        Closed += (_, _) => { _clock.Stop(); _tray?.Dispose(); _restrictions.Dispose(); AppServices.Data.Dispose(); };

        // App 内快捷键(全局热键 RegisterHotKey 在真机阶段接窗口过程)。
        // Window 没有 KeyboardAccelerators,挂在内容根元素上;Invoked 在加速器上。
        Activated += (_, _) =>
        {
            if (Content is not Microsoft.UI.Xaml.UIElement root) return;
            if (root.KeyboardAccelerators.Count > 0) return; // 只挂一次
            AddAccelerator(root, Windows.System.VirtualKey.N,
                Windows.System.VirtualKeyModifiers.Control);
            AddAccelerator(root, Windows.System.VirtualKey.K,
                Windows.System.VirtualKeyModifiers.Control);
            AddAccelerator(root, Windows.System.VirtualKey.Space,
                Windows.System.VirtualKeyModifiers.Control |
                Windows.System.VirtualKeyModifiers.Shift);
        };
    }

    public void ShowPage(Type page) => ContentFrame.Navigate(page);

    private void UpdatePauseNotice()
    {
        var remaining = _restrictions.PauseRemaining;
        PauseNotice.IsOpen = remaining > TimeSpan.Zero;
        if (PauseNotice.IsOpen)
            PauseNotice.Message = $"剩余 {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}，倒计时结束后自动恢复限制。";
    }

    private void OnTogglePane(object sender, RoutedEventArgs e)
    {
        Shell.IsPaneOpen = !Shell.IsPaneOpen;
        foreach (var item in Nav.Items.Concat(FooterNav.Items).OfType<ListViewItem>())
        {
            if (item.Content is not StackPanel panel) continue;
            var transform = panel.RenderTransform as Microsoft.UI.Xaml.Media.TranslateTransform ?? new();
            panel.RenderTransform = transform;
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = Shell.IsPaneOpen ? 0 : 4,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EnableDependentAnimation = true,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, transform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "X");
            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            storyboard.Children.Add(animation); storyboard.Begin();
        }
    }

    private void SyncNavigation(Type page)
    {
        var tag = page == typeof(TasksPage) ? "tasks" : page == typeof(CalendarPage) ? "calendar"
            : page == typeof(MatrixPage) ? "matrix" : page == typeof(FocusPage) ? "focus"
            : page == typeof(RestrictionsPage) ? "restrictions" : page == typeof(SettingsPage) ? "settings" : "home";
        Nav.SelectedItem = Nav.Items.OfType<ListViewItem>().FirstOrDefault(item => Equals(item.Tag, tag));
        FooterNav.SelectedItem = tag == "settings" ? FooterNav.Items[0] : null;
    }

    private void AddAccelerator(Microsoft.UI.Xaml.UIElement root,
        Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
    {
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
        };
        accelerator.Invoked += OnAcceleratorInvoked;
        root.KeyboardAccelerators.Add(accelerator);
    }

    private void OnFooterSelected(object sender, SelectionChangedEventArgs e)
    {
        if (FooterNav.SelectedItem is not null && ContentFrame.CurrentSourcePageType != typeof(SettingsPage)) ShowPage(typeof(SettingsPage));
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if ((Nav.SelectedItem as ListViewItem)?.Tag is not string tag) return;

        var page = tag switch
        {
            "home" => typeof(HomePage),
            "tasks" => typeof(TasksPage),
            "calendar" => typeof(CalendarPage),
            "matrix" => typeof(MatrixPage),
            "focus" => typeof(FocusPage),
            "settings" => typeof(SettingsPage),
            "restrictions" => typeof(RestrictionsPage),
            _ => typeof(HomePage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null,
                new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
        }
    }
    private async void OnAcceleratorInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        var ctrl = sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
        var shift = sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);

        if (ctrl && shift && sender.Key == Windows.System.VirtualKey.Space)
        {
            args.Handled = true;
            await Controls.QuickCaptureFlyout.ShowAsync(this);
        }
        else if (ctrl && sender.Key == Windows.System.VirtualKey.K)
        {
            args.Handled = true;
            await Controls.CommandPalette.ShowAsync(this, ContentFrame);
        }
        else if (ctrl && sender.Key == Windows.System.VirtualKey.N)
        {
            args.Handled = true;
            ContentFrame.Navigate(typeof(Pages.TasksPage));
            AppServices.Tasks.NewTaskCommand.Execute(null);
        }
    }
}
