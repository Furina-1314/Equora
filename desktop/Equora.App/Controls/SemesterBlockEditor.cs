using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Controls;

internal static class SemesterBlockEditor
{
    public static async Task ShowAsync(XamlRoot root)
    {
        var semester = Appearance.Current.Semester;
        if (!semester.Enabled) return;
        var title = new TextBox { Header = "日程标题", PlaceholderText = "例如：高等数学" };
        var addToTasks = new CheckBox { Content = "同时添加到任务列表", IsChecked = true };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(title, "SemesterBlockTitle");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(addToTasks, "SemesterAddToTasks");
        var mode = new ComboBox { Header = "安排周次", ItemsSource = new[] { "每周", "特定周" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var weeks = new TextBox { Header = $"周次（1–{semester.WeekCount}）", PlaceholderText = "例如：1,3,5-8", Visibility = Visibility.Collapsed };
        var weekday = new ComboBox { Header = "星期", ItemsSource = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var from = new TimePicker { Header = "开始时间", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(9) };
        var to = new TimePicker { Header = "结束时间", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(10) };
        var note = new TextBox { Header = "备注", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var (color, colorRow) = Controls.ColorHexField.Create("日程颜色（#RRGGBB）", "#0078D4");
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 10, MinWidth = 320 };
        foreach (var element in new UIElement[] {
            new TextBlock { Text = $"{semester.Name} · {semester.StartDate:yyyy-MM-dd} 至 {semester.EndDate:yyyy-MM-dd}\n起始日期所在周为第 1 周，每周从周一开始。每项独立保存，关闭学期模式后仍保留。", TextWrapping = TextWrapping.Wrap },
            title, addToTasks, mode, weeks, weekday, from, to, note, colorRow, preview, error }) content.Children.Add(element);
        var dialog = new ContentDialog { XamlRoot = root, Title = "按学期批量添加日程", PrimaryButtonText = "添加", CloseButtonText = "取消",
            Content = new ScrollViewer { Content = content, MaxHeight = 540 } };
        void UpdatePreview()
        {
            try
            {
                var slots = semester.Slots(mode.SelectedIndex == 0 ? null : weeks.Text,
                    (DayOfWeek)((weekday.SelectedIndex + 1) % 7), from.Time, to.Time);
                preview.Text = $"将添加 {slots.Count} 项：{slots[0].Start:yyyy-MM-dd} 至 {slots[^1].Start:yyyy-MM-dd}。{(addToTasks.IsChecked == true ? "同时创建独立任务。" : "仅添加到日历。")}";
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (ArgumentException ex) { preview.Text = ex.Message; dialog.IsPrimaryButtonEnabled = false; }
        }
        mode.SelectionChanged += (_, _) => { weeks.Visibility = mode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible; UpdatePreview(); };
        weeks.TextChanged += (_, _) => UpdatePreview();
        addToTasks.Checked += (_, _) => UpdatePreview();
        addToTasks.Unchecked += (_, _) => UpdatePreview();
        weekday.SelectionChanged += (_, _) => UpdatePreview();
        from.TimeChanged += (_, _) => UpdatePreview();
        to.TimeChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                if (!Appearance.Current.Semester.Enabled || Appearance.Current.Semester.Id != semester.Id)
                    throw new ArgumentException("学期设置已变更，请关闭窗口后重新添加。");
                AppServices.Calendar.CreateSemesterBlocks(semester, mode.SelectedIndex == 0 ? null : weeks.Text,
                    (DayOfWeek)((weekday.SelectedIndex + 1) % 7), from.Time, to.Time, title.Text, note.Text, color.Text.Trim(), addToTasks.IsChecked == true);
                AppServices.Tasks.Refresh();
            }
            catch (Exception ex) { error.Text = ex.Message; e.Cancel = true; }
        };
        await dialog.ShowAsync();
    }
}
