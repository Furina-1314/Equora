using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Equora.App.Services;

namespace Equora.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        AboutText.Text = $"衡序 Equora 0.1.0(M0 骨架)\n数据目录:{AppPaths.DataDirectory}";
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (App.MainWindow?.Content is not FrameworkElement root) return;

        root.RequestedTheme = ThemeChoice.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
