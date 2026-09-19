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
        // 原生日志:文件位于数据目录 logs(未初始化时为空操作)。
        NativeInterop.EquoraCore.InitLog(
            System.IO.Path.Combine(Services.AppPaths.DataDirectory, "logs", "equora.log"));

        // 崩溃恢复:收尾上次未闭合的专注会话(以最后活动时刻计)。
        var recovered = AppServices.Data.RecoverFocusSessions();
        if (recovered > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[app.recovery] 收尾 {recovered} 个遗留专注会话");
        }

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
