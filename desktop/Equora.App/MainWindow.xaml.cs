using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Equora.App.Pages;

namespace Equora.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "衡序 Equora";
        ExtendsContentIntoTitleBar = false;
        ContentFrame.Navigate(typeof(HomePage));
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnNavSelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;

        var page = tag switch
        {
            "home" => typeof(HomePage),
            "tasks" => typeof(TasksPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null,
                new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        }
    }
}
