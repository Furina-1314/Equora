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
    private TimeSpan _grabOffset;
    private Border? _preview;
    private Border? _hitBlock;

    public WeekView()
    {
        InitializeComponent();
        _days = new[] { Day0, Day1, Day2, Day3, Day4, Day5, Day6 };

        BuildAxis();
        _nowTimer.Tick += (_, _) => UpdateNowLine();
        Loaded += (_, _) =>
        {
            ViewModel.PropertyChanged += OnViewModelChanged;
            ViewModel.Items.CollectionChanged += OnItemsChanged;
            Rebuild();
            _nowTimer.Start();
        };
        Unloaded += (_, _) => { _nowTimer.Stop(); ViewModel.PropertyChanged -= OnViewModelChanged; ViewModel.Items.CollectionChanged -= OnItemsChanged; };
    }
    private bool _rebuildPending;
    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildPending) return;
        _rebuildPending = true;
        DispatcherQueue.TryEnqueue(() => { _rebuildPending = false; Rebuild(); });
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalendarViewModel.StatusText) or nameof(CalendarViewModel.ViewModeIndex)) Rebuild();
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
                Margin = new Thickness(0),
            };
            Canvas.SetLeft(text, 10);
            Canvas.SetTop(text, Math.Max(2, h * HourHeight - 8));
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

        var weekStart = ViewModel.WindowStart;
        var dayCount = ViewModel.ViewModeIndex == 1 ? 1 : 7;
        for (var i = 0; i < 7; i++)
        {
            _days[i].Visibility = i < dayCount ? Visibility.Visible : Visibility.Collapsed;
            HeaderGrid.ColumnDefinitions[i + 1].Width = i < dayCount ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            BodyGrid.ColumnDefinitions[i + 1].Width = HeaderGrid.ColumnDefinitions[i + 1].Width;
        }
        canvasWidth = Math.Max(1, (BodyGrid.ActualWidth - 64) / dayCount);

        // 星期标题。
        HeaderGrid.Children.Clear();
        for (int i = 0; i < dayCount; ++i)
        {
            var day = weekStart.AddDays(i);
            var header = new TextBlock
            {
                Text = $"周{"日一二三四五六"[(int)day.DayOfWeek]} {day.Month:00}-{day.Day:00}",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12,
                Padding = new Thickness(0, 6, 0, 6),
            };
            Grid.SetColumn(header, i + 1);
            HeaderGrid.Children.Add(header);
        }

        // 工作时间底纹(周一~周五)。
        for (int i = 0; i < dayCount; ++i)
        {
            var day = weekStart.AddDays(i);
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (isWeekend) continue;

            var shade = new Rectangle
            {
                Height = (WorkEnd - WorkStart).TotalHours * HourHeight,
                Width = canvasWidth,
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
            for (var offset = 0; offset < dayCount; offset++)
            {
                var day = ViewModel.WindowStart.AddDays(offset);
                if (item.Span.Start < day.AddDays(1) && item.Span.End > day) byDay[offset].Add(item);
            }
        }

        for (int i = 0; i < 7; ++i)
        {
            var dayStart = new DateTimeOffset(ViewModel.WindowStart.Date.AddDays(i),
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
            CornerRadius = new CornerRadius(0),
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

        var visibleStart = item.Span.Start < dayStart ? dayStart : item.Span.Start;
        var visibleEnd = item.Span.End > dayStart.AddDays(1) ? dayStart.AddDays(1) : item.Span.End;
        var top = SlotMath.YOffsetWithinDay(visibleStart.ToLocalTime(), dayStart, HourHeight);
        var height = Math.Max(18, (visibleEnd - visibleStart).TotalHours * HourHeight - 2);

        var width = (canvasWidth <= 0 ? 120 : canvasWidth) / overlapCount;
        Canvas.SetTop(block, top);
        Canvas.SetLeft(block, slot * width + 1);
        block.Width = Math.Max(24, width - 2);
        block.Height = height;

        block.PointerPressed += OnBlockPointerPressed;
        if (item.Span.SourceType == "block")
        {
            var menu = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = "编辑时间段", Icon = new SymbolIcon(Symbol.Edit) };
            edit.Click += async (_, _) => await TimeBlockEditor.ShowAsync(XamlRoot, item.Span.Start, item.Span.End, item.Span.SourceId);
            var delete = new MenuFlyoutItem { Text = "删除时间段", Icon = new SymbolIcon(Symbol.Delete) };
            delete.Click += async (_, _) =>
            {
                var confirm = new ContentDialog { XamlRoot = XamlRoot, Title = "删除时间段", Content = "仅删除该时间安排，关联任务会保留。", PrimaryButtonText = "删除", CloseButtonText = "取消" };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary) ViewModel.DeleteItem(item);
            };
            menu.Items.Add(edit); menu.Items.Add(delete); block.ContextFlyout = menu;
        }
        return block;
    }

    private static Color BlockColor(CalendarItem item) => Appearance.TryColor(item.Color, out var chosen)
        ? Color.FromArgb(65, chosen.R, chosen.G, chosen.B) : item.Span.SourceType switch
    {
        "block" => Color.FromArgb(50, 0x33, 0x66, 0xCC),   // 任务块:蓝
        "event" => Color.FromArgb(50, 0x2E, 0x8B, 0x57),   // 日程:绿
        "recurring" => Color.FromArgb(50, 0x7A, 0x3C, 0xB0), // 重复:紫
        _ => Color.FromArgb(50, 0x80, 0x80, 0x80),
    };

    private double canvasWidth;

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
    {
        canvasWidth = e.NewSize.Width > 64 ? (e.NewSize.Width - 64) / (ViewModel.ViewModeIndex == 1 ? 1 : 7) : 120;
        Rebuild();
    }

    private void UpdateNowLine()
    {
        var now = DateTimeOffset.Now;
        var dayStart = new DateTimeOffset(ViewModel.WindowStart.Date,
            TimeZoneInfo.Local.GetUtcOffset(now));
        var dayOffset = (now.Date - dayStart.Date).Days;
        if (dayOffset < 0 || dayOffset >= (ViewModel.ViewModeIndex == 1 ? 1 : 7))
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { e.Handled = true; return; }
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
        var pointerTime = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, _activeDayStart, HourHeight));
        _dragAnchorTime = item.Span.Start;
        _grabOffset = pointerTime - item.Span.Start;
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
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
                UpdatePreview(time - _grabOffset, _dragOriginalDuration);
                break;
            case DragKind.Resize:
                var end = time > _dragAnchorTime ? time : _dragAnchorTime.AddMinutes(15);
                UpdatePreview(_dragAnchorTime, end - _dragAnchorTime);
                break;
        }
    }

    private async void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragKind.None || _activeCanvas is null) return;
        var position = e.GetCurrentPoint(_activeCanvas).Position;
        var time = SlotMath.Snap(SlotMath.TimeAtOffset(position.Y, _activeDayStart, HourHeight));

        var createStart = _dragAnchorTime;
        var createEnd = time > _dragAnchorTime ? time : _dragAnchorTime.AddHours(1);
        var creating = _drag == DragKind.CreateNew;
        switch (_drag)
        {
            case DragKind.CreateNew:
                break;
            case DragKind.Move when blockIdUnderDrag is not null:
                ViewModel.MoveBlock(blockIdUnderDrag, time - _grabOffset);
                break;
            case DragKind.Resize when blockIdUnderDrag is not null:
                var resizeEnd = time > _dragAnchorTime ? time : _dragAnchorTime.AddMinutes(15);
                ViewModel.ResizeBlock(blockIdUnderDrag, resizeEnd);
                break;
        }

        _activeCanvas.ReleasePointerCaptures();
        _hitBlock?.ReleasePointerCaptures();
        ClearPreview();
        _drag = DragKind.None;
        blockIdUnderDrag = null;
        _hitBlock = null;
        _activeCanvas = null;
        if (creating) await TimeBlockEditor.ShowAsync(XamlRoot, createStart, createEnd, createTask: true);
    }

    private DateTimeOffset DayStartOf(Canvas? canvas)
    {
        var index = canvas is null ? 0 : Array.IndexOf(_days, canvas);
        if (index < 0) index = 0;
        return new DateTimeOffset(ViewModel.WindowStart.Date.AddDays(index),
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
            CornerRadius = new CornerRadius(0),
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
