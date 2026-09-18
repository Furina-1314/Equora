using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Equora.App.Pages;

public sealed partial class TasksPage : Page
{
    public TasksPage() => InitializeComponent();

    private void OnGoHome(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(HomePage));
}
