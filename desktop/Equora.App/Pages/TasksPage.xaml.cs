using Equora.App.NativeInterop;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Equora.App.Pages;

public sealed partial class TasksPage : Page
{
    private TasksViewModel ViewModel => AppServices.Tasks;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public TasksPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Run(ViewModel.RunSearch); };
        Loaded += (_, _) => { ViewModel.PropertyChanged += OnViewModelChanged; UpdateLayoutMode(); };
        Unloaded += (_, _) => { _searchTimer.Stop(); ViewModel.PropertyChanged -= OnViewModelChanged; };
        var undo = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Z, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        undo.Invoked += (_, e) => { Run(ViewModel.Undo); e.Handled = true; };
        KeyboardAccelerators.Add(undo);
        ViewModel.InitializeNav();
        if (ViewModel.SelectedList is null && ViewModel.Lists.Count > 0)
        {
            ViewModel.SelectedList = ViewModel.Lists[0]; // 收集箱,触发首次加载
        }
        else Run(ViewModel.Refresh);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { ViewModel.StatusText = $"操作未完成：{ex.Message}"; }
    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TasksViewModel.SelectedTask)) UpdateLayoutMode();
    }

    private void OnBackToTasks(object sender, RoutedEventArgs e) => ViewModel.SelectedTask = null;

    private void UpdateLayoutMode()
    {
        if (TaskWorkspace is null) return;
        var narrow = ActualWidth < 650;
        var compact = ActualWidth < 1040;
        var showDetail = compact && ViewModel.SelectedTask is not null;
        ListColumn.Width = new GridLength(narrow ? 0 : 196);
        ListSidebar.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        CompactLists.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        CompactManagement.Visibility = CompactLists.Visibility;
        DetailColumn.Width = new GridLength(compact ? 0 : 320);
        DetailRow.Height = new GridLength(0);
        Grid.SetRow(DetailHost, 0);
        Grid.SetColumn(DetailHost, compact ? 1 : 2);
        Grid.SetColumnSpan(DetailHost, 1);
        DetailHost.Visibility = !compact || showDetail ? Visibility.Visible : Visibility.Collapsed;
        TaskWorkspace.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
        BackToTasks.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ViewModel.IsRefreshing) ViewModel.SelectedTask = TaskList.SelectedItem as TaskDto;
    }

    private void OnDeleteTask(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskDto task }) Run(() => ViewModel.DeleteTask(task));
    }

    private void OnRestoreTask(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskDto task }) Run(() => ViewModel.RestoreTask(task));
    }

    private void OnAddTask(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.NewTaskTitle)) { NewTaskBox.Focus(FocusState.Programmatic); return; }
        Run(ViewModel.NewTask);
        NewTaskBox.Focus(FocusState.Programmatic);
    }

    private void OnNewTaskKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        OnAddTask(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
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
            DispatcherQueue.TryEnqueue(() => Run(() => ViewModel.ToggleDone(task)));
        }
    }

    private async void OnManageProjects(object sender, RoutedEventArgs e) => await ManageWorkspace(true);
    private async void OnManageTags(object sender, RoutedEventArgs e) => await ManageWorkspace(false);
    private async Task ManageWorkspace(bool projects)
    {
        await Controls.WorkspaceManager.ShowAsync(XamlRoot, projects);
        var previous = ViewModel.SelectedList;
        ViewModel.InitializeNav();
        if (previous?.ProjectId is string projectId)
        {
            var project = ViewModel.Projects.FirstOrDefault(p => p.Id == projectId);
            ViewModel.SelectedList = project is null ? ViewModel.Lists[0] : previous with { Title = project.Name };
        }
        if (previous?.TagId is string tagId)
        {
            var tag = ViewModel.Tags.FirstOrDefault(t => t.Id == tagId);
            ViewModel.SelectedList = tag is null ? ViewModel.Lists[0] : previous with { Title = tag.Name };
        }
        ViewModel.Refresh();
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
