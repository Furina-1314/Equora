using Equora.App.NativeInterop;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Equora.App.Controls;

public sealed partial class TaskDetailPanel : UserControl
{
    private TaskDetailViewModel ViewModel => AppServices.Tasks.Detail;

    /// <summary>标签行的展示包装(名称 + 是否已指派)。</summary>
    public sealed class TagRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public bool IsAssigned { get; set; }
    }

    private List<TagRow> _tagRows = new();

    public TaskDetailPanel()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskDetailViewModel.IsLoaded) ||
                e.PropertyName == nameof(TaskDetailViewModel.AssignedTagIds))
            {
                RebuildTagRows();
            }
        };
        RebuildTagRows();
    }

    private void RebuildTagRows()
    {
        _tagRows = ViewModel.AllTags
            .Select(t => new TagRow
            {
                Id = t.Id,
                Name = t.Name,
                IsAssigned = ViewModel.IsTagAssigned(t.Id),
            })
            .ToList();
        TagList.ItemsSource = _tagRows;
    }

    private void OnCommitDue(object sender, RoutedEventArgs e) => ViewModel.CommitDue();

    private void OnEstimateLostFocus(object sender, RoutedEventArgs e) =>
        ViewModel.CommitEstimate();

    private void OnTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TagRow row })
        {
            ViewModel.ToggleTag(row.Id);
            RebuildTagRows();
        }
    }

    private void OnAddChecklist(object sender, RoutedEventArgs e) => AddChecklist();

    private void OnChecklistKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) AddChecklist();
    }

    private void AddChecklist()
    {
        var text = NewChecklistText.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        ViewModel.AddChecklistItem(text);
        NewChecklistText.Text = "";
    }

    private void OnToggleChecklist(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ChecklistItemDto item })
        {
            ViewModel.ToggleChecklistItem(item);
        }
    }

    private void OnRemoveChecklist(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ChecklistItemDto item })
        {
            ViewModel.RemoveChecklistItem(item);
        }
    }
}
