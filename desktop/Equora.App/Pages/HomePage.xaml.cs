using Equora.App.NativeInterop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        var tasks = AppServices.Data.ListTasks();
        var pending = tasks.Where(t => t.Status is not (TaskStatus.Done or TaskStatus.Cancelled)).ToList();
        DateLabel.Text = DateTime.Now.ToString("yyyy 年 M 月 d 日 · dddd");
        SummaryLabel.Text = $"待处理 {pending.Count} 项 · 已完成 {tasks.Count(t => t.Status == TaskStatus.Done)} 项";
        OverviewTasks.ItemsSource = pending.Take(8).ToList();
        EmptyLabel.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnTasks(object sender, RoutedEventArgs e) => App.MainWindow?.ShowPage(typeof(TasksPage));
    private void OnCalendar(object sender, RoutedEventArgs e) => App.MainWindow?.ShowPage(typeof(CalendarPage));
    private void OnFocus(object sender, RoutedEventArgs e) => App.MainWindow?.ShowPage(typeof(FocusPage));
    private void OnTaskClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not TaskDto task) return;
        App.MainWindow?.ShowPage(typeof(TasksPage));
        var vm = AppServices.Tasks;
        vm.SearchText = "";
        vm.SelectedList = vm.Lists.First(l => l.Key == "all");
        vm.Refresh();
        vm.SelectedTask = vm.Tasks.FirstOrDefault(t => t.Id == task.Id);
    }
}
