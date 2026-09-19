using Equora.App.NativeInterop;
using Equora.App.ViewModels;
using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;

namespace Equora.App.Controls;

/// <summary>
/// 自定义周视图:Canvas 定位 + 按窗口重建(数据量为一周级,全部实例化开销可忽略;
/// 大规模虚拟化在 P16 性能阶段处理)。支持:拖拽创建、块移动/底部缩放(15 分钟吸附)、
/// 当前时间线、工作时间底纹、冲突描红、待办拖入。
/// </summary>
public sealed partial class WeekView : UserControl
{
    private const double HourHeight = 56.0;
    private static readonly TimeSpan WorkStart = new(9, 0, 0);
    private static readonly TimeSpan WorkEnd = new(18, 0, 0);

    private readonly Canvas[] _days;
    private readonly DispatcherTimer _nowTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    private CalendarViewModel ViewModel => AppServices.Calendar;

    // 指针交互状态。
    private enum DragKind { None, CreateNew, Move, Resize }
    private DragKind _drag = DragKind.None;
    private Canvas? _activeCanvas;
    private DateTimeOffset _activeDayStart;
    private DateTimeOffset _dragAnchorTime;
    private TimeSpan _dragOriginalDuration;
    private Border? _preview;
    private Border? _hitBlock;

