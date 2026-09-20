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

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CalendarBody is null) return;
        var narrow = e.NewSize.Width < 780;
        SidebarColumn.Width = new GridLength(narrow ? 0 : 240);
        Grid.SetColumn(CalendarSidebar, narrow ? 1 : 0);
        Grid.SetRowSpan(CalendarSidebar, narrow ? 1 : 2);
        CalendarSidebar.MaxHeight = narrow ? 220 : double.PositiveInfinity;
        Grid.SetRow(CalendarBody, narrow ? 1 : 0);
        Grid.SetRowSpan(CalendarBody, narrow ? 1 : 2);
        Week.Width = Math.Max(560, e.NewSize.Width - (narrow ? 0 : 240) - 44);
    }

    private void OnViewModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AgendaList is null || Week is null) return;
        var agenda = ViewModel.ViewModeIndex == 2;
        AgendaList.Visibility = agenda ? Visibility.Visible : Visibility.Collapsed;
        WeekHost.Visibility = agenda ? Visibility.Collapsed : Visibility.Visible;
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

    private async void OnNewBlock(object sender, RoutedEventArgs e) => await Controls.TimeBlockEditor.ShowAsync(XamlRoot, ViewModel.SelectedDay.AddHours(9), ViewModel.SelectedDay.AddHours(10));
    private async void OnExportIcs(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"equora-calendar-{DateTime.Now:yyyyMMdd}", SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("日历文件", new List<string> { ".ics" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
            var file = await picker.PickSaveFileAsync();
            if (file is not null) ViewModel.ExportIcs(file.Path);
        }
        catch (Exception ex) { ViewModel.StatusText = $"导出失败：{ex.Message}"; }
    }

    private async void OnImportIcs(object sender, RoutedEventArgs e)
    {
        try
        {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
        picker.FileTypeFilter.Add(".ics");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ViewModel.ImportIcs(file.Path);
        }
        catch (Exception ex) { ViewModel.StatusText = $"导入失败：{ex.Message}"; }
    }
}
