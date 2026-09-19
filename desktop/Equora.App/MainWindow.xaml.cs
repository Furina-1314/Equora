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
        ContentFrame.Navigate(typeof(TasksPage));
        Nav.SelectedItem = Nav.MenuItems[0];

        // App 内快捷键(全局热键 RegisterHotKey 在真机阶段接窗口过程)。
        // Window 没有 KeyboardAccelerators,挂在内容根元素上;Invoked 在加速器上。
        Activated += (_, _) =>
        {
            if (Content is not Microsoft.UI.Xaml.UIElement root) return;
            if (root.KeyboardAccelerators.Count > 0) return; // 只挂一次
            AddAccelerator(root, Windows.System.VirtualKey.N,
                Windows.System.VirtualKeyModifiers.Control);
            AddAccelerator(root, Windows.System.VirtualKey.K,
                Windows.System.VirtualKeyModifiers.Control);
            AddAccelerator(root, Windows.System.VirtualKey.Space,
                Windows.System.VirtualKeyModifiers.Control |
                Windows.System.VirtualKeyModifiers.Shift);
        };
    }

    private void AddAccelerator(Microsoft.UI.Xaml.UIElement root,
        Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
    {
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
        };
        accelerator.Invoked += OnAcceleratorInvoked;
        root.KeyboardAccelerators.Add(accelerator);
    }

    private void OnNavSelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag) return;

        var page = tag switch
        {
            "home" => typeof(HomePage),
            "tasks" => typeof(TasksPage),
            "calendar" => typeof(CalendarPage),
            "matrix" => typeof(MatrixPage),
            "focus" => typeof(FocusPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null,
                new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        }
    }
    private async void OnAcceleratorInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        var ctrl = sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
        var shift = sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift);

        if (ctrl && shift && sender.Key == Windows.System.VirtualKey.Space)
        {
            args.Handled = true;
            await Controls.QuickCaptureFlyout.ShowAsync(this);
        }
        else if (ctrl && sender.Key == Windows.System.VirtualKey.K)
        {
            args.Handled = true;
            await Controls.CommandPalette.ShowAsync(this, ContentFrame);
        }
        else if (ctrl && sender.Key == Windows.System.VirtualKey.N)
        {
            args.Handled = true;
            ContentFrame.Navigate(typeof(Pages.TasksPage));
            AppServices.Tasks.NewTaskCommand.Execute(null);
        }
    }
}
