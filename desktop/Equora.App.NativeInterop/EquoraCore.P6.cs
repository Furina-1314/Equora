using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

public sealed record CalendarDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Color { get; init; } = "";
    public string Source { get; init; } = "local";
    public bool IsVisible { get; init; } = true;
}

public sealed record TimeBlockDto
{
    public required string Id { get; init; }
    public string? TaskId { get; init; }
    public string CalendarId { get; init; } = "";
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset EndAt { get; init; }
    public int PrepareMinutes { get; init; }
    public int BufferMinutes { get; init; }
    public int ActualMinutes { get; init; }
    public string Note { get; init; } = "";
    public string Title { get; init; } = "";
    public string BatchId { get; init; } = "";
    public long Revision { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed record CalendarEventDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Location { get; init; } = "";
    public string Note { get; init; } = "";
    public string CalendarId { get; init; } = "";
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset EndAt { get; init; }
    public bool IsAllDay { get; init; }
    public long Revision { get; init; }
}

public enum RecurFreqDto
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}

public enum MonthModeDto
{
    Date = 0,
    NthWeekday = 1,
    LastWeekday = 2,
}

/// <summary>重复规则(结构化字段;RruleText 由原生层序列化)。</summary>
public sealed record RecurrenceRuleDto
{
    public required string Id { get; init; }
    public required string HostType { get; init; }
    public required string HostId { get; init; }
    public RecurFreqDto Freq { get; init; }
    public int Interval { get; init; } = 1;
    public IReadOnlyList<int> ByWeekday { get; init; } = Array.Empty<int>();
    public MonthModeDto MonthMode { get; init; } = MonthModeDto.Date;
    public int MonthNth { get; init; } = 1;
    public int MonthWeekday { get; init; }
    public DateTimeOffset? Until { get; init; }
    public long? MaxCount { get; init; }
    public int CompleteRecurDays { get; init; }
    public IReadOnlyList<string> ExcludedDates { get; init; } = Array.Empty<string>();
    public string RruleText { get; init; } = "";
    public long Revision { get; init; }
}

/// <summary>窗口物化产物(块/事件/重复实例)。</summary>
public sealed record SpanDto
{
    public required string SourceId { get; init; }
    public required string SourceType { get; init; }
    public string Title { get; init; } = "";
    public string? TaskId { get; init; }
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
}

public sealed record ConflictDto
{
    public required SpanDto A { get; init; }
    public required SpanDto B { get; init; }
    public long OverlapMinutes { get; init; }
}

public sealed record FreeSlotDto
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public long Minutes => (long)(End - Start).TotalMinutes;
}

/// <summary>P6 日历接口(在 EquoraCore 上扩展)。</summary>
public sealed partial class EquoraCore
{
    public CalendarDto CreateCalendar(string name, string color = "")
    {
        var rc = NativeMethods.eq_calendar_create(_core, name, color, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "calendar_create");
        return ReadCalendar(handle);
    }

