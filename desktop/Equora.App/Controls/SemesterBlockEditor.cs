using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Controls;

internal static class SemesterBlockEditor
{
    public static async Task ShowAsync(XamlRoot root)
    {
        var semester = Appearance.Current.Semester;
        if (!semester.Enabled) return;
        var title = new TextBox { Header = "任务标题", PlaceholderText = "例如：高等数学" };
        var mode = new ComboBox { Header = "安排周次", ItemsSource = new[] { "每周", "特定周" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var weeks = new TextBox { Header = $"周次（1–{semester.WeekCount}）", PlaceholderText = "例如：1,3,5-8", Visibility = Visibility.Collapsed };
        var weekday = new ComboBox { Header = "星期", ItemsSource = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var from = new TimePicker { Header = "开始时间", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(9) };
        var to = new TimePicker { Header = "结束时间", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(10) };
        var note = new TextBox { Header = "备注", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var color = new TextBox { Header = "时间段颜色（#RRGGBB）", Text = "#0078D4" };
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 10, MinWidth = 320 };
        foreach (var element in new UIElement[] {
            new TextBlock { Text = $"{semester.Name} · {semester.StartDate:yyyy-MM-dd} 至 {semester.EndDate:yyyy-MM-dd}\n起始日期所在周为第 1 周，每周从周一开始。每项独立保存，关闭学期模式后仍保留。", TextWrapping = TextWrapping.Wrap },
            title, mode, weeks, weekday, from, to, note, color, preview, error }) content.Children.Add(element);
        var dialog = new ContentDialog { XamlRoot = root, Title = "按学期批量添加时间段", PrimaryButtonText = "添加", CloseButtonText = "取消",
            Content = new ScrollViewer { Content = content, MaxHeight = 540 } };
        void UpdatePreview()
        {
            try
            {
                var slots = semester.Slots(mode.SelectedIndex == 0 ? null : weeks.Text,
                    (DayOfWeek)((weekday.SelectedIndex + 1) % 7), from.Time, to.Time);
                preview.Text = $"将添加 {slots.Count} 项：{slots[0].Start:yyyy-MM-dd} 至 {slots[^1].Start:yyyy-MM-dd}，每项创建独立任务。";
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (ArgumentException ex) { preview.Text = ex.Message; dialog.IsPrimaryButtonEnabled = false; }
        }
        mode.SelectionChanged += (_, _) => { weeks.Visibility = mode.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible; UpdatePreview(); };
        weeks.TextChanged += (_, _) => UpdatePreview();
        weekday.SelectionChanged += (_, _) => UpdatePreview();
        from.TimeChanged += (_, _) => UpdatePreview();
        to.TimeChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                AppServices.Calendar.CreateSemesterBlocks(semester, mode.SelectedIndex == 0 ? null : weeks.Text,
                    (DayOfWeek)((weekday.SelectedIndex + 1) % 7), from.Time, to.Time, title.Text, note.Text, color.Text.Trim());
                AppServices.Tasks.Refresh();
            }
            catch (Exception ex) { error.Text = ex.Message; e.Cancel = true; }
        };
        await dialog.ShowAsync();
    }
}
