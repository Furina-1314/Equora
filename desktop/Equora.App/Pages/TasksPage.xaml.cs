using Equora.App.NativeInterop;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Equora.App.Pages;

public sealed partial class TasksPage : Page
{
    private TasksViewModel ViewModel => AppServices.Tasks;

    public TasksPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.InitializeNav();
        if (ViewModel.SelectedList is null && ViewModel.Lists.Count > 0)
        {
            ViewModel.SelectedList = ViewModel.Lists[0]; // 收集箱,触发首次加载
        }
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ViewModel.RunSearchCommand.Execute(null);
        }
    }

    private void OnToggleDoneClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TaskDto task })
        {
            ViewModel.ToggleDoneCommand.Execute(task);
        }
    }

    private void OnProjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: ProjectDto project })
        {
            ViewModel.SelectedList = new NavItem(
                $"project:{project.Id}", project.Name, "\uE8F1", ProjectId: project.Id);
        }
    }

    private void OnTagSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: TagDto tag })
        {
            ViewModel.SelectedList = new NavItem(
                $"tag:{tag.Id}", $"#{tag.Name}", "\uE8EC", TagId: tag.Id);
        }
    }
}
