using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Controls;

internal static class CalendarEventEditor
{
    public static async Task ShowAsync(XamlRoot root, string id)
    {
        var existing = AppServices.Calendar.GetEvent(id);
        if (existing is null) return;
        var title = new TextBox { Header = "日程标题", Text = existing.Title };
        var location = new TextBox { Header = "地点", Text = existing.Location };
        var note = new TextBox { Header = "备注", Text = existing.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        var start = existing.StartAt.ToLocalTime();
        var end = existing.EndAt.ToLocalTime();
        var fromDate = new CalendarDatePicker { Header = "开始日期", Date = start };
        var fromTime = new TimePicker { Header = "开始时间", ClockIdentifier = "24HourClock", Time = start.TimeOfDay };
        var toDate = new CalendarDatePicker { Header = "结束日期", Date = end };
        var toTime = new TimePicker { Header = "结束时间", ClockIdentifier = "24HourClock", Time = end.TimeOfDay };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 10, MinWidth = 320 };
        foreach (var element in new UIElement[] { title, location, note, fromDate, fromTime, toDate, toTime, error }) content.Children.Add(element);
        var dialog = new ContentDialog { XamlRoot = root, Title = "编辑日程", PrimaryButtonText = "保存", CloseButtonText = "取消", Content = new ScrollViewer { Content = content, MaxHeight = 540 } };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                if (fromDate.Date is null || toDate.Date is null) throw new ArgumentException("请选择起止日期。");
                var from = fromDate.Date.Value.Date + fromTime.Time;
                var to = toDate.Date.Value.Date + toTime.Time;
                if (TimeZoneInfo.Local.IsInvalidTime(from) || TimeZoneInfo.Local.IsInvalidTime(to)) throw new ArgumentException("所选日期的时间在夏令时切换中不存在。");
                AppServices.Calendar.UpdateEvent(existing with
                {
                    Title = title.Text, Location = location.Text, Note = note.Text,
                    StartAt = new DateTimeOffset(from, TimeZoneInfo.Local.GetUtcOffset(from)),
                    EndAt = new DateTimeOffset(to, TimeZoneInfo.Local.GetUtcOffset(to))
                });
            }
            catch (Exception ex) { error.Text = ex.Message; e.Cancel = true; }
        };
        await dialog.ShowAsync();
    }
}
