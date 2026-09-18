using Microsoft.UI.Xaml;

namespace Equora.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 日志系统在 P4 引入;当前至少保留异常信息供调试。
        System.Diagnostics.Debug.WriteLine($"[未处理异常] {e.Message}");
        e.Handled = true;
    }
}
