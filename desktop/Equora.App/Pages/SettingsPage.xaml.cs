using Equora.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Equora.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        AboutText.Text = $"衡序 Equora 0.2.0(M1)\n数据目录:{AppPaths.DataDirectory}\n" +
                         $"数据库模式:v{AppServices.Data.SchemaVersion}\n" +
                         $"设备 ID:{AppServices.Data.DeviceId}";
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (App.MainWindow?.Content is not FrameworkElement root) return;

        root.RequestedTheme = ThemeChoice.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private async void OnBackupNow(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AppServices.Data.CreateBackup(AppPaths.BackupsDirectory);
            var ok = AppServices.Data.VerifyBackup(path);
            BackupStatus.Text = ok
                ? $"备份完成:{System.IO.Path.GetFileName(path)}(校验通过)"
                : $"备份完成但校验失败:{path}";
        }
        catch (Exception ex)
        {
            BackupStatus.Text = $"备份失败:{ex.Message}";
        }
    }

    private async void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        await global::Windows.System.Launcher.LaunchFolderAsync(
            await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                AppPaths.DataDirectory));
    }

    private async void OnExportJson(object sender, RoutedEventArgs e) =>
        await ExportAsync("JSON 文档", "json", path => AppServices.Data.ExportTasksJson(path));

    private async void OnExportCsv(object sender, RoutedEventArgs e) =>
        await ExportAsync("CSV 文档", "csv", path => AppServices.Data.ExportTasksCsv(path));

    private async System.Threading.Tasks.Task ExportAsync(string typeName, string ext,
        Func<string, int> export)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(typeName, new List<string> { $".{ext}" });
        picker.SuggestedFileName = $"equora-tasks-{DateTime.Now:yyyyMMdd-HHmmss}";
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            var count = export(file.Path);
            TransferStatus.Text = $"已导出 {count} 个任务 → {file.Path}";
        }
        catch (Exception ex)
        {
            TransferStatus.Text = $"导出失败:{ex.Message}";
        }
    }

    private async void OnImportJson(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            var (imported, skipped) = AppServices.Data.ImportTasksJson(file.Path);
            TransferStatus.Text = $"导入完成:新增 {imported},跳过 {skipped}(已存在或非法)";
            AppServices.Tasks.Refresh();
        }
        catch (Exception ex)
        {
            TransferStatus.Text = $"导入失败:{ex.Message}";
        }
    }
}
