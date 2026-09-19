using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Equora.App.Controls;

/// <summary>命令面板条目。</summary>
public sealed record PaletteCommand(string Title, string Glyph, string Hint, Action Run);

/// <summary>Ctrl+K 命令面板:统一入口跳页面与常用操作。</summary>
public sealed partial class CommandPalette : UserControl
{
    private List<PaletteCommand> _commands = new();
    private Action? _close;

    public CommandPalette()
    {
        InitializeComponent();
    }

    public static IReadOnlyList<PaletteCommand> DefaultCommands(Window window,
        Microsoft.UI.Xaml.Controls.Frame frame) => new List<PaletteCommand>
    {
        new("去 · 任务", "\uE71D", "Ctrl+1", () => frame.Navigate(typeof(Pages.TasksPage))),
        new("去 · 日历", "\uE787", "Ctrl+2", () => frame.Navigate(typeof(Pages.CalendarPage))),
        new("去 · 四象限", "\uE8FD", "Ctrl+3", () => frame.Navigate(typeof(Pages.MatrixPage))),
        new("去 · 设置", "\uE713", "", () => frame.Navigate(typeof(Pages.SettingsPage))),
        new("快速收集任务", "\uE710", "Ctrl+Shift+Space",
            async () => await QuickCaptureFlyout.ShowAsync(window)),
        new("回到今天", "\uE8F1", "T",
            () => { AppServices.Calendar.GoToday(); frame.Navigate(typeof(Pages.CalendarPage)); }),
        new("立即备份", "\uE74E", "",
            () => AppServices.Data.CreateBackup(Services.AppPaths.BackupsDirectory)),
    };

    /// <summary>在主窗口弹出命令面板。</summary>
    public static async Task ShowAsync(Window owner, Frame frame)
    {
        var control = new CommandPalette();
        var dialog = new ContentDialog
        {
            Title = "命令面板",
            Content = control,
            CloseButtonText = "关闭",
            XamlRoot = owner.Content is FrameworkElement root ? root.XamlRoot : null,
        };
        control.Initialize(DefaultCommands(owner, frame), () => dialog.Hide());
        await dialog.ShowAsync();
    }

    public void Initialize(IEnumerable<PaletteCommand> commands, Action close)
    {
        _commands = commands.ToList();
        _close = close;
        ResultsList.ItemsSource = _commands;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = SearchBox.Text.Trim();
        ResultsList.ItemsSource = string.IsNullOrEmpty(keyword)
            ? _commands
            : _commands.Where(c => c.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                   || c.Hint.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                       .ToList();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ResultsList.SelectedItem is PaletteCommand cmd)
        {
            RunAndClose(cmd);
        }
    }

    private void OnItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteCommand cmd) RunAndClose(cmd);
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is PaletteCommand cmd)
        {
            RunAndClose(cmd);
        }
    }

    private void RunAndClose(PaletteCommand cmd)
    {
        _close?.Invoke();
        cmd.Run();
    }
}
