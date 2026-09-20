using Equora.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System.Text.Json;

namespace Equora.App.Pages;

public sealed partial class RestrictionsPage : Page
{
    private RestrictionRuntime Runtime => RestrictionRuntime.Current!;
    private UsageRule? _editing;
    private bool _loading = true;
    private IReadOnlyList<InstalledApplication> _apps = Array.Empty<InstalledApplication>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public RestrictionsPage()
    {
        InitializeComponent();
        AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnBackgroundPressed), true);
        MasterSwitch.IsOn = Runtime.Configuration.Enabled;
        ActivityToggle.IsOn = Appearance.Current.ActivityMonitoring;
        BlockedAppsBox.Text = Appearance.Current.BlockedApps;
        Refresh();
        _loading = false;
        _timer.Tick += (_, _) => UpdateStatus();
        Loaded += (_, _) => { _timer.Start(); UpdateStatus(); };
        Unloaded += (_, _) => _timer.Stop();
    }
    private void Refresh() => RulesList.ItemsSource = Runtime.Configuration.Rules;
    private void OnBackgroundPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source == InstalledApps) return;
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }
        InstalledApps.IsSuggestionListOpen = false;
    }
    private void OnActivityToggled(object sender, RoutedEventArgs e)
    { if (!_loading) Appearance.Save(Appearance.Current with { ActivityMonitoring = ActivityToggle.IsOn }); }
    private void OnBlockedAppsChanged(object sender, RoutedEventArgs e)
    { if (!_loading) Appearance.Save(Appearance.Current with { BlockedApps = BlockedAppsBox.Text.Replace('，', ',') }); }
    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 860;
        OverviewColumn.Width = compact ? new GridLength(0) : new GridLength(340);
        Grid.SetColumn(RulesPanel, compact ? 1 : 0);
        Grid.SetColumn(EditorPanel, 1);
        Grid.SetRow(EditorPanel, compact ? 1 : 0);
        Grid.SetRowSpan(EditorPanel, compact ? 1 : 3);
        Grid.SetColumn(BrowserPanel, compact ? 1 : 0);
        Grid.SetRow(BrowserPanel, compact ? 2 : 1);
    }
    private void UpdateStatus()
    {
        RuntimeStatus.Text = Runtime.Status;
        BrowserStatus.Text = Runtime.Store.BrowserSeen is { } seen && DateTimeOffset.Now - seen < TimeSpan.FromSeconds(15)
            ? "浏览器已连接" : "等待浏览器连接";
        UsageText.Text = _editing is null ? "" : $"今日已使用 {Runtime.Usage(_editing) / 60:0.0} 分钟";
    }
    private void Report(string message, bool error = false)
    {
        Notice.Message = message; Notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success; Notice.IsOpen = true;
    }
    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { Runtime.Save(Runtime.Configuration with { Enabled = MasterSwitch.IsOn }); }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    private void OnAllow(object sender, RoutedEventArgs e) { Runtime.AllowTemporarily(); UpdateStatus(); }
    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppPicker is not null) AppPicker.Visibility = KindChoice.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void OnReadApps(object sender, RoutedEventArgs e)
    {
        ReadAppsButton.IsEnabled = false;
        try { _apps = await Task.Run(InstalledApplicationCatalog.Read); InstalledApps.ItemsSource = _apps.Take(30).ToList(); Report($"已读取 {_apps.Count} 个应用。可搜索选择或手动填写进程名。"); }
        catch (Exception ex) { Report($"读取失败：{ex.Message}", true); }
        finally { ReadAppsButton.IsEnabled = true; }
    }
    private void OnAppSearch(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = _apps.Where(a => a.ToString().Contains(sender.Text, StringComparison.CurrentCultureIgnoreCase)).Take(30).ToList();
    }
    private void OnAppChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs e)
    {
        if (e.SelectedItem is InstalledApplication app) { RuleTarget.Text = app.ProcessName; RuleName.Text = app.Name; }
    }
    private void OnNew(object sender, RoutedEventArgs e)
    {
        _editing = null; RulesList.SelectedItem = null; EditorTitle.Text = "新增规则";
        RuleName.Text = ""; RuleTarget.Text = ""; FocusOnly.IsChecked = false;
        RuleEnabled.IsChecked = true; ScheduleEnabled.IsChecked = false; DailyMinutes.Value = 0;
        DeleteRuleButton.IsEnabled = false; UpdateStatus();
    }
    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RulesList.SelectedItem is not UsageRule rule) return;
        _editing = rule; EditorTitle.Text = "编辑规则"; KindChoice.SelectedIndex = rule.Kind == "app" ? 0 : 1;
        RuleName.Text = rule.Name; RuleTarget.Text = rule.Target; RuleEnabled.IsChecked = rule.Enabled;
        FocusOnly.IsChecked = rule.DuringFocus; ScheduleEnabled.IsChecked = rule.HasSchedule;
        StartTime.Time = TimeSpan.FromMinutes(rule.StartMinute); EndTime.Time = TimeSpan.FromMinutes(rule.EndMinute);
        DailyMinutes.Value = rule.DailyMinutes; DeleteRuleButton.IsEnabled = true; UpdateStatus();
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!double.IsFinite(DailyMinutes.Value) || DailyMinutes.Value != Math.Floor(DailyMinutes.Value)) throw new ArgumentException("每日额度请输入整数分钟。");
            var rule = RestrictionPolicy.Validate(new UsageRule { Id = _editing?.Id ?? Guid.NewGuid().ToString("N"),
                Kind = KindChoice.SelectedIndex == 0 ? "app" : "website", Name = RuleName.Text, Target = RuleTarget.Text,
                Enabled = RuleEnabled.IsChecked == true, DuringFocus = FocusOnly.IsChecked == true,
                HasSchedule = ScheduleEnabled.IsChecked == true, StartMinute = (int)StartTime.Time.TotalMinutes,
                EndMinute = (int)EndTime.Time.TotalMinutes, DailyMinutes = (int)DailyMinutes.Value });
            var rules = Runtime.Configuration.Rules.Where(r => r.Id != rule.Id).Append(rule).ToList();
            Runtime.Save(Runtime.Configuration with { Rules = rules }); Refresh(); RulesList.SelectedItem = rule; Report("规则已保存并生效。");
        }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        var rule = _editing;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "删除限制规则", Content = $"删除「{rule.Name}」？使用记录将保留。", PrimaryButtonText = "删除", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { Runtime.Save(Runtime.Configuration with { Rules = Runtime.Configuration.Rules.Where(r => r.Id != rule.Id).ToList() }); Refresh(); OnNew(sender, e); }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    private async void OnOpenExtension(object sender, RoutedEventArgs e)
    {
        try { await Windows.System.Launcher.LaunchFolderAsync(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.Combine(AppContext.BaseDirectory, "BrowserExtension"))); }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    private void OnConnectBrowser(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = ExtensionId.Text.Trim();
            if (id.Length != 32 || id.Any(c => c < 'a' || c > 'p')) throw new ArgumentException("请输入扩展管理页显示的 32 位扩展 ID。");
            var exe = Path.Combine(AppContext.BaseDirectory, "BrowserHost", "com.equora.nativehost.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("缺少浏览器连接组件，请重新构建完整桌面应用。", exe);
            var manifest = Path.Combine(AppPaths.DataDirectory, "native-host.json");
            var origins = new HashSet<string> { $"chrome-extension://{id}/" };
            if (File.Exists(manifest))
            {
                try
                {
                    using var previous = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (previous.RootElement.TryGetProperty("allowed_origins", out var existing))
                        foreach (var origin in existing.EnumerateArray())
                            if (origin.GetString() is { } value) origins.Add(value);
                }
                catch (JsonException) { }
            }
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { name = "com.equora.nativehost", description = "Equora usage restrictions", path = exe, type = "stdio", allowed_origins = origins }));
            foreach (var browser in new[] { @"Google\Chrome", @"Microsoft\Edge" })
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\{browser}\NativeMessagingHosts\com.equora.nativehost");
                key.SetValue("", manifest);
            }
            Report("已连接。请在浏览器扩展管理页重新加载 Equora 扩展。");
        }
        catch (Exception ex) { Report(ex.Message, true); }
    }
}
