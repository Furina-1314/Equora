using System.Diagnostics;
using System.Runtime.InteropServices;
using Equora.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Equora.App;

// These windows belong to Equora but attach to windows of restricted applications.
// The block page covers only the target's client area so the title bar stays usable;
// overlays ride just above their target in z-order instead of going topmost.
// WinEvent hooks keep them glued to the target as it moves, shows, restores or closes.
internal sealed class ForegroundOverlays : IDisposable
{
    public sealed record TargetState(string Process, string? BlockReason, bool AllowUsed,
        DateTimeOffset? AllowUntil, double? QuotaSeconds);

    private sealed class Pair
    {
        public required Window Block;
        public required Window Countdown;
        public required TextBlock BlockName;
        public required TextBlock BlockReason;
        public required TextBlock AllowFeedback;
        public required TextBlock CountdownText;
        public required Button Allow;
        public IntPtr Target;
        public string Process = "";
        public bool Blocking;
        public bool CountdownShown;
        public bool Active;
    }

    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WsPopup = unchecked((int)0x80000000);
    private const long WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExTransparent = 0x20;
    private const uint SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020, SwpShowWindow = 0x0040;
    private static readonly IntPtr Topmost = new(-1);
    private static readonly IntPtr NotTopmost = new(-2);
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016, EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001, EventObjectShow = 0x8002, EventObjectHide = 0x8003, EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0;
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect buttons, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private const int SmCyCaption = 4, SmCxPaddingBorder = 92;

    private readonly Dictionary<IntPtr, Pair> _pairs = new();
    private readonly List<Pair> _pool = new();
    private readonly Window _nudge = new();
    private readonly TextBlock _nudgeText = new();
    private readonly Action<string> _grant;
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private readonly WinEventProc _hookProc; // 防 GC
    private readonly IntPtr[] _hooks = new IntPtr[3];
    private readonly Stopwatch _resyncGate = Stopwatch.StartNew();
    private bool _resyncQueued;
    public event Action? SyncRequested;
    public bool IsBlocking { get; private set; }

