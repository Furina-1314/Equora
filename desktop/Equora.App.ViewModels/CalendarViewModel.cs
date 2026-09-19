using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equora.App.NativeInterop;
using Equora.App.Services;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.ViewModels;

/// <summary>周/日视图的一次呈现项(物化 Span + 冲突标记)。</summary>
public sealed record CalendarItem
{
    public required SpanDto Span { get; init; }
    public bool IsConflict { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Span.Title)
        ? (Span.SourceType == "block" ? "时间块" : "日程")
        : Span.Title;
}

/// <summary>时间↔像素换算(周视图共用;15 分钟吸附)。</summary>
public static class SlotMath
{
    public const int SnapMinutes = 15;

    public static DateTimeOffset Snap(DateTimeOffset t)
    {
        var local = t.LocalDateTime;
        var snapped = SnapMinutes <= 0
            ? local
            : new DateTime(local.Ticks - local.Ticks % (TimeSpan.TicksPerMinute * SnapMinutes),
                           local.Kind);
        return new DateTimeOffset(snapped, t.Offset);
    }

    public static DateTimeOffset LocalDayStart(DateTimeOffset t)
    {
        var local = t.LocalDateTime;
        return new DateTimeOffset(local.Date, t.Offset);
    }

    /// <summary>锚定日内的偏移像素(y 向下为正)。</summary>
    public static double YOffsetWithinDay(DateTimeOffset t, DateTimeOffset dayStart,
        double hourHeight)
    {
        return (t - dayStart).TotalHours * hourHeight;
    }

    public static DateTimeOffset TimeAtOffset(double y, DateTimeOffset dayStart,
        double hourHeight)
    {
        var minutes = y / hourHeight * 60;
        return Snap(dayStart.AddMinutes(minutes));
    }
}

/// <summary>日历页视图模型:窗口物化、冲突标记、块编辑与待办拖入。</summary>
public partial class CalendarViewModel : ObservableObject
{
    private readonly ICalendarService _calendar;
    private readonly ITaskService _tasks;
    private readonly IUndoService _undo;

