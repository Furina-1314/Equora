using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Equora.App.Pages;

public sealed partial class FocusPage : Page
{
    private FocusViewModel ViewModel => AppServices.FocusVm;

    /// <summary>前台监测(温和提醒;隐私开关关闭时不启动)。</summary>
    private AppMonitor? _monitor;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };


    public FocusPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Load();
_timer.Tick += (_, _) => ViewModel.Clock = DateTimeOffset.Now;
        _timer.Start();

        Loaded += (_, _) => StartMonitorIfEnabled();
        Unloaded += (_, _) => _monitor?.Dispose();
    }

    private void StartMonitorIfEnabled()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        var enabled = settings.Values["ActivityMonitoring"] is true; // 默认关闭(隐私优先)
        if (!enabled || _monitor is not null) return;

        var blocked = settings.Values["BlockedApps"] as string ?? "game.exe,steam.exe";
        _monitor = new AppMonitor(process =>
        {
            if (AppRuleMatcher.IsBlocked(process, Array.Empty<string>(),
                    blocked.Split(',', StringSplitOptions.RemoveEmptyEntries)))
            {
                ViewModel.ReportBlockedApp(process);
            }
            else
            {
                ViewModel.ClearNudge();
            }
        })
        {
            BlockedApps = blocked.Split(',', StringSplitOptions.RemoveEmptyEntries),
        };
        _monitor.Start();
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: FocusProfileDto profile })
        {
            ViewModel.ApplyProfile(profile);
        }
    }

    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SubmitCapture();
            e.Handled = true;
        }
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e) => SubmitCapture();

    private void SubmitCapture()
    {
        ViewModel.Capture(CaptureBox.Text);
        CaptureBox.Text = "";
    }

    private void OnResolveAsTask(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DistractionDto d })
        {
            ViewModel.ResolveAsTask(d);
        }
    }
}