    public ForegroundOverlays(Action<string> grant)
    {
        _grant = grant;
        _nudge.ExtendsContentIntoTitleBar = true;
        _nudge.Content = Notice(_nudgeText);
        Configure(_nudge, true);
        // 窗口池在启动阶段(主窗口激活前)预建:此后在计时器回调里创建的 WinUI 窗口
        // 不会再加载 XAML 内容(呈现为空白),必须复用启动时创建好的窗口。
        for (var i = 0; i < 3; i++) _pool.Add(CreatePair());
        _hookProc = OnWinEvent;
        _hooks[0] = SetWinEventHook(EventObjectDestroy, EventObjectLocationChange, IntPtr.Zero, _hookProc, 0, 0, WinEventOutOfContext);
        _hooks[1] = SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeEnd, IntPtr.Zero, _hookProc, 0, 0, WinEventOutOfContext);
        _hooks[2] = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _hookProc, 0, 0, WinEventOutOfContext);
    }

    private static IntPtr Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private static TextBlock Text(string value, double size, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = muted ? Brush(90, 90, 90) : Brush(32, 32, 32)
    };

    // 与扩展封锁页(blocked.html)同一副方角锁形:圆弧锁梁 + 方形锁体 + 圆点锁孔,描边不填充。
    private static Microsoft.UI.Xaml.Shapes.Path SquareLock()
    {
        var shackle = new PathFigure { StartPoint = new Windows.Foundation.Point(8, 14) };
        shackle.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(8, 9) });
        shackle.Segments.Add(new ArcSegment
        {
            Point = new Windows.Foundation.Point(24, 9), Size = new Windows.Foundation.Size(8, 8),
            SweepDirection = SweepDirection.Clockwise
        });
        shackle.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(24, 14) });
        var body = new PathFigure { StartPoint = new Windows.Foundation.Point(5, 14), IsClosed = true };
        body.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(27, 14) });
        body.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(27, 30) });
        body.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(5, 30) });
        var keyhole = new PathFigure { StartPoint = new Windows.Foundation.Point(16, 20) };
        keyhole.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(16, 25) });
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = new PathGeometry { Figures = { shackle, body, keyhole } },
            Stroke = Brush(32, 32, 32), StrokeThickness = 1.5, Fill = null,
            Width = 40, Height = 40, Stretch = Stretch.None,
            RenderTransform = new ScaleTransform { ScaleX = 1.25, ScaleY = 1.25 },
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private Pair CreatePair()
    {
        var blockName = new TextBlock { FontSize = 20, Foreground = Brush(32, 32, 32), TextWrapping = TextWrapping.Wrap };
        var blockReason = new TextBlock { FontSize = 14, Foreground = Brush(90, 90, 90), TextWrapping = TextWrapping.Wrap };
        var allowFeedback = new TextBlock { FontSize = 14, Foreground = Brush(90, 90, 90), TextWrapping = TextWrapping.Wrap };
        Button allow = new() { Content = "临时允许 5 分钟", Template = FlatTemplate() };
        Button close = new() { Content = "关闭窗口", Template = FlatTemplate() };
        var block = new Window { ExtendsContentIntoTitleBar = true };
        var pair = new Pair
        {
            Block = block, Countdown = new Window { ExtendsContentIntoTitleBar = true },
            BlockName = blockName, BlockReason = blockReason, AllowFeedback = allowFeedback,
            CountdownText = new TextBlock(), Allow = allow
        };
        close.Click += (_, _) => ForegroundAccess.Close(pair.Target);
        allow.Click += (_, _) => _grant(pair.Process);
        block.Content = BuildBlock(close, allow, blockName, blockReason, allowFeedback);
        pair.Countdown.Content = Notice(pair.CountdownText);
        Configure(block, false);
        Configure(pair.Countdown, true);
        // 从未激活过的 WinUI 窗口不会加载 XAML 内容(呈现为空白)。离屏激活一次强制完成
        // 内容加载,再把焦点还给原窗口;之后的所有摆放都不再依赖激活。
        var focus = GetForegroundWindow();
        ForceContentLoaded(block);
        ForceContentLoaded(pair.Countdown);
        if (focus != IntPtr.Zero && !Owns(focus)) SetForegroundWindow(focus);
        return pair;
    }

    private static void ForceContentLoaded(Window window)
    {
        SetWindowPos(Handle(window), IntPtr.Zero, -4000, -4000, 4, 4, SwpNoActivate);
        window.Activate();
        window.AppWindow.Hide();
    }

    private ControlTemplate? _flatTemplate;
    // 与扩展封锁页一致的纯色按钮:无悬停/按压变色的扁平模板,禁用态由调用方设置透明度。
    private ControlTemplate FlatTemplate() => _flatTemplate ??= (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>" +
        "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='0'>" +
        "<ContentPresenter Content='{TemplateBinding Content}' Foreground='{TemplateBinding Foreground}' Padding='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
        "</Border></ControlTemplate>");

    private static UIElement BuildBlock(Button close, Button allow, TextBlock name, TextBlock reason, TextBlock feedback)
    {
        var root = new Grid { Background = Brush(243, 243, 243), Padding = new Thickness(24) };
        var card = new Border { MaxWidth = 560, BorderBrush = Brush(195, 47, 55), BorderThickness = new Thickness(0, 4, 0, 0), Background = Brush(255, 255, 255), Padding = new Thickness(48, 44, 48, 20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(SquareLock());
        stack.Children.Add(Text("此应用暂时不可使用", 30));
        stack.Children.Add(name);
        stack.Children.Add(Text("已触发你在 Equora 中设置的时段、专注或每日使用时长限制。", 15));
        stack.Children.Add(reason);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        close.Background = Brush(195, 47, 55); close.Foreground = Brush(255, 255, 255);
        close.BorderBrush = Brush(195, 47, 55); close.BorderThickness = new Thickness(1);
        close.CornerRadius = new CornerRadius(0); close.Padding = new Thickness(20, 12, 20, 12);
        allow.Background = Brush(229, 229, 229); allow.Foreground = Brush(32, 32, 32);
        allow.BorderBrush = Brush(136, 136, 136); allow.BorderThickness = new Thickness(1);
        allow.CornerRadius = new CornerRadius(0); allow.Padding = new Thickness(20, 12, 20, 12);
        buttons.Children.Add(close); buttons.Children.Add(allow);
        stack.Children.Add(buttons);
        stack.Children.Add(feedback);
        stack.Children.Add(Text("每个应用仅可临时允许一次，重启不会重置。允许后窗口右上角显示倒计时，到期后恢复限制。可在桌面端“使用限制”中调整规则。", 12, true));
        var brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.png")), Width = 28, Height = 28 });
        brand.Children.Add(new TextBlock { Text = "EQUORA", FontFamily = new FontFamily("Segoe UI Variable"), FontSize = 18, CharacterSpacing = 120, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(32, 32, 32) });
        stack.Children.Add(brand);
        card.Child = stack; root.Children.Add(card);
        return root;
    }

    private static UIElement Notice(TextBlock label)
    {
        var root = new Grid { Background = Brush(32, 32, 32), Padding = new Thickness(16, 10, 16, 10) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new Border { Background = Brush(195, 47, 55) });
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
        SetWindowPos(hwnd, NotTopmost, 0, 0, 1, 1, SwpNoActivate | SwpFrameChanged);
        window.AppWindow.Hide();
    }

    // 插入到目标窗口正上方;传 IntPtr.Zero 即 HWND_TOP。
    private static void Place(Window window, IntPtr target, int x, int y, int width, int height)
    {
        SetWindowPos(Handle(window), Topmost, x, y,
            Math.Max(width, 1), Math.Max(height, 1), SwpNoActivate | SwpShowWindow);
        window.AppWindow.Show(false);
    }

    // 露出整个标题栏:现代应用把最小化/还原/关闭按钮画在客户区内,用 DWM 的标题栏按钮
    // 边界精确求出应下移的高度;经典应用的标题栏本就在客户区之外,结果为 0,不会多露。
    private static int TitleStripHeight(IntPtr target, ForegroundAccess.WindowBounds client)
    {
        if (DwmGetWindowAttribute(target, 5 /* DWMWA_CAPTION_BUTTON_BOUNDS */, out var buttons, 16) == 0)
        {
            var window = ForegroundAccess.Bounds(target);
            if (window is { } w)
            {
                var strip = buttons.Bottom + 2 - (client.Top - w.Top);
                return Math.Max(0, Math.Min(client.Height / 2, strip));
            }
        }
        var dpi = GetDpiForWindow(target);
        var pad = Math.Max(1, GetSystemMetricsForDpi(SmCxPaddingBorder, dpi));
        return Math.Min(client.Height / 2, GetSystemMetricsForDpi(SmCyCaption, dpi) + 3 * pad);
    }

    /// <summary>把遮罩同步到当前所有命中窗口;倒计时提示与封锁页一样常驻,不依赖前台焦点。</summary>
    public void Sync(IReadOnlyList<(IntPtr Window, TargetState State)> targets, IntPtr nudgeHost, string? nudge)
    {
        foreach (var pair in _pairs.Values) pair.Active = false;
        var blocking = false;
        foreach (var (hwnd, state) in targets)
        {
            if (!_pairs.TryGetValue(hwnd, out var pair))
            {
                pair = _pool.Count > 0 ? _pool[^1] : CreatePair();
                if (_pool.Count > 0) _pool.RemoveAt(_pool.Count - 1);
                pair.Target = hwnd;
                _pairs[hwnd] = pair;
            }
            pair.Target = hwnd; pair.Process = state.Process; pair.Active = true;
            pair.BlockName.Text = state.Process;
            pair.BlockReason.Text = state.BlockReason ?? "";
            pair.Allow.IsEnabled = !state.AllowUsed;
            pair.Allow.Opacity = state.AllowUsed ? 0.55 : 1.0;
            pair.AllowFeedback.Text = state.AllowUsed && state.BlockReason is not null ? "临时允许机会已用完。" : "";
            var bounds = ForegroundAccess.ClientBounds(hwnd);
            if (state.BlockReason is not null)
            {
                var wasBlocking = pair.Blocking;
                blocking = true;
                pair.Blocking = true;
                pair.CountdownShown = false;
                if (bounds is not null)
                {
                    var rect = bounds.Value;
                    PlaceBlock(pair, hwnd, rect);
                    pair.Countdown.AppWindow.Hide();
                    // 前台应用刚被封锁时让封锁页接管焦点;后台窗口被封锁时不抢夺用户当前焦点。
                    if (!wasBlocking && ForegroundAccess.Current().Window == hwnd) pair.Block.Activate();
                }
                else
                {
                    // 最小化等不可见状态:收起遮罩;恢复显示后由窗口事件立即重新覆盖。
                    pair.Block.AppWindow.Hide(); pair.Countdown.AppWindow.Hide();
                }
                continue;
            }
            pair.Blocking = false;
            pair.Block.AppWindow.Hide();
            if (bounds is null) { pair.CountdownShown = false; pair.Countdown.AppWindow.Hide(); continue; }
            var rect2 = bounds.Value;
            var scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
            var seconds = state.AllowUntil is { } end && end > DateTimeOffset.Now
                ? (int)Math.Ceiling((end - DateTimeOffset.Now).TotalSeconds)
                : state.QuotaSeconds is > 0 ? (int)Math.Ceiling(state.QuotaSeconds.Value) : 0;
            if (seconds > 0)
            {
                pair.CountdownText.Text = state.AllowUntil is { } e && e > DateTimeOffset.Now
                    ? $"Equora · 临时允许 {seconds / 60:00}:{seconds % 60:00}"
                    : seconds >= 600 ? $"Equora · 今日可用 {(int)Math.Ceiling(seconds / 60.0)} 分钟"
                    : $"Equora · 今日可用 {seconds / 60:00}:{seconds % 60:00}";
                pair.CountdownShown = true;
                PlaceCountdown(pair, rect2, scale);
            }
            else { pair.CountdownShown = false; pair.Countdown.AppWindow.Hide(); }
        }
        IsBlocking = blocking;
        // 目标已消失或规则不再命中的空闲对:恢复被隐藏的窗口后归还窗口池。
        foreach (var idle in _pairs.Where(kv => !kv.Value.Active).ToList())
        {
            var pair = idle.Value;
            pair.Block.AppWindow.Hide(); pair.Countdown.AppWindow.Hide();
            pair.Blocking = false; pair.CountdownShown = false;
            _pairs.Remove(idle.Key);
            if (_pool.Count < 3) _pool.Add(pair);
            else { pair.Block.Close(); pair.Countdown.Close(); }
        }
        PlaceNudge(nudgeHost, nudge);
    }

    // 封锁页整体盖住“客户区减去顶部标题栏”的区域:标题栏(含最小化/还原/关闭)完全露出。
    private void PlaceBlock(Pair pair, IntPtr target, ForegroundAccess.WindowBounds rect)
    {
        var strip = TitleStripHeight(target, rect);
        Place(pair.Block, target, rect.Left, rect.Top + strip, rect.Width, rect.Height - strip);
    }

    // 倒计时提示框:钉在目标客户区右上角(避开标题栏按钮豁口),尺寸随文字与可用宽度自适应。
    private void PlaceCountdown(Pair pair, ForegroundAccess.WindowBounds rect, double scale)
    {
        var inset = (int)Math.Ceiling(16 * scale);
        var below = TitleStripHeight(pair.Target, rect) + inset;
        pair.CountdownText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var wanted = (int)Math.Ceiling(Math.Max(320, pair.CountdownText.DesiredSize.Width + 48) * scale);
        var width = Math.Min(wanted, Math.Max(1, rect.Width - 2 * inset));
        pair.CountdownText.Measure(new Windows.Foundation.Size(Math.Max(1, width / scale - 32), double.PositiveInfinity));
        var height = (int)Math.Ceiling(Math.Max(64, pair.CountdownText.DesiredSize.Height + 28) * scale);
        Place(pair.Countdown, pair.Target, rect.Right - width - inset, rect.Top + below, width, height);
    }

    private IntPtr _nudgeHost;
    private string? _nudgeValue;
    // 分心提醒框:钉在宿主窗口客户区顶部正中,拖动窗口时由钩子逐帧跟随,视觉上与窗口一体。
    private void PlaceNudge(IntPtr host, string? text)
    {
        _nudgeValue = text;
        if (text is null || !ForegroundAccess.IsAlive(host) || ForegroundAccess.IsMinimized(host))
        { _nudge.AppWindow.Hide(); _nudgeHost = IntPtr.Zero; return; }
        var bounds = ForegroundAccess.ClientBounds(host);
        if (bounds is null) { _nudge.AppWindow.Hide(); _nudgeHost = IntPtr.Zero; return; }
        var rect = bounds.Value;
        var scale = Math.Max(1, GetDpiForWindow(host) / 96.0);
        var inset = (int)Math.Ceiling(16 * scale);
        _nudgeText.Text = "Equora · " + text;
        var width = Math.Min((int)Math.Ceiling(560 * scale), Math.Max(1, rect.Width - 2 * inset));
        _nudgeText.Measure(new Windows.Foundation.Size(Math.Max(1, width / scale - 32), double.PositiveInfinity));
        var height = (int)Math.Ceiling(Math.Max(64, _nudgeText.DesiredSize.Height + 28) * scale);
        var x = rect.Left + Math.Max(inset, (rect.Width - width) / 2);
        Place(_nudge, host, x, rect.Top + inset, width, height);
        _nudgeHost = host;
    }

    public bool Owns(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && (_pairs.Values.Any(p => hwnd == Handle(p.Block) || hwnd == Handle(p.Countdown)) || hwnd == Handle(_nudge));

    /// <summary>遮罩窗口 → 它跟随的目标窗口(前台是自家遮罩时,把逻辑映射回目标应用)。</summary>
    public IntPtr HostOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        foreach (var pair in _pairs.Values)
            if (hwnd == Handle(pair.Block) || hwnd == Handle(pair.Countdown)) return pair.Target;
        return IntPtr.Zero;
    }
    public string? ProcessOf(IntPtr target) => target != IntPtr.Zero && _pairs.TryGetValue(target, out var pair) ? pair.Process : null;
    /// <summary>前台窗口是否为封锁页(封锁页获得焦点时不给目标应用累计使用时长)。</summary>
    public bool IsBlockWindow(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && _pairs.Values.Any(p => p.Blocking && hwnd == Handle(p.Block));

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 /* OBJID_WINDOW */ || hwnd == IntPtr.Zero) return;
        // 新窗口出现或前台切换:可能有需要封锁的新目标,请求运行时重算(限流,避免事件风暴)。
        if (eventType is EventSystemForeground or EventObjectShow)
        {
            if (_resyncGate.ElapsedMilliseconds < 150 || _resyncQueued) return;
            _resyncGate.Restart();
            _resyncQueued = true;
            _queue.TryEnqueue(() => { _resyncQueued = false; SyncRequested?.Invoke(); });
            return;
        }
        // 已跟踪目标的位置/显隐变化:立刻重摆或收起对应遮罩。
        if (eventType is not (EventObjectLocationChange or EventObjectDestroy or EventObjectHide
            or EventSystemMinimizeStart or EventSystemMinimizeEnd)) return;
        var target = hwnd;
        if (hwnd == _nudgeHost)
        {
            // 提醒框宿主移动:逐帧重摆,保持钉在窗口顶部正中。
            _queue.TryEnqueue(() => PlaceNudge(_nudgeHost, _nudgeValue));
            if (!_pairs.ContainsKey(hwnd)) return;
        }
        else if (!_pairs.ContainsKey(hwnd)) return;
        _queue.TryEnqueue(() => Reposition(target));
    }

    private void Reposition(IntPtr target)
    {
        if (!_pairs.TryGetValue(target, out var pair)) return;
        if (!ForegroundAccess.IsAlive(target) || ForegroundAccess.IsMinimized(target))
        {
            pair.Block.AppWindow.Hide(); pair.Countdown.AppWindow.Hide();
            return;
        }
        var bounds = ForegroundAccess.ClientBounds(target);
        if (bounds is null)
        {
            pair.Block.AppWindow.Hide(); pair.Countdown.AppWindow.Hide();
            return;
        }
        var rect = bounds.Value;
        if (pair.Blocking) PlaceBlock(pair, target, rect);
        else
        {
            pair.Block.AppWindow.Hide();
            if (pair.CountdownShown) PlaceCountdown(pair, rect, Math.Max(1, GetDpiForWindow(target) / 96.0));
        }
    }

    public void Hide()
    {
        foreach (var pair in _pairs.Values)
        { pair.Block.AppWindow.Hide(); pair.Countdown.AppWindow.Hide(); pair.Blocking = false; }
        _nudge.AppWindow.Hide();
        _nudgeHost = IntPtr.Zero;
        IsBlocking = false;
    }
    public void Dispose()
    {
        foreach (var hook in _hooks) if (hook != IntPtr.Zero) UnhookWinEvent(hook);
        foreach (var pair in _pairs.Values) { pair.Block.Close(); pair.Countdown.Close(); }
        foreach (var pair in _pool) { pair.Block.Close(); pair.Countdown.Close(); }
        _pairs.Clear();
        _pool.Clear();
        _nudge.Close();
    }
}
