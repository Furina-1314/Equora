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
    public string Color { get; init; } = "";
    public string Note { get; init; } = "";
    public string Title { get; init; } = "";

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : Span.SourceType == "block" && Span.TaskId is null && !string.IsNullOrWhiteSpace(Note) ? Note.Split('\n')[0] : string.IsNullOrWhiteSpace(Span.Title)
        ? (string.IsNullOrWhiteSpace(Note) ? "日程" : Note.Split('\n')[0])
        : Span.Title;
}

/// <summary>时间↔像素换算(周视图共用;15 分钟吸附)。</summary>
public static class SlotMath
{
    public const int SnapMinutes = 15;

    public static DateTimeOffset Snap(DateTimeOffset t)
    {
        // 按时刻自身偏移的壁钟吸附(LocalDateTime 会换算到系统时区,语义不对)。
        var wall = t.DateTime; // DateTimeOffset.DateTime 保留自身壁钟,Kind=Unspecified
        var snapped = SnapMinutes <= 0
            ? wall
            : wall.AddTicks(-(wall.Ticks % (TimeSpan.TicksPerMinute * SnapMinutes)));
        return new DateTimeOffset(snapped, t.Offset);
    }

    public static DateTimeOffset LocalDayStart(DateTimeOffset t)
    {
        return new DateTimeOffset(t.DateTime.Date, t.Offset);
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
        var calendars = _calendar.ListCalendars().ToDictionary(c => c.Id);
        foreach (var s in spans)
        {
            var block = s.SourceType == "block" ? _calendar.GetBlock(s.SourceId) : null;
            // 冲突集合记录的是「块 id」或「ruleId:时刻」;两者都对 SourceId 匹配。
            Items.Add(new CalendarItem
            {
                Span = s,
                IsConflict = conflictedIds.Contains(s.SourceId),
                Note = block?.Note ?? "",
                Title = block?.Title ?? "",
                Color = block is not null && calendars.TryGetValue(block.CalendarId, out var calendar) ? calendar.Color : "",
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
            if (t.Status is not (TaskStatus.Done or TaskStatus.Cancelled) && !_calendar.BlocksForTask(t.Id).Any()) UnscheduledTasks.Add(t);
        }

        StatusText = $"{WindowStart:yyyy-MM-dd} · {(ViewModeIndex == 1 ? "日视图" : "周视图")} · {Items.Count} 项 · 冲突 {conflicts.Count}";
    }

    [RelayCommand]
    public void GoToday()
    {
        var today = SlotMath.LocalDayStart(DateTimeOffset.Now);
        SelectedDay = today;
        var dow = today.DayOfWeek;
        WeekStart = today.AddDays(-(int)dow + (dow == DayOfWeek.Sunday ? -6 : 1));
        Refresh();
    }

    [RelayCommand]
    public void PrevWeek() => MoveWindow(-1);

    [RelayCommand]
    public void NextWeek() => MoveWindow(1);

    private void MoveWindow(int direction)
    {
        if (ViewModeIndex == 1)
        {
            SelectedDay = SelectedDay.AddDays(direction);
            OnPropertyChanged(nameof(WindowStart));
            OnPropertyChanged(nameof(WindowEnd));
            Refresh();
        }
        else WeekStart = WeekStart.AddDays(direction * 7);
    }

    // ---- 块编辑(接撤销) ----
    public IReadOnlyList<TimeBlockDto> CreateSemesterBlocks(SemesterSettings semester, string? weeks,
        DayOfWeek weekday, TimeSpan start, TimeSpan end, string title, string note, string color, bool addToTasks = true)
    {
        var slots = semester.Slots(weeks, weekday, start, end);
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("请输入任务标题。");
        if (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("颜色格式应为 #RRGGBB。");
        var calendar = _calendar.ListCalendars().FirstOrDefault(c => c.Color.Equals(color, StringComparison.OrdinalIgnoreCase))
            ?? _calendar.CreateCalendar("默认日程 " + color, color);
        var blocks = new List<TimeBlockDto>();
        var taskIds = new List<string>();
        var batchId = Guid.NewGuid().ToString();
        try
        {
            foreach (var slot in slots)
            {
                string? taskId = null;
                if (addToTasks)
                {
                    taskId = _tasks.CreateTask(new TaskDraft { Title = title.Trim(), Status = TaskStatus.Planned }).Id;
                    taskIds.Add(taskId);
                }
                var block = _calendar.CreateBlock(taskId, slot.Start, slot.End, note);
                blocks.Add(block);
                blocks[^1] = _calendar.UpdateBlock(block with { CalendarId = calendar.Id, Title = title.Trim(), BatchId = batchId });
            }
        }
        catch (Exception creationError)
        {
            var errors = new List<Exception> { creationError };
            foreach (var block in blocks)
                try { _calendar.DeleteBlock(block.Id); } catch (Exception ex) { errors.Add(ex); }
            foreach (var taskId in taskIds)
                try { _tasks.DeleteTask(taskId); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 1) throw new AggregateException("批量添加失败，部分内容未能撤回，请检查日历后再重试。", errors);
            throw;
        }
        Refresh();
        StatusText = $"已添加 {blocks.Count} 个日程{(addToTasks ? "及关联任务" : "，未添加到任务列表")}，可分别编辑或按批次管理。";
        return blocks;
    }

    public TimeBlockDto SaveTimeBlock(string? id, DateTimeOffset start, DateTimeOffset end, string note, string color, string? newTaskTitle = null, string? title = null)
    {
        if (end <= start) throw new ArgumentException("结束时间必须晚于开始时间。");
        if (color.Length != 7 || color[0] != '#' || !color.AsSpan(1).ToString().All(Uri.IsHexDigit)) throw new ArgumentException("颜色格式应为 #RRGGBB。");
        var calendar = _calendar.ListCalendars().FirstOrDefault(c => c.Color.Equals(color, StringComparison.OrdinalIgnoreCase))
            ?? _calendar.CreateCalendar("默认日程 " + color, color);
        var old = id is null ? null : _calendar.GetBlock(id) ?? throw new ArgumentException("日程已不存在。");
        var taskId = old?.TaskId;
        if (old is null && !string.IsNullOrWhiteSpace(newTaskTitle)) taskId = _tasks.CreateTask(new TaskDraft { Title = newTaskTitle.Trim(), Status = TaskStatus.Planned }).Id;
        var block = old ?? _calendar.CreateBlock(taskId, start, end, note);
        var saved = _calendar.UpdateBlock(block with { StartAt = start, EndAt = end, Note = note, CalendarId = calendar.Id, Title = title?.Trim() ?? (old?.Title ?? newTaskTitle?.Trim() ?? "") });
        Refresh(); return saved;
    }

    public IReadOnlyList<TimeBlockDto> BatchCandidates(string id)
    {
        var block = _calendar.GetBlock(id) ?? throw new ArgumentException("日程已不存在。");
        // Older releases did not persist batch IDs; never infer a series from matching titles.
        return string.IsNullOrEmpty(block.BatchId)
            ? _calendar.BlocksInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
            : _calendar.BlocksInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Where(b => b.BatchId == block.BatchId).ToArray();
    }

    public void EditBlocks(IReadOnlyList<TimeBlockDto> blocks, string? title, string? note, string? color, TimeSpan? start, TimeSpan? end, bool syncTaskTitles = false)
    {
        if (blocks.Count == 0) throw new ArgumentException("请至少选择一个日程。");
        if (title is not null && string.IsNullOrWhiteSpace(title)) throw new ArgumentException("请输入标题。");
        if (start.HasValue && (!end.HasValue || start.Value < TimeSpan.Zero || end.Value >= TimeSpan.FromDays(1) || end <= start))
            throw new ArgumentException("请输入同一天内有效的起止时间。");
        string? calendarId = null;
        if (color is not null)
        {
            if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit)) throw new ArgumentException("颜色格式应为 #RRGGBB。");
            calendarId = (_calendar.ListCalendars().FirstOrDefault(c => c.Color.Equals(color, StringComparison.OrdinalIgnoreCase))
                ?? _calendar.CreateCalendar("默认日程 " + color, color)).Id;
        }
        DateTimeOffset LocalTime(DateTime date, TimeSpan time)
        {
            var wall = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(wall)) throw new ArgumentException("所选日期的时间在夏令时切换中不存在。");
            return new(wall, TimeZoneInfo.Local.GetUtcOffset(wall));
        }
        var updates = blocks.Select(b => b with { Title = title?.Trim() ?? b.Title, Note = note ?? b.Note,
            CalendarId = calendarId ?? b.CalendarId,
            StartAt = start.HasValue ? LocalTime(b.StartAt.LocalDateTime, start.Value) : b.StartAt,
            EndAt = start.HasValue ? LocalTime(b.StartAt.LocalDateTime, end!.Value) : b.EndAt }).ToArray();
        _calendar.ApplyBlockBatch(updates, false, syncTaskTitles && title is not null);
        Refresh();
        StatusText = $"已批量编辑 {blocks.Count} 个日程。";
    }

