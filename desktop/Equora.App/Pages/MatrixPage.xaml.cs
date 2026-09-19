using Equora.App.NativeInterop;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Equora.App.Pages;

public sealed partial class MatrixPage : Page
{
    private MatrixViewModel ViewModel => AppServices.Matrix;

    public MatrixPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Refresh();
    }

    private void OnTaskDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 1 && e.Items[0] is TaskDto task)
        {
            e.Data.Properties["task"] = task;
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void OnQuadrantDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void OnQuadrantDrop(object sender, DragEventArgs e)
    {
        if (sender is not Grid { Tag: string tag } || !int.TryParse(tag, out var q))
        {
            return;
        }
        if (!e.DataView.Properties.TryGetValue("task", out var raw) || raw is not TaskDto task)
        {
            return;
        }
        ViewModel.MoveToQuadrant(task, (Quadrant)(q - 1));
    }

    /// <summary>双击卡片 = 设/取消今日要事(Q1/Q2 来源)。</summary>
    private void OnToggleBigThree(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TaskDto task)
        {
            ViewModel.StatusText = ViewModel.ToggleBigThree(task);
        }
    }
}
