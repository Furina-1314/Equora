using Equora.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Equora.App.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;
    public SettingsPage()
    {
        InitializeComponent();
        var settings = Appearance.Current;
        ThemeChoice.SelectedIndex = Math.Clamp(settings.Theme, 0, 2);
        AccentHex.Text = settings.Accent;
        AccentChoice.SelectedItem = AccentChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag as string, settings.Accent, StringComparison.OrdinalIgnoreCase)) ?? AccentChoice.Items.Last();
        CloseChoice.SelectedIndex = settings.CloseToTray ? 1 : 0;
        StartupToggle.IsOn = StartupRegistration.IsRegistered();
        var semester = settings.Semester;
        SemesterToggle.IsOn = semester.Enabled;
        SemesterName.Text = semester.Name;
        SemesterStart.Date = new DateTimeOffset(semester.StartDate.ToDateTime(TimeOnly.MinValue));
        SemesterEnd.Date = new DateTimeOffset(semester.EndDate.ToDateTime(TimeOnly.MinValue));
        UpdateSemesterDisplay();
        AboutText.Text = $"衡序 Equora\n数据目录：{AppPaths.DataDirectory}\n数据库版本：{AppServices.Data.SchemaVersion}";
        _loading = false;
    }

    private void UpdateSemesterDisplay()
    {
        var semester = Appearance.Current.Semester;
        SemesterFields.Visibility = semester.Enabled ? Visibility.Visible : Visibility.Collapsed;
        SemesterStatus.Text = semester.WeekNumber(DateOnly.FromDateTime(DateTime.Today)) is int week
            ? $"{semester.Name} · 当前第 {week} 周 · 共 {semester.WeekCount} 周"
            : $"{semester.Name} · 当前不在学期内 · 共 {semester.WeekCount} 周";
    }

    private async void OnSemesterToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SemesterToggle.IsEnabled = false;
        try
        {
            var enabled = SemesterToggle.IsOn;
            if (!enabled)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "关闭学期模式？",
                    Content = "关闭后将隐藏学期周次和批量添加入口。已添加的任务和时间段会保留，仍可单独编辑；学期设置也会保留。",
                    PrimaryButtonText = "关闭学期模式", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            Appearance.Save(Appearance.Current with { Semester = Appearance.Current.Semester with { Enabled = enabled } });
            SettingsNotice.IsOpen = false;
        }
        catch (Exception ex) { ShowError($"学期设置保存失败：{ex.Message}"); }
        finally
        {
            _loading = true;
            SemesterToggle.IsOn = Appearance.Current.Semester.Enabled;
            _loading = false;
            SemesterToggle.IsEnabled = true;
            UpdateSemesterDisplay();
        }
    }

    private void OnSaveSemester(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SemesterStart.Date is null || SemesterEnd.Date is null) throw new ArgumentException("请选择起始和结束日期。");
            var semester = new SemesterSettings { Enabled = Appearance.Current.Semester.Enabled,
                Name = SemesterName.Text.Trim(), StartDate = DateOnly.FromDateTime(SemesterStart.Date.Value.Date),
                EndDate = DateOnly.FromDateTime(SemesterEnd.Date.Value.Date) };
            semester.Validate();
            Appearance.Save(Appearance.Current with { Semester = semester });
            UpdateSemesterDisplay();
            SemesterStatus.Text = "已保存。" + SemesterStatus.Text;
            SettingsNotice.IsOpen = false;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Save(AppPreferences preferences)
    {
        if (_loading) return;
        try { Appearance.Save(preferences); SettingsNotice.IsOpen = false; }
        catch (Exception ex) { ShowError($"设置保存失败：{ex.Message}"); }
    }

    private void ShowError(string message)
    {
        SettingsNotice.Severity = InfoBarSeverity.Error;
        SettingsNotice.Message = message;
        SettingsNotice.IsOpen = true;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) Save(Appearance.Current with { Theme = ThemeChoice.SelectedIndex });
    }

    private void OnAccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentChoice.SelectedItem is not ComboBoxItem { Tag: string hex } || hex == "custom") return;
        AccentHex.Text = hex;
        Save(Appearance.Current with { Accent = hex });
    }

    private void OnApplyAccent(object sender, RoutedEventArgs e)
    {
        var hex = AccentHex.Text.Trim().ToUpperInvariant();
        if (!Appearance.TryColor(hex, out _)) { ShowError("请输入有效颜色，例如 #0078D4。"); return; }
        Save(Appearance.Current with { Accent = hex });
        _loading = true;
        AccentChoice.SelectedItem = AccentChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, hex)) ?? AccentChoice.Items.Last();
        _loading = false;
    }

    private async void OnBackupNow(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await PickFolder();
            if (folder is null) return;
            var path = AppServices.Data.CreateBackup(folder.Path);
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
        try
        {
            AppPaths.EnsureCreated();
            var folder = await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(AppPaths.DataDirectory);
            if (!await global::Windows.System.Launcher.LaunchFolderAsync(folder)) ShowError("无法打开数据目录。");
        }
        catch (Exception ex) { ShowError($"无法打开数据目录：{ex.Message}"); }
    }

    private async void OnExportJson(object sender, RoutedEventArgs e) =>
        await ExportAsync("JSON 文档", "json", path => AppServices.Data.ExportTasksJson(path));

    private async void OnExportCsv(object sender, RoutedEventArgs e) =>
        await ExportAsync("CSV 文档", "csv", path => AppServices.Data.ExportTasksCsv(path));

    private async System.Threading.Tasks.Task ExportAsync(string typeName, string ext,
        Func<string, int> export)
    {
        try
        {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
        picker.FileTypeChoices.Add(typeName, new List<string> { $".{ext}" });
        picker.SuggestedFileName = $"equora-tasks-{DateTime.Now:yyyyMMdd-HHmmss}";
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
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
        try
        {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
            var (imported, skipped) = AppServices.Data.ImportTasksJson(file.Path);
            TransferStatus.Text = $"导入完成:新增 {imported},跳过 {skipped}(已存在或非法)";
            AppServices.Tasks.Refresh();
        }
        catch (Exception ex)
        {
            TransferStatus.Text = $"导入失败:{ex.Message}";
        }
    }
    private void OnCloseChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading) Save(Appearance.Current with { CloseToTray = CloseChoice.SelectedIndex == 1 }); }
    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { StartupRegistration.SetRegistered(StartupToggle.IsOn, Environment.ProcessPath!); }
        catch (Exception ex) { _loading = true; StartupToggle.IsOn = !StartupToggle.IsOn; _loading = false; ShowError(ex.Message); }
    }
    private async System.Threading.Tasks.Task<Windows.Storage.StorageFolder?> PickFolder()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
        return await picker.PickSingleFolderAsync();
    }
    private async void OnChangeDataDirectory(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppServices.FocusVm.IsRunning) { ShowError("请先结束当前专注，再迁移数据。"); return; }
            var folder = await PickFolder(); if (folder is null) return;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "迁移数据目录", Content = $"将数据复制到：{folder.Path}\n目标必须为空。原数据保留；复制成功后退出程序，下次启动使用新目录。", PrimaryButtonText = "迁移并退出", CloseButtonText = "取消" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            RestrictionRuntime.Current?.FlushUsage();
            DataDirectoryMigration.CopySnapshot(AppPaths.DataDirectory, folder.Path, AppServices.Data.CreateBackup);
            AppPaths.SelectForNextStart(folder.Path);
            App.MainWindow!.ExitApplication();
        }
        catch (Exception ex) { ShowError($"迁移未完成，继续使用原目录：{ex.Message}"); }
    }
    private async void OnHelp(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "使用帮助与快捷键", CloseButtonText = "关闭", Content = new TextBlock { Text = "Ctrl+N：新建任务\nCtrl+K：打开命令面板\nCtrl+Shift+空格：快速记录\nCtrl+Z：在任务页撤销\n\n日历：点击空白处新建；拖动安排时间；右键时间段编辑或删除。\n托盘：单击恢复窗口，右键退出。\n专注：番茄钟按轮次自动交替工作与休息；深度工作为单次倒计时；正计时手动结束。", TextWrapping = TextWrapping.Wrap } };
        await dialog.ShowAsync();
    }
}
