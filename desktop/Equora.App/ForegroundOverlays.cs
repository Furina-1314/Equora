using System.Runtime.InteropServices;
using Equora.App.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Equora.App;

// These windows belong to Equora but follow the foreground application's rectangle.
// The two notices do not accept input; the full block page keeps its buttons interactive.
internal sealed class ForegroundOverlays : IDisposable
{
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WsPopup = unchecked((int)0x80000000);
    private const long WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExTransparent = 0x20;
    private const uint SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020, SwpShowWindow = 0x0040;
    private static readonly IntPtr Topmost = new(-1);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly Window _block = new();
    private readonly Window _nudge = new();
    private readonly Window _quota = new();
    private readonly TextBlock _blockName = new();
    private readonly TextBlock _blockReason = new();
    private readonly TextBlock _allowFeedback = new();
    private readonly TextBlock _nudgeText = new();
    private readonly TextBlock _quotaText = new();
    private readonly Button _allow = new() { Content = "临时允许 5 分钟" };
    private readonly Action<string> _grant;
    private string _targetName = "";
    public IntPtr TargetWindow { get; private set; }
    public bool IsBlocking { get; private set; }
    public bool Owns(IntPtr hwnd) => hwnd != IntPtr.Zero && (hwnd == Handle(_block) || hwnd == Handle(_nudge) || hwnd == Handle(_quota));

    public ForegroundOverlays(Action<string> grant)
    {
        _grant = grant;
        _block.ExtendsContentIntoTitleBar = true;
        _nudge.ExtendsContentIntoTitleBar = true;
        _quota.ExtendsContentIntoTitleBar = true;
        _block.Content = BuildBlock();
        _nudge.Content = Notice(_nudgeText, true);
        _quota.Content = Notice(_quotaText, false);
        Configure(_block, false);
        Configure(_nudge, true);
        Configure(_quota, true);
    }

    private static IntPtr Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private static TextBlock Text(string value, double size, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = muted ? Brush(90, 90, 90) : Brush(32, 32, 32)
    };