    public CalendarViewModel(ICalendarService calendar, ITaskService tasks, IUndoService undo)
    {
        _calendar = calendar;
        _tasks = tasks;
        _undo = undo;
        var today = SlotMath.LocalDayStart(DateTimeOffset.Now);
        _weekStart = today.AddDays(-(int)today.DayOfWeek + (today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
    }

    public ObservableCollection<CalendarItem> Items { get; } = new();

    public ObservableCollection<TaskDto> UnscheduledTasks { get; } = new();

    [ObservableProperty]
    private DateTimeOffset _weekStart;

    [ObservableProperty]
    private int _viewModeIndex; // 0 周视图 1 日视图 2 议程

    [ObservableProperty]
    private string _statusText = "";

    public bool IsAgenda => ViewModeIndex == 2;

    public DateTimeOffset WindowStart => ViewModeIndex == 1
        ? SlotMath.LocalDayStart(SelectedDay)
        : WeekStart;

    public DateTimeOffset WindowEnd => WindowStart.AddDays(ViewModeIndex == 1 ? 1 : 7);

    /// <summary>日视图选中的天(默认今天;周视图忽略)。</summary>
    public DateTimeOffset SelectedDay { get; set; } = SlotMath.LocalDayStart(DateTimeOffset.Now);

    partial void OnViewModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAgenda));
        OnPropertyChanged(nameof(WindowStart));
        OnPropertyChanged(nameof(WindowEnd));
        Refresh();
    }

    partial void OnWeekStartChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(WindowStart));
        OnPropertyChanged(nameof(WindowEnd));
        Refresh();
    }

    // ---- 刷新 ----

    [RelayCommand]
    public void Refresh()
    {
        var tz = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes;
        var spans = _calendar.WindowSpans(WindowStart, WindowEnd, tz);
        var conflicts = _calendar.WindowConflicts(WindowStart, WindowEnd, tz);

        var conflictedIds = new HashSet<string>();
        foreach (var c in conflicts)
        {
            conflictedIds.Add(c.A.SourceId);
            conflictedIds.Add(c.B.SourceId);
        }

        Items.Clear();
        foreach (var s in spans)
        {
            // 冲突集合记录的是「块 id」或「ruleId:时刻」;两者都对 SourceId 匹配。
            Items.Add(new CalendarItem
            {
                Span = s,
                IsConflict = conflictedIds.Contains(s.SourceId),
            });
        }

        UnscheduledTasks.Clear();
        foreach (var t in _tasks.QueryTasks(new TaskQuery
                 {
                     SmartList = SmartListKind.NoDate,
                     Sort = TaskSort.PriorityDesc,
                     Limit = 30,
                 }))
        {
            if (!_calendar.BlocksForTask(t.Id).Any()) UnscheduledTasks.Add(t);
        }

        StatusText = $"{WindowStart:MM-dd} 起 7 天 · {Items.Count} 项 · 冲突 {conflicts.Count}";
    }

    [RelayCommand]
    public void GoToday()
    {
        var today = SlotMath.LocalDayStart(DateTimeOffset.Now);
        SelectedDay = today;
        var dow = today.DayOfWeek;
        WeekStart = today.AddDays(-(int)dow + (dow == DayOfWeek.Sunday ? -6 : 1));
    }

    [RelayCommand]
    public void PrevWeek() => WeekStart = WeekStart.AddDays(ViewModeIndex == 1 ? -1 : -7);

    [RelayCommand]
    public void NextWeek() => WeekStart = WeekStart.AddDays(ViewModeIndex == 1 ? 1 : 7);

    // ---- 块编辑(接撤销) ----

    /// <summary>在指定槽位创建块(视图拖拽/点击创建与待办拖入共用)。</summary>
    public TimeBlockDto CreateBlockAt(DateTimeOffset start, int minutes, string? taskId)
    {
        var end = start.AddMinutes(minutes);
        var created = _calendar.CreateBlock(taskId, start, end);
        if (taskId is not null)
        {
            var task = _tasks.GetTask(taskId);
            if (task is not null && task.Status == TaskStatus.Inbox)
            {
                _tasks.UpdateTask(task with { Status = TaskStatus.Planned });
            }
        }
        _undo.Push($"安排时间块({start:MM-dd HH:mm})", () =>
        {
            _calendar.DeleteBlock(created.Id);
            if (taskId is not null)
            {
                var t = _tasks.GetTask(taskId);
                if (t is not null && t.Status == TaskStatus.Planned)
                {
                    _tasks.UpdateTask(t with { Status = TaskStatus.Inbox });
                }
            }
        });
        Refresh();
        return created;
    }

    /// <summary>移动块到新起点(保持时长)。</summary>
    public void MoveBlock(string blockId, DateTimeOffset newStart)
    {
        var block = _calendar.GetBlock(blockId);
        if (block is null) return;
        var duration = block.EndAt - block.StartAt;
        var old = block;

        var updated = _calendar.UpdateBlock(block with
        {
            StartAt = newStart,
            EndAt = newStart + duration,
        });
        _undo.Push($"移动时间块到 {newStart:MM-dd HH:mm}", () =>
        {
            var latest = _calendar.GetBlock(updated.Id);
            if (latest is not null)
            {
                _calendar.UpdateBlock(latest with { StartAt = old.StartAt, EndAt = old.EndAt });
            }
        });
        Refresh();
    }

    /// <summary>调整块长度(新结束时间)。</summary>
    public void ResizeBlock(string blockId, DateTimeOffset newEnd)
    {
        var block = _calendar.GetBlock(blockId);
        if (block is null || newEnd <= block.StartAt) return;
        var old = block;

        var updated = _calendar.UpdateBlock(block with { EndAt = newEnd });
        _undo.Push($"调整时间块时长({(int)(newEnd - block.StartAt).TotalMinutes} 分钟)", () =>
        {
            var latest = _calendar.GetBlock(updated.Id);
            if (latest is not null) _calendar.UpdateBlock(latest with { EndAt = old.EndAt });
        });
        Refresh();
    }

    [RelayCommand]
    public void DeleteItem(CalendarItem? item)
    {
        if (item is null || item.Span.SourceType != "block") return;
        var id = ExtractBlockId(item.Span.SourceId);
        if (id is null) return;

        var block = _calendar.GetBlock(id);
        if (block is null) return;
        _calendar.DeleteBlock(id);
        _undo.Push("取消时间块", () =>
        {
            // 软删除暂无 ABI 恢复入口(P8 冲突中心阶段补);此处仅提示。
        });
        StatusText = "已取消时间块";
        Refresh();
    }

    /// <summary>SourceId 形如 UUID(块)或 ruleId:timestamp(重复实例)。</summary>
    public static string? ExtractBlockId(string sourceId)
    {
        var colon = sourceId.LastIndexOf(':');
        if (colon < 0) return sourceId;
        return sourceId.Length == 36 && colon < 0 ? sourceId : (IsValidUuid(sourceId) ? sourceId : null);
    }

    private static bool IsValidUuid(string s)
    {
        if (s.Length != 36 || s[8] != '-' || s[13] != '-' || s[18] != '-' || s[23] != '-')
        {
            return false;
        }
        foreach (var c in s)
        {
            if (c == '-') continue;
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    [RelayCommand]
    public void Undo()
    {
        var description = _undo.Undo();
        StatusText = description is null ? "没有可撤销的操作" : $"已撤销:{description}";
        Refresh();
    }

    // ---- ICS ----

    public string ExportIcs()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"equora-{Guid.NewGuid():N}.ics");
        var count = _calendar.ExportIcs(WindowStart, WindowEnd, path);
        StatusText = $"已导出 {count} 项到 {path}";
        return path;
    }

    public void ImportIcs(string path)
    {
        var (imported, skipped, failed) = _calendar.ImportIcs(path);
        StatusText = $"导入完成:新增 {imported},跳过 {skipped},失败 {failed}";
        Refresh();
    }
}
