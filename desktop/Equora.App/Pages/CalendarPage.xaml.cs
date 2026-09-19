using Equora.App.NativeInterop;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Equora.App.Pages;

public sealed partial class CalendarPage : Page
{
    private CalendarViewModel ViewModel => AppServices.Calendar;

    public CalendarPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Refresh();
    }

    private void OnViewModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var agenda = ViewModel.ViewModeIndex == 2;
        AgendaList.Visibility = agenda ? Visibility.Visible : Visibility.Collapsed;
        Week.Visibility = agenda ? Visibility.Collapsed : Visibility.Visible;
        if (agenda)
        {
            AgendaList.ItemsSource = ViewModel.Items;
        }
    }

    private void OnPrev(object sender, RoutedEventArgs e) =>
        ViewModel.PrevWeekCommand.Execute(null);

    private void OnNext(object sender, RoutedEventArgs e) =>
        ViewModel.NextWeekCommand.Execute(null);

    private void OnTaskDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 1 && e.Items[0] is TaskDto task)
        {
            e.Data.Properties["task"] = task;
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
    }

    private void OnExportIcs(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.ExportIcs();
        if (!string.IsNullOrEmpty(path))
        {
            ViewModel.StatusText = $"ICS 已导出:{path}";
        }
    }

    private async void OnImportIcs(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".ics");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ViewModel.ImportIcs(file.Path);
    }
}