    private UIElement BuildBlock()
    {
        var root = new Grid { Background = Brush(243, 243, 243), Padding = new Thickness(24) };
        var card = new Border { MaxWidth = 560, BorderBrush = Brush(195, 47, 55), BorderThickness = new Thickness(0, 4, 0, 0), Background = Brush(255, 255, 255), Padding = new Thickness(48, 44, 48, 20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(new FontIcon { Glyph = "\uE72E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 40, Foreground = Brush(32, 32, 32), HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(Text("此应用暂时不可使用", 30));
        _blockName.FontSize = 20; _blockName.Foreground = Brush(32, 32, 32); _blockName.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_blockName);
        stack.Children.Add(Text("已触发你在 Equora 中设置的时段、专注或每日使用时长限制。", 15));
        _blockReason.FontSize = 14; _blockReason.Foreground = Brush(90, 90, 90);
        stack.Children.Add(_blockReason);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        var back = new Button { Content = "返回桌面", Padding = new Thickness(20, 12, 20, 12) };
        back.Click += (_, _) => { ForegroundAccess.Minimize(TargetWindow); Hide(); };
        _allow.Background = Brush(195, 47, 55); _allow.Foreground = Brush(255, 255, 255);
        _allow.Padding = new Thickness(20, 12, 20, 12);
        _allow.Click += (_, _) => _grant(_targetName);
        buttons.Children.Add(back); buttons.Children.Add(_allow);
        stack.Children.Add(buttons);
        _allowFeedback.FontSize = 14; _allowFeedback.Foreground = Brush(90, 90, 90); _allowFeedback.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(_allowFeedback);
        stack.Children.Add(Text("每个应用仅可临时允许一次，重启不会重置。允许后右上角显示倒计时，到期后恢复限制。可在桌面端“使用限制”中调整规则。", 12, true));
        var brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png")), Width = 28, Height = 28 });
        brand.Children.Add(new TextBlock { Text = "EQUORA", FontFamily = new FontFamily("Segoe UI Variable"), FontSize = 18, CharacterSpacing = 120, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(32, 32, 32) });
        stack.Children.Add(brand);
        card.Child = stack; root.Children.Add(card);
        return root;
    }

    private static UIElement Notice(TextBlock label, bool warning)
    {
        var root = new Grid { Background = Brush(32, 32, 32), Padding = new Thickness(16, 10, 16, 10) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var stripe = new Border { Background = warning ? Brush(195, 47, 55) : Brush(195, 47, 55) };
        root.Children.Add(stripe);
        label.Foreground = Brush(255, 255, 255); label.FontSize = 14; label.TextWrapping = TextWrapping.Wrap;
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(label, 1); root.Children.Add(label);
        return root;
    }

    private static void Configure(Window window, bool passive)
    {
        var hwnd = Handle(window);
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr((style & ~(WsCaption | WsThickFrame)) | WsPopup));
        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | WsExToolWindow;
        if (passive) ex |= WsExNoActivate | WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(ex));
        SetWindowPos(hwnd, Topmost, 0, 0, 1, 1, SwpNoActivate | SwpFrameChanged);
        window.AppWindow.Hide();
    }

    private static void Place(Window window, int x, int y, int width, int height)
    {
        SetWindowPos(Handle(window), Topmost, x, y, Math.Max(width, 1), Math.Max(height, 1), SwpNoActivate | SwpShowWindow);
        window.AppWindow.Show(false);
    }

    public void Update(IntPtr target, string name, string? blockReason, bool allowUsed, DateTimeOffset? allowUntil, string? nudge, double? quotaSeconds)
    {
        var bounds = ForegroundAccess.Bounds(target);
        if (bounds is null) { Hide(); return; }
        TargetWindow = target; _targetName = name;
        var rect = bounds.Value;
        var scale = Math.Max(1, GetDpiForWindow(target) / 96.0);
        var inset = (int)Math.Ceiling(16 * scale);
        if (blockReason is not null)
        {
            IsBlocking = true;
            _blockName.Text = name;
            _blockReason.Text = blockReason;
            _allow.IsEnabled = !allowUsed;
            _allowFeedback.Text = allowUsed ? "临时允许机会已用完。" : "";
            Place(_block, rect.Left, rect.Top, rect.Width, rect.Height);
            _nudge.AppWindow.Hide(); _quota.AppWindow.Hide();
            return;
        }
        IsBlocking = false;
        _block.AppWindow.Hide();
        if (!string.IsNullOrWhiteSpace(nudge))
        {
            _nudgeText.Text = "Equora · " + nudge;
            var width = Math.Min((int)Math.Ceiling(560 * scale), Math.Max(1, rect.Width - 2 * inset));
            _nudgeText.Measure(new Windows.Foundation.Size(Math.Max(1, width / scale - 32), double.PositiveInfinity));
            var height = (int)Math.Ceiling(Math.Max(64, _nudgeText.DesiredSize.Height + 28) * scale);
            Place(_nudge, rect.Left + inset, rect.Top + inset, width, height);
        }
        else _nudge.AppWindow.Hide();
        var temporary = allowUntil is { } end && end > DateTimeOffset.Now;
        if (temporary || quotaSeconds is > 0)
        {
            var seconds = temporary ? (int)Math.Ceiling((allowUntil!.Value - DateTimeOffset.Now).TotalSeconds) : (int)Math.Ceiling(quotaSeconds!.Value);
            _quotaText.Text = temporary ? $"Equora · 临时允许 {seconds / 60:00}:{seconds % 60:00}"
                : seconds >= 600 ? $"Equora · 今日可用 {(int)Math.Ceiling(seconds / 60.0)} 分钟"
                : $"Equora · 今日可用 {seconds / 60:00}:{seconds % 60:00}";
            _quotaText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var wanted = (int)Math.Ceiling(Math.Max(320, _quotaText.DesiredSize.Width + 48) * scale);
            var width = Math.Min(wanted, Math.Max(1, rect.Width - 2 * inset));
            _quotaText.Measure(new Windows.Foundation.Size(Math.Max(1, width / scale - 32), double.PositiveInfinity));
            var height = (int)Math.Ceiling(Math.Max(64, _quotaText.DesiredSize.Height + 28) * scale);
            Place(_quota, rect.Right - width - inset, rect.Top + inset, width, height);
        }
        else _quota.AppWindow.Hide();
    }

    public void Hide()
    {
        _block.AppWindow.Hide(); _nudge.AppWindow.Hide(); _quota.AppWindow.Hide();
        TargetWindow = IntPtr.Zero;
        IsBlocking = false;
    }
    public void Dispose() { Hide(); _block.Close(); _nudge.Close(); _quota.Close(); }
}
