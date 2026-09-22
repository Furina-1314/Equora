using Equora.App.NativeInterop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Controls;
internal static class TimeBlockEditor
{
    public static async Task ShowAsync(XamlRoot root, DateTimeOffset start, DateTimeOffset end, string? id = null, bool createTask = false)
    {
        var existing = id is null ? null : AppServices.Data.GetBlock(id);
        if (existing is not null) { start = existing.StartAt.ToLocalTime(); end = existing.EndAt.ToLocalTime(); }
        var task = new CheckBox { Content = "同时创建任务", IsChecked = createTask, Visibility = id is null ? Visibility.Visible : Visibility.Collapsed };
        var title = new TextBox { Header = "时间段标题", Text = existing?.Title ?? "", PlaceholderText = "不添加到任务列表也可以设置标题" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(title, "BlockTitle");
        var note = new TextBox { Header = "备注", Text = existing?.Note ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
        var fromDate = new CalendarDatePicker { Header = "开始日期", Date = start };
        var fromTime = new TimePicker { Header = "开始时间", ClockIdentifier = "24HourClock", Time = start.TimeOfDay };
        var toDate = new CalendarDatePicker { Header = "结束日期", Date = end };
        var toTime = new TimePicker { Header = "结束时间", ClockIdentifier = "24HourClock", Time = end.TimeOfDay };
        var color = new TextBox { Header = "时间段颜色（#RRGGBB）", Text = AppServices.Data.ListCalendars().FirstOrDefault(c => c.Id == existing?.CalendarId)?.Color is { Length: 7 } savedColor ? savedColor : "#0078D4" };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 10, MinWidth = 320 };
        foreach (var element in new UIElement[] { task, title, note, fromDate, fromTime, toDate, toTime, color, error }) content.Children.Add(element);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(note, "BlockNote");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(color, "BlockColor");
        var dialog = new ContentDialog { XamlRoot = root, Title = id is null ? "新建时间段" : "编辑时间段", PrimaryButtonText = "保存", CloseButtonText = "取消", Content = new ScrollViewer { Content = content, MaxHeight = 540 } };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                if (fromDate.Date is null || toDate.Date is null) throw new ArgumentException("请选择开始和结束日期。");
                if (id is null && task.IsChecked == true && string.IsNullOrWhiteSpace(title.Text)) throw new ArgumentException("请输入任务标题。");
                var from = fromDate.Date.Value.Date + fromTime.Time;
                var to = toDate.Date.Value.Date + toTime.Time;
                if (TimeZoneInfo.Local.IsInvalidTime(from) || TimeZoneInfo.Local.IsInvalidTime(to)) throw new ArgumentException("该时间在本地夏令时切换中不存在。");
                AppServices.Calendar.SaveTimeBlock(id, new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)), new DateTimeOffset(to, TimeZoneInfo.Local.GetUtcOffset(to)), note.Text, color.Text.Trim(), id is null && task.IsChecked == true ? title.Text : null, title.Text);
            }
            catch (Exception ex) { error.Text = ex.Message; e.Cancel = true; }
        };
        await dialog.ShowAsync();
    }
}
