using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Equora.App.Pages;

public sealed partial class FocusPage : Page
{
    private FocusViewModel ViewModel => AppServices.FocusVm;

    public FocusPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Load();
        if (!ViewModel.IsRunning) { ViewModel.Rounds = Appearance.Current.PomodoroRounds; ViewModel.BreakMinutes = Appearance.Current.BreakMinutes; }

    }

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CapturePanel is null) return;
        var narrow = e.NewSize.Width < 880;
        CaptureColumn.Width = new GridLength(narrow ? 0 : 340);
        Grid.SetColumn(CapturePanel, narrow ? 0 : 1);
        Grid.SetRow(CapturePanel, narrow ? 1 : 0);
    }
    private void OnCycleSettingsChanged(object sender, RoutedEventArgs e) => Appearance.Save(Appearance.Current with { PomodoroRounds = Math.Clamp(ViewModel.Rounds, 1, 20), BreakMinutes = Math.Clamp(ViewModel.BreakMinutes, 1, 60) });

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: FocusProfileDto profile })
        {
            ViewModel.ApplyProfile(profile);
        }
    }

    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SubmitCapture();
            e.Handled = true;
        }
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e) => SubmitCapture();

    private void SubmitCapture()
    {
        ViewModel.Capture(CaptureBox.Text);
        CaptureBox.Text = "";
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DistractionDto d }) ViewModel.DiscardCommand.Execute(d);
    }

    private void OnResolveAsTask(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DistractionDto d })
        {
            ViewModel.ResolveAsTask(d);
        }
    }
}