    public void DeleteBlocks(IReadOnlyList<TimeBlockDto> blocks, bool deleteTasks = false)
    {
        _calendar.ApplyBlockBatch(blocks, true, deleteTasks);
        Refresh();
        StatusText = $"已删除 {blocks.Count} 个日程，{(deleteTasks ? "关联任务已移入回收站" : "关联任务保留")}。";
    }

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

    public string ExportIcs(string? destination = null)
    {
        var path = destination ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(),
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

    public CalendarEventDto? GetEvent(string id) => _calendar.GetEvent(id);

    public string? EventIdFor(CalendarItem item)
    {
        if (item.Span.SourceType == "event") return item.Span.SourceId;
        if (item.Span.SourceType != "recurring") return null;
        var separator = item.Span.SourceId.LastIndexOf(':');
        if (separator <= 0) return null;
        var rule = _calendar.GetRule(item.Span.SourceId[..separator]);
        return rule?.HostType == "event" ? rule.HostId : null;
    }

    public CalendarEventDto UpdateEvent(CalendarEventDto ev)
    {
        if (string.IsNullOrWhiteSpace(ev.Title) || ev.EndAt <= ev.StartAt)
            throw new ArgumentException("请输入标题及有效的起止时间。");
        var saved = _calendar.UpdateEvent(ev with { Title = ev.Title.Trim() });
        Refresh();
        StatusText = "日程已更新。";
        return saved;
    }

    public void DeleteEvent(string id)
    {
        if (_calendar.GetEvent(id) is null) throw new ArgumentException("日程已不存在。");
        _calendar.DeleteEvent(id);
        Refresh();
        StatusText = "日程已删除。";
    }
}