    public WeekView()
    {
        InitializeComponent();
        _days = new[] { Day0, Day1, Day2, Day3, Day4, Day5, Day6 };

        BuildAxis();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CalendarViewModel.Items) ||
                e.PropertyName == nameof(CalendarViewModel.StatusText))
            {
                Rebuild();
            }
        };
        Loaded += (_, _) =>
        {
            Rebuild();
            UpdateNowLine();
            _nowTimer.Tick += (_, _) => UpdateNowLine();
            _nowTimer.Start();
        };
        Unloaded += (_, _) => _nowTimer.Stop();
    }

    // ---- 构建 ----

    private void BuildAxis()
    {
        HourAxis.Children.Clear();
        for (int h = 0; h < 24; ++h)
        {
            var text = new TextBlock
            {
                Text = $"{h:00}:00",
                FontSize = 11,
                Opacity = 0.55,
                Margin = new Thickness(4, h * HourHeight - 8, 0, 0),
            };
            HourAxis.Children.Add(text);
        }
        HourAxis.Height = 24 * HourHeight;
        foreach (var day in _days)
        {
            day.Height = 24 * HourHeight;
        }
    }

    private void Rebuild()
    {
        for (int i = 0; i < 7; ++i)
        {
            _days[i].Children.Clear();
        }

        var weekStart = ViewModel.WeekStart;

        // 星期标题。
        HeaderGrid.Children.Clear();
        for (int i = 0; i < 7; ++i)
        {
            var day = weekStart.AddDays(i);
            var header = new TextBlock
            {
                Text = $"周{"一二三四五六日"[i]} {day.Month:00}-{day.Day:00}",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12,
                Padding = new Thickness(0, 6, 0, 6),
            };
            Grid.SetColumn(header, i + 1);
            HeaderGrid.Children.Add(header);
        }

        // 工作时间底纹(周一~周五)。
        for (int i = 0; i < 7; ++i)
        {
            var day = weekStart.AddDays(i);
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (isWeekend) continue;

            var shade = new Rectangle
            {
                Height = (WorkEnd - WorkStart).TotalHours * HourHeight,
                Width = double.NaN,
                Fill = new SolidColorBrush(Color.FromArgb(10, 0x20, 0x60, 0x20)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(shade, 0);
            Canvas.SetTop(shade, WorkStart.TotalHours * HourHeight);
            _days[i].Children.Add(shade);
        }

        // 事件块:按天分组,同天重叠链横向均分。
        var byDay = new List<CalendarItem>[7];
        for (int i = 0; i < 7; ++i) byDay[i] = new List<CalendarItem>();
        foreach (var item in ViewModel.Items)
        {
            var local = item.Span.Start.ToLocalTime();
            var day = ViewModel.WeekStart.Date;
            var offset = (local.Date - day).Days;
            if (offset < 0 || offset > 6) continue;
            byDay[offset].Add(item);
        }

        for (int i = 0; i < 7; ++i)
        {
            var dayStart = new DateTimeOffset(ViewModel.WeekStart.Date.AddDays(i),
                TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now));
            var canvas = _days[i];
            var groups = OverlapGroups(byDay[i]);
            foreach (var group in groups)
            {
                for (int slot = 0; slot < group.Count; ++slot)
                {
                    canvas.Children.Add(MakeBlock(group[slot], group.Count, slot, dayStart));
                }
            }
        }

        UpdateNowLine();
    }

    private static List<List<CalendarItem>> OverlapGroups(List<CalendarItem> items)
    {
        var sorted = items.OrderBy(x => x.Span.Start).ToList();
        var groups = new List<List<CalendarItem>>();
        List<CalendarItem>? current = null;
        DateTimeOffset currentEnd = DateTimeOffset.MinValue;

        foreach (var item in sorted)
        {
            if (current is null || item.Span.Start >= currentEnd)
            {
                current = new List<CalendarItem> { item };
                groups.Add(current);
                currentEnd = item.Span.End;
            }
            else
            {
                current.Add(item);
                currentEnd = currentEnd > item.Span.End ? currentEnd : item.Span.End;
            }
        }
        return groups;
    }

    private Border MakeBlock(CalendarItem item, int overlapCount, int slot,
        DateTimeOffset dayStart)
    {
        var block = new Border
        {
            Tag = item,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(item.IsConflict
                ? Color.FromArgb(255, 0xC0, 0x30, 0x30)
                : Color.FromArgb(90, 0x80, 0x80, 0x80)),
            Background = new SolidColorBrush(BlockColor(item)),
            Padding = new Thickness(4, 2, 4, 2),
            CanDrag = false,
        };
        var text = new TextBlock
        {
            Text = $"{item.DisplayTitle}\n{item.Span.Start.ToLocalTime():HH:mm}-{item.Span.End.ToLocalTime():HH:mm}",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        block.Child = text;

        var top = SlotMath.YOffsetWithinDay(item.Span.Start.ToLocalTime(), dayStart, HourHeight);
        var height = Math.Max(18, (item.Span.End - item.Span.Start).TotalHours * HourHeight - 2);

        var width = (canvasWidth <= 0 ? 120 : canvasWidth) / overlapCount;
        Canvas.SetTop(block, top);
        Canvas.SetLeft(block, slot * width + 1);
        block.Width = Math.Max(24, width - 2);
        block.Height = height;

        block.PointerPressed += OnBlockPointerPressed;
        return block;
    }

    private static Color BlockColor(CalendarItem item) => item.Span.SourceType switch
    {
        "block" => Color.FromArgb(50, 0x33, 0x66, 0xCC),   // 任务块:蓝
        "event" => Color.FromArgb(50, 0x2E, 0x8B, 0x57),   // 日程:绿
        "recurring" => Color.FromArgb(50, 0x7A, 0x3C, 0xB0), // 重复:紫
        _ => Color.FromArgb(50, 0x80, 0x80, 0x80),
    };

    private double canvasWidth;

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        canvasWidth = e.NewSize.Width > 48 ? (e.NewSize.Width - 48) / 7 : 120;
        Rebuild();
    }

    private void UpdateNowLine()
    {
        var now = DateTimeOffset.Now;
        var dayStart = new DateTimeOffset(ViewModel.WeekStart.Date,
            TimeZoneInfo.Local.GetUtcOffset(now));
        var dayOffset = (now.Date - dayStart.Date).Days;
        if (dayOffset < 0 || dayOffset > 6)
        {
            NowLine.Visibility = Visibility.Collapsed;
            return;
        }
        NowLine.Visibility = Visibility.Visible;
        var y = SlotMath.YOffsetWithinDay(now, dayStart, HourHeight);
        Canvas.SetTop(NowLine, y);
        NowLine.Width = NowLineLayer.ActualWidth > 0 ? NowLineLayer.ActualWidth : 600;
    }

    // ---- 指针交互:创建 / 移动 / 缩放 ----

    private void OnBlockPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not CalendarItem item) return;
        if (item.Span.SourceType != "block") return; // 重复实例编辑走详情/例外入口
        var blockId = CalendarViewModel.ExtractBlockId(item.Span.SourceId);
        if (blockId is null) return;

        _activeCanvas = border.Parent as Canvas;
        _activeDayStart = DayStartOf(_activeCanvas);
        var position = e.GetCurrentPoint(_activeCanvas).Position;
        var top = Canvas.GetTop(border);

        // 底部 12px 命中缩放,否则整体移动。
        var isResize = position.Y > top + border.ActualHeight - 14;
        _drag = isResize ? DragKind.Resize : DragKind.Move;
        _dragAnchorTime = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, _activeDayStart,
            HourHeight));
        _dragOriginalDuration = item.Span.End - item.Span.Start;
        _hitBlock = border;
        blockIdUnderDrag = blockId;

        border.CapturePointer(e.Pointer);
        StartPreview(_dragAnchorTime,
            isResize ? _dragAnchorTime - _dragAnchorTime : _dragOriginalDuration);
        e.Handled = true;
    }

    private string? blockIdUnderDrag;

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Canvas canvas) return;
        _activeCanvas = canvas;
        _activeDayStart = DayStartOf(canvas);
        var position = e.GetCurrentPoint(canvas).Position;
        _drag = DragKind.CreateNew;
        _dragAnchorTime = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, _activeDayStart,
            HourHeight));
        canvas.CapturePointer(e.Pointer);
        StartPreview(_dragAnchorTime, TimeSpan.FromMinutes(SlotMath.SnapMinutes));
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None || _activeCanvas is null) return;
        var position = e.GetCurrentPoint(_activeCanvas).Position;
        var time = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, _activeDayStart, HourHeight));

        switch (_drag)
        {
            case DragKind.CreateNew:
                UpdatePreview(_dragAnchorTime, time > _dragAnchorTime
                    ? time - _dragAnchorTime
                    : TimeSpan.FromMinutes(SlotMath.SnapMinutes));
                break;
            case DragKind.Move:
                UpdatePreview(time, _dragOriginalDuration);
                break;
            case DragKind.Resize:
                var end = time > _dragAnchorTime ? time : _dragAnchorTime.AddMinutes(15);
                UpdatePreview(_dragAnchorTime, end - _dragAnchorTime);
                break;
        }
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None || _activeCanvas is null) return;
        var position = e.GetCurrentPoint(_activeCanvas).Position;
        var time = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, _activeDayStart, HourHeight));

        switch (_drag)
        {
            case DragKind.CreateNew:
                var end = time > _dragAnchorTime ? time : _dragAnchorTime.AddHours(1);
                ViewModel.CreateBlockAt(_dragAnchorTime, (int)(end - _dragAnchorTime).TotalMinutes,
                    taskId: null);
                break;
            case DragKind.Move when blockIdUnderDrag is not null:
                ViewModel.MoveBlock(blockIdUnderDrag, time);
                break;
            case DragKind.Resize when blockIdUnderDrag is not null:
                var resizeEnd = time > _dragAnchorTime ? time : _dragAnchorTime.AddMinutes(15);
                ViewModel.ResizeBlock(blockIdUnderDrag, resizeEnd);
                break;
        }

        ClearPreview();
        _drag = DragKind.None;
        blockIdUnderDrag = null;
        _hitBlock = null;
        _activeCanvas = null;
    }

    private DateTimeOffset DayStartOf(Canvas? canvas)
    {
        var index = canvas is null ? 0 : Array.IndexOf(_days, canvas);
        if (index < 0) index = 0;
        return new DateTimeOffset(ViewModel.WeekStart.Date.AddDays(index),
            TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now));
    }

    // ---- 拖拽预览 ----

    private void StartPreview(DateTimeOffset start, TimeSpan duration)
    {
        ClearPreview();
        _preview = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0x33, 0x66, 0xCC)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0x33, 0x66, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
        };
        if (_activeCanvas is not null) _activeCanvas.Children.Add(_preview);
        UpdatePreview(start, duration);
    }

    private void UpdatePreview(DateTimeOffset start, TimeSpan duration)
    {
        if (_preview is null || _activeCanvas is null) return;
        Canvas.SetTop(_preview, SlotMath.YOffsetWithinDay(start, _activeDayStart, HourHeight));
        _preview.Height = Math.Max(14, duration.TotalHours * HourHeight - 2);
        _preview.Width = Math.Max(40, canvasWidth - 6);
        Canvas.SetLeft(_preview, 2);
    }

    private void ClearPreview()
    {
        if (_preview is not null && _preview.Parent is Panel panel)
        {
            panel.Children.Remove(_preview);
        }
        _preview = null;
    }

    // ---- 待办拖入 ----

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (sender is not Canvas canvas) return;
        if (!e.DataView.Properties.TryGetValue("task", out var raw) || raw is not TaskDto task)
        {
            return;
        }
        var position = e.GetPosition(canvas);
        var dayStart = DayStartOf(canvas);
        var start = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, dayStart, HourHeight));
        var block = ViewModel.CreateBlockAt(start, 60, task.Id);
        ViewModel.StatusText = $"已安排「{task.Title}」{start:MM-dd HH:mm}(60 分钟)";
    }
}
