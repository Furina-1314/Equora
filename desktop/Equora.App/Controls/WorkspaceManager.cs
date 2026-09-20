using Equora.App.NativeInterop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Controls;

internal static class WorkspaceManager
{
    public static async Task ShowAsync(XamlRoot root, bool projects)
    {
        var list = new ListView { MaxHeight = 220, DisplayMemberPath = "Name" };
        var name = new TextBox { Header = "名称", MaxLength = 100 };
        var color = new TextBox { Header = "颜色（可选，例如 #0078D4）", MaxLength = 7 };
        var goal = new TextBox { Header = "项目目标", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Visibility = projects ? Visibility.Visible : Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(name, "WorkspaceName");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(color, "WorkspaceColor");
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var fresh = new Button { Content = projects ? "新增项目" : "新增标签" };
        var confirm = new CheckBox { Content = "确认删除选中分类（任务将保留）" };
        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        foreach (var control in new UIElement[] { list, fresh, name, color, goal, message, confirm }) panel.Children.Add(control);
        var dialog = new ContentDialog { XamlRoot = root, Title = projects ? "管理项目" : "管理标签", Content = new ScrollViewer { Content = panel, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = "保存", SecondaryButtonText = "删除选中项", CloseButtonText = "关闭", IsSecondaryButtonEnabled = false };
        void Reload() => list.ItemsSource = projects ? (object)AppServices.Data.ListProjects() : AppServices.Data.ListTags();
        list.SelectionChanged += (_, _) =>
        {
            name.Text = list.SelectedItem switch { ProjectDto p => p.Name, TagDto t => t.Name, _ => "" };
            color.Text = list.SelectedItem switch { ProjectDto p => p.Color, TagDto t => t.Color, _ => "" };
            goal.Text = (list.SelectedItem as ProjectDto)?.Goal ?? "";
            confirm.IsChecked = false;
            dialog.IsSecondaryButtonEnabled = list.SelectedItem is not null;
        };
        fresh.Click += (_, _) => { list.SelectedItem = null; name.Text = ""; color.Text = ""; goal.Text = ""; name.Focus(FocusState.Programmatic); };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            e.Cancel = true;
            try
            {
                var title = name.Text.Trim();
                if (title.Length == 0) throw new ArgumentException("请输入名称。");
                if (color.Text.Length > 0 && !Appearance.TryColor(color.Text, out var parsedColor)) throw new ArgumentException("颜色格式应为 #RRGGBB。");
                object saved;
                if (projects) saved = list.SelectedItem is ProjectDto p ? AppServices.Data.UpdateProject(p with { Name = title, Color = color.Text, Goal = goal.Text }) : AppServices.Data.CreateProject(title, color.Text, goal.Text);
                else saved = list.SelectedItem is TagDto t ? AppServices.Data.UpdateTag(t with { Name = title, Color = color.Text }) : AppServices.Data.CreateTag(title, color.Text);
                Reload();
                list.SelectedItem = saved switch
                {
                    ProjectDto project => list.Items.OfType<ProjectDto>().FirstOrDefault(item => item.Id == project.Id),
                    TagDto tag => list.Items.OfType<TagDto>().FirstOrDefault(item => item.Id == tag.Id),
                    _ => null
                };
                message.Text = "已保存";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        dialog.SecondaryButtonClick += (_, e) =>
        {
            e.Cancel = true;
            if (confirm.IsChecked != true) { message.Text = "请勾选删除确认。"; return; }
            try
            {
                if (list.SelectedItem is ProjectDto p) AppServices.Data.DeleteProject(p.Id);
                else if (list.SelectedItem is TagDto t) AppServices.Data.DeleteTag(t.Id);
                Reload(); message.Text = "分类已删除，任务保留。";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        Reload();
        await dialog.ShowAsync();
    }
}