    public IReadOnlyList<CalendarDto> ListCalendars()
    {
        var rc = NativeMethods.eq_calendar_list(_core, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "calendar_list");
        try
        {
            var result = new List<CalendarDto>();
            var count = NativeMethods.eq_calendar_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_calendar_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromCalendarView(
                    Marshal.PtrToStructure<NativeMethods.EqCalendarView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_calendar_list_destroy(list);
        }
    }

    private static CalendarDto ReadCalendar(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_calendar_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "calendar view unavailable");
            return FromCalendarView(Marshal.PtrToStructure<NativeMethods.EqCalendarView>(ptr));
        }
        finally
        {
            NativeMethods.eq_calendar_handle_destroy(handle);
        }
    }

    private static CalendarDto FromCalendarView(NativeMethods.EqCalendarView v) => new()
    {
        Id = v.Id.Str() ?? "",
        Name = v.Name.Str() ?? "",
        Color = v.Color.Str() ?? "",
        Source = v.Source.Str() ?? "local",
        IsVisible = v.is_visible != 0,
    };

    // ---- 时间块 ----

    public TimeBlockDto CreateBlock(string? taskId, DateTimeOffset start, DateTimeOffset end,
        string note = "", int prepareMinutes = 0, int bufferMinutes = 0)
    {
        using var scope = new BlockScope(taskId, null, start, end, note, prepareMinutes,
            bufferMinutes, 0, actualMinutes: 0, id: null);
        var rc = NativeMethods.eq_block_create(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "block_create");
        return ReadBlock(handle);
    }

    public TimeBlockDto? GetBlock(string id, bool includeDeleted = false)
    {
        var rc = NativeMethods.eq_block_get(_core, id, includeDeleted ? 1 : 0, out var handle,
            out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "block_get");
        return ReadBlock(handle);
    }

    public TimeBlockDto UpdateBlock(TimeBlockDto block)
    {
        using var scope = new BlockScope(block.TaskId, block.CalendarId, block.StartAt,
            block.EndAt, block.Note, block.PrepareMinutes, block.BufferMinutes,
            block.Revision, block.ActualMinutes, block.Id, block.Title, block.BatchId);
        var rc = NativeMethods.eq_block_update(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "block_update");
        return ReadBlock(handle);
    }

    public void ApplyBlockBatch(IReadOnlyList<TimeBlockDto> blocks, bool delete, bool affectTasks = false)
    {
        var scopes = new List<BlockScope>();
        try
        {
            foreach (var b in blocks) scopes.Add(new(b.TaskId, b.CalendarId, b.StartAt, b.EndAt, b.Note,
                b.PrepareMinutes, b.BufferMinutes, b.Revision, b.ActualMinutes, b.Id, b.Title, b.BatchId));
            var rc = NativeMethods.eq_block_apply_batch(_core, scopes.Select(s => s.Value).ToArray(), scopes.Count, delete ? 1 : 0, affectTasks ? 1 : 0, out var error);
            EquoraException.ThrowIfFailed(rc, error, "block_apply_batch");
        }
        finally { foreach (var scope in scopes) scope.Dispose(); }
    }

    public void DeleteBlock(string id)
    {
        var rc = NativeMethods.eq_block_set_deleted(_core, id, 1, out var error);
        EquoraException.ThrowIfFailed(rc, error, "block_set_deleted");
    }

    public IReadOnlyList<TimeBlockDto> BlocksInRange(DateTimeOffset from, DateTimeOffset to)
    {
        var rc = NativeMethods.eq_block_list_range(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "block_list_range");
        return ReadBlockList(list);
    }

    public IReadOnlyList<TimeBlockDto> BlocksForTask(string taskId)
    {
        var rc = NativeMethods.eq_block_list_for_task(_core, taskId, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "block_list_for_task");
        return ReadBlockList(list);
    }

    private static IReadOnlyList<TimeBlockDto> ReadBlockList(IntPtr list)
    {
        try
        {
            var result = new List<TimeBlockDto>();
            var count = NativeMethods.eq_block_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_block_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromBlockView(
                    Marshal.PtrToStructure<NativeMethods.EqBlockView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_block_list_destroy(list);
        }
    }

    private static TimeBlockDto ReadBlock(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_block_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "block view unavailable");
            return FromBlockView(Marshal.PtrToStructure<NativeMethods.EqBlockView>(ptr));
        }
        finally
        {
            NativeMethods.eq_block_handle_destroy(handle);
        }
    }

    private static TimeBlockDto FromBlockView(NativeMethods.EqBlockView v) => new()
    {
        Id = v.Id.Str() ?? "",
        TaskId = v.TaskId.Str(),
        CalendarId = v.CalendarId.Str() ?? "",
        StartAt = DateTimeOffset.FromUnixTimeMilliseconds(v.start_at),
        EndAt = DateTimeOffset.FromUnixTimeMilliseconds(v.end_at),
        PrepareMinutes = v.prepare_minutes,
        BufferMinutes = v.buffer_minutes,
        ActualMinutes = v.actual_minutes,
        Note = v.Note.Str() ?? "",
        Title = v.Title.Str() ?? "",
        BatchId = v.BatchId.Str() ?? "",
        Revision = v.revision,
        IsDeleted = v.has_deleted != 0,
    };

    private sealed class BlockScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();
        public NativeMethods.EqBlockInput Value;

        public BlockScope(string? taskId, string? calendarId, DateTimeOffset start,
            DateTimeOffset end, string note, int prepare, int buffer, long revision,
            int actualMinutes, string? id, string title = "", string batchId = "")
        {
            Value = new NativeMethods.EqBlockInput
            {
                Id = Pin(id),
                TaskId = Pin(string.IsNullOrEmpty(taskId) ? null : taskId),
                CalendarId = Pin(calendarId),
                start_at = start.ToUnixTimeMilliseconds(),
                end_at = end.ToUnixTimeMilliseconds(),
                prepare_minutes = prepare,
                buffer_minutes = buffer,
                actual_minutes = actualMinutes,
                Note = Pin(string.IsNullOrEmpty(note) ? null : note),
                revision = revision,
                Title = Pin(title),
                BatchId = Pin(batchId),
            };
        }

        private nint Pin(string? s)
        {
            if (s is null) return 0;
            var holder = new Utf8NativeString(s);
            _owned.Add(holder);
            return holder.Pointer;
        }

        public void Dispose()
        {
            foreach (var h in _owned) h.Dispose();
            _owned.Clear();
        }
    }

    // ---- 事件 ----

    public CalendarEventDto CreateEvent(string title, DateTimeOffset start, DateTimeOffset end,
        string location = "", string note = "", string? id = null)
    {
        using var scope = new EventScope(title, location, note, "", start, end, false, 0, id);
        var rc = NativeMethods.eq_event_create(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "event_create");
        return ReadEvent(handle);
    }

    public CalendarEventDto? GetEvent(string id, bool includeDeleted = false)
    {
        var rc = NativeMethods.eq_event_get(_core, id, includeDeleted ? 1 : 0, out var handle,
            out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "event_get");
        return ReadEvent(handle);
    }

    public CalendarEventDto UpdateEvent(CalendarEventDto ev)
    {
        using var scope = new EventScope(ev.Title, ev.Location, ev.Note, ev.CalendarId,
            ev.StartAt, ev.EndAt, ev.IsAllDay, ev.Revision, ev.Id);
        var rc = NativeMethods.eq_event_update(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "event_update");
        return ReadEvent(handle);
    }

    public void DeleteEvent(string id)
    {
        var rc = NativeMethods.eq_event_set_deleted(_core, id, 1, out var error);
        EquoraException.ThrowIfFailed(rc, error, "event_set_deleted");
    }

    public IReadOnlyList<CalendarEventDto> EventsInRange(DateTimeOffset from,
        DateTimeOffset to)
    {
        var rc = NativeMethods.eq_event_list_range(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "event_list_range");
        try
        {
            var result = new List<CalendarEventDto>();
            var count = NativeMethods.eq_event_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_event_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromEventView(
                    Marshal.PtrToStructure<NativeMethods.EqEventView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_event_list_destroy(list);
        }
    }

    private static CalendarEventDto ReadEvent(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_event_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "event view unavailable");
            return FromEventView(Marshal.PtrToStructure<NativeMethods.EqEventView>(ptr));
        }
        finally
        {
            NativeMethods.eq_event_handle_destroy(handle);
        }
    }

    private static CalendarEventDto FromEventView(NativeMethods.EqEventView v) => new()
    {
        Id = v.Id.Str() ?? "",
        Title = v.Title.Str() ?? "",
        Location = v.Location.Str() ?? "",
        Note = v.Note.Str() ?? "",
        CalendarId = v.CalendarId.Str() ?? "",
        StartAt = DateTimeOffset.FromUnixTimeMilliseconds(v.start_at),
        EndAt = DateTimeOffset.FromUnixTimeMilliseconds(v.end_at),
        IsAllDay = v.is_all_day != 0,
        Revision = v.revision,
    };

    private sealed class EventScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();
        public NativeMethods.EqEventInput Value;

        public EventScope(string title, string location, string note, string calendarId,
            DateTimeOffset start, DateTimeOffset end, bool allDay, long revision, string? id)
        {
            Value = new NativeMethods.EqEventInput
            {
                Id = Pin(id),
                Title = Pin(title),
                Location = Pin(string.IsNullOrEmpty(location) ? null : location),
                Note = Pin(string.IsNullOrEmpty(note) ? null : note),
                CalendarId = Pin(string.IsNullOrEmpty(calendarId) ? null : calendarId),
                start_at = start.ToUnixTimeMilliseconds(),
                end_at = end.ToUnixTimeMilliseconds(),
                is_all_day = allDay ? 1 : 0,
                revision = revision,
            };
        }

        private nint Pin(string? s)
        {
            if (s is null) return 0;
            var holder = new Utf8NativeString(s);
            _owned.Add(holder);
            return holder.Pointer;
        }

        public void Dispose()
        {
            foreach (var h in _owned) h.Dispose();
            _owned.Clear();
        }
    }

    // ---- 重复规则 ----

    public RecurrenceRuleDto CreateRule(string hostType, string hostId, RecurFreqDto freq,
        int interval = 1, IReadOnlyList<int>? byWeekday = null,
        MonthModeDto monthMode = MonthModeDto.Date, int monthNth = 1, int monthWeekday = 0,
        DateTimeOffset? until = null, long? maxCount = null, int completeRecurDays = 0,
        IReadOnlyList<string>? excludedDates = null)
    {
        using var scope = new RuleScope(hostType, hostId, freq, interval, byWeekday, monthMode,
            monthNth, monthWeekday, until, maxCount, completeRecurDays, excludedDates, 0, id: null);
        var rc = NativeMethods.eq_rule_create(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "rule_create");
        return ReadRule(handle);
    }

    public RecurrenceRuleDto? GetRule(string id)
    {
        var rc = NativeMethods.eq_rule_get(_core, id, out var handle, out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "rule_get");
        return ReadRule(handle);
    }

    public RecurrenceRuleDto UpdateRule(RecurrenceRuleDto rule)
    {
        using var scope = new RuleScope(rule.HostType, rule.HostId, rule.Freq, rule.Interval,
            rule.ByWeekday, rule.MonthMode, rule.MonthNth, rule.MonthWeekday, rule.Until,
            rule.MaxCount, rule.CompleteRecurDays, rule.ExcludedDates, rule.Revision,
            rule.Id);
        var rc = NativeMethods.eq_rule_update(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "rule_update");
        return ReadRule(handle);
    }

    public void DeleteRule(string id)
    {
        var rc = NativeMethods.eq_rule_delete(_core, id, out var error);
        EquoraException.ThrowIfFailed(rc, error, "rule_delete");
    }

    public IReadOnlyList<RecurrenceRuleDto> RulesForHost(string hostType, string hostId)
    {
        var rc = NativeMethods.eq_rule_list_for_host(_core, hostType, hostId, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "rule_list_for_host");
        try
        {
            var result = new List<RecurrenceRuleDto>();
            var count = NativeMethods.eq_rule_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_rule_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromRuleView(
                    Marshal.PtrToStructure<NativeMethods.EqRuleView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_rule_list_destroy(list);
        }
    }

    private static RecurrenceRuleDto ReadRule(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_rule_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "rule view unavailable");
            return FromRuleView(Marshal.PtrToStructure<NativeMethods.EqRuleView>(ptr));
        }
        finally
        {
            NativeMethods.eq_rule_handle_destroy(handle);
        }
    }

    private static RecurrenceRuleDto FromRuleView(NativeMethods.EqRuleView v) => new()
    {
        Id = v.Id.Str() ?? "",
        HostType = v.HostType.Str() ?? "",
        HostId = v.HostId.Str() ?? "",
        Freq = (RecurFreqDto)v.freq,
        Interval = v.interval,
        ByWeekday = SplitCsv(v.ByWeekday.Str()).Select(int.Parse).ToArray(),
        MonthMode = (MonthModeDto)v.month_mode,
        MonthNth = v.month_nth,
        MonthWeekday = v.month_weekday,
        Until = v.has_until != 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(v.until_utc) : null,
        MaxCount = v.has_max_count != 0 ? v.max_count : null,
        CompleteRecurDays = v.complete_recur_days,
        ExcludedDates = SplitCsv(v.ExcludedDates.Str()),
        RruleText = v.RruleText.Str() ?? "",
        Revision = v.revision,
    };

    private static string[] SplitCsv(string? csv) =>
        string.IsNullOrEmpty(csv) ? Array.Empty<string>() : csv.Split(',');

    private sealed class RuleScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();
        public NativeMethods.EqRuleInput Value;

        public RuleScope(string hostType, string hostId, RecurFreqDto freq, int interval,
            IReadOnlyList<int>? byWeekday, MonthModeDto monthMode, int monthNth,
            int monthWeekday, DateTimeOffset? until, long? maxCount, int completeRecurDays,
            IReadOnlyList<string>? excludedDates, long revision, string? id)
        {
            var byCsv = byWeekday is null ? "" : string.Join(",", byWeekday);
            var exCsv = excludedDates is null ? "" : string.Join(",", excludedDates);
            Value = new NativeMethods.EqRuleInput
            {
                Id = Pin(id),
                HostType = Pin(hostType),
                HostId = Pin(hostId),
                freq = (int)freq,
                interval = interval,
                ByWeekday = Pin(byCsv.Length == 0 ? null : byCsv),
                month_mode = (int)monthMode,
                month_nth = monthNth,
                month_weekday = monthWeekday,
                until_utc = until?.ToUnixTimeMilliseconds() ?? 0,
                has_until = until is not null ? 1 : 0,
                max_count = maxCount ?? 0,
                has_max_count = maxCount is not null ? 1 : 0,
                complete_recur_days = completeRecurDays,
                ExcludedDates = Pin(exCsv.Length == 0 ? null : exCsv),
                revision = revision,
            };
        }

        private nint Pin(string? s)
        {
            if (s is null) return 0;
            var holder = new Utf8NativeString(s);
            _owned.Add(holder);
            return holder.Pointer;
        }

        public void Dispose()
        {
            foreach (var h in _owned) h.Dispose();
            _owned.Clear();
        }
    }

    // ---- 窗口物化、冲突、空闲 ----

    public IReadOnlyList<SpanDto> WindowSpans(DateTimeOffset from, DateTimeOffset to,
        int tzOffsetMinutes)
    {
        var rc = NativeMethods.eq_window_spans(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), tzOffsetMinutes, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "window_spans");
        try
        {
            var result = new List<SpanDto>();
            var count = NativeMethods.eq_span_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_span_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromSpanView(
                    Marshal.PtrToStructure<NativeMethods.EqSpanView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_span_list_destroy(list);
        }
    }

    private static SpanDto FromSpanView(NativeMethods.EqSpanView v) => new()
    {
        SourceId = v.SourceId.Str() ?? "",
        SourceType = v.SourceType.Str() ?? "",
        Title = v.Title.Str() ?? "",
        TaskId = v.TaskId.Str(),
        Start = DateTimeOffset.FromUnixTimeMilliseconds(v.start),
        End = DateTimeOffset.FromUnixTimeMilliseconds(v.end),
    };

    public IReadOnlyList<ConflictDto> WindowConflicts(DateTimeOffset from, DateTimeOffset to,
        int tzOffsetMinutes)
    {
        var rc = NativeMethods.eq_window_conflicts(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), tzOffsetMinutes, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "window_conflicts");
        try
        {
            var result = new List<ConflictDto>();
            var count = NativeMethods.eq_conflict_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_conflict_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqConflictView>(ptr);
                result.Add(new ConflictDto
                {
                    A = FromSpanView(v.a),
                    B = FromSpanView(v.b),
                    OverlapMinutes = v.overlap_minutes,
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_conflict_list_destroy(list);
        }
    }

    /// <summary>查找空闲段。workdayMask:bit0=周一 .. bit6=周日。</summary>
    public IReadOnlyList<FreeSlotDto> FindFreeSlots(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute, int workEndMinute, int workdayMask, long minMinutes, int limit = 0)
    {
        var rc = NativeMethods.eq_find_free_slots(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), workStartMinute, workEndMinute, workdayMask,
            minMinutes, limit, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "find_free_slots");
        try
        {
            var result = new List<FreeSlotDto>();
            var count = NativeMethods.eq_slot_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_slot_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqSlotView>(ptr);
                result.Add(new FreeSlotDto
                {
                    Start = DateTimeOffset.FromUnixTimeMilliseconds(v.start),
                    End = DateTimeOffset.FromUnixTimeMilliseconds(v.end),
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_slot_list_destroy(list);
        }
    }

    // ---- 例外编辑与 ICS ----

    public CalendarEventDto DetachOccurrence(string ruleId, DateTimeOffset occurrenceStart,
        int tzOffsetMinutes)
    {
        var rc = NativeMethods.eq_event_detach_occurrence(_core, ruleId,
            occurrenceStart.ToUnixTimeMilliseconds(), tzOffsetMinutes, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "detach_occurrence");
        return ReadEvent(handle);
    }

    public RecurrenceRuleDto SplitSeries(string ruleId, DateTimeOffset occurrenceStart)
    {
        var rc = NativeMethods.eq_rule_split_series(_core, ruleId,
            occurrenceStart.ToUnixTimeMilliseconds(), out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "split_series");
        return ReadRule(handle);
    }

    public int ExportIcs(DateTimeOffset from, DateTimeOffset to, string path)
    {
        var rc = NativeMethods.eq_export_ics(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), path, out var count, out var error);
        EquoraException.ThrowIfFailed(rc, error, "export_ics");
        return count;
    }

    public (int Imported, int Skipped, int Failed) ImportIcs(string path)
    {
        var rc = NativeMethods.eq_import_ics(_core, path, out var imported, out var skipped,
            out var failed, out var error);
        EquoraException.ThrowIfFailed(rc, error, "import_ics");
        return (imported, skipped, failed);
    }
}
