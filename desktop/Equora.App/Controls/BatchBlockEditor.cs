using Equora.App.NativeInterop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Controls;

internal static class BatchBlockEditor
{
    public static async Task ShowAsync(XamlRoot root, string id, bool delete)
    {
        try
        {
            var candidates = AppServices.Calendar.BatchCandidates(id);
            var grouped = !string.IsNullOrEmpty(candidates.First(b => b.Id == id).BatchId);
            // 用复选框打勾表示选中,直观且可逐项切换。
            var checks = new List<CheckBox>();
            var count = new TextBlock();
            var listPanel = new StackPanel { Spacing = 4 };
            foreach (var block in candidates)
            {
                var box = new CheckBox
                {
                    Content = $"{block.StartAt.ToLocalTime():yyyy-MM-dd HH:mm} · {(string.IsNullOrWhiteSpace(block.Title) ? block.Note : block.Title)}",
                    Tag = block,
                    IsChecked = grouped || block.Id == id
                };
                checks.Add(box);
                listPanel.Children.Add(box);
                box.Checked += (_, _) => UpdateCount();
                box.Unchecked += (_, _) => UpdateCount();
            }
            var all = new Button { Content = "全选 / 取消全选" };
            all.Click += (_, _) =>
            {
                var target = checks.All(c => c.IsChecked == true) ? false : true;
                foreach (var box in checks) box.IsChecked = target;
            };
            var changeTitle = new CheckBox { Content = "修改日程标题" };
            var title = new TextBox { Header = "新标题", IsEnabled = false };
            var changeNote = new CheckBox { Content = "修改备注" };
            var note = new TextBox { Header = "新备注（可留空）", IsEnabled = false };
            var changeColor = new CheckBox { Content = "修改颜色" };
            var color = new TextBox { Header = "颜色（#RRGGBB）", Text = "#0078D4", IsEnabled = false };
            var changeTime = new CheckBox { Content = "修改每天的时间（保留各自日期）" };
            var from = new TimePicker { Header = "开始时间", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(9), IsEnabled = false };
            var to = new TimePicker { Header = "结束时间", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(10), IsEnabled = false };
            changeTitle.Checked += (_, _) => title.IsEnabled = true; changeTitle.Unchecked += (_, _) => title.IsEnabled = false;
            changeNote.Checked += (_, _) => note.IsEnabled = true; changeNote.Unchecked += (_, _) => note.IsEnabled = false;
            changeColor.Checked += (_, _) => color.IsEnabled = true; changeColor.Unchecked += (_, _) => color.IsEnabled = false;
            changeTime.Checked += (_, _) => from.IsEnabled = to.IsEnabled = true;
            changeTime.Unchecked += (_, _) => from.IsEnabled = to.IsEnabled = false;
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var affectTasks = new CheckBox { Content = delete ? "同时将关联任务移入回收站" : "修改标题时，同时更新关联任务标题" };
            void UpdateCount() => count.Text = $"已选择 {checks.Count(c => c.IsChecked == true)} / {checks.Count} 个日程";
            UpdateCount();
            var content = new StackPanel { Spacing = 10, MinWidth = 340 };
            content.Children.Add(new TextBlock { Text = "同一批次的日程可跨周选择；旧版未记录批次的安排将列出全部日程，需手动勾选。默认只修改日历，勾选下方选项可同时处理关联任务。", TextWrapping = TextWrapping.Wrap });
            foreach (var element in new UIElement[] { all, new ScrollViewer { Content = listPanel, MaxHeight = 200, VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto }, count }) content.Children.Add(element);
            if (!delete)
                foreach (var element in new UIElement[] { changeTitle, title, changeNote, note, changeColor, color, changeTime, from, to }) content.Children.Add(element);
            else content.Children.Add(new TextBlock { Text = "默认只删除时间安排。勾选下方选项可同时将关联任务移入回收站。删除的时间安排无法撤销。", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(affectTasks);
            content.Children.Add(error);
            var dialog = new ContentDialog { XamlRoot = root, Title = delete ? "批量删除日程" : "批量编辑日程",
                PrimaryButtonText = delete ? "删除选中日程" : "保存选中日程", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer { Content = content, MaxHeight = 540 } };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    var selected = checks.Where(c => c.IsChecked == true).Select(c => (TimeBlockDto)c.Tag).ToArray();
                    if (selected.Length == 0) throw new ArgumentException("请至少选择一个日程。");
                    if (delete) AppServices.Calendar.DeleteBlocks(selected, affectTasks.IsChecked == true);
                    else
                    {
                        if (!(title.IsEnabled || note.IsEnabled || color.IsEnabled || from.IsEnabled)) throw new ArgumentException("请勾选至少一项要修改的内容。");
                        AppServices.Calendar.EditBlocks(selected, title.IsEnabled ? title.Text : null, note.IsEnabled ? note.Text : null,
                            color.IsEnabled ? color.Text.Trim() : null, from.IsEnabled ? from.Time : null, to.IsEnabled ? to.Time : null, affectTasks.IsChecked == true);
                    }
                    AppServices.Tasks.Refresh();
                }
                catch (Exception ex) { error.Text = ex.Message; args.Cancel = true; }
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) { AppServices.Calendar.StatusText = ex.Message; }
    }
}
