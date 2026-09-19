using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>ICalendarService 实现:转发到原生核心。</summary>
public sealed partial class AppDataService
{
    public CalendarDto CreateCalendar(string name, string color = "") =>
        _core.CreateCalendar(name, color);
    public IReadOnlyList<CalendarDto> ListCalendars() => _core.ListCalendars();

    public TimeBlockDto CreateBlock(string? taskId, DateTimeOffset start, DateTimeOffset end,
        string note = "", int prepareMinutes = 0, int bufferMinutes = 0) =>
        _core.CreateBlock(taskId, start, end, note, prepareMinutes, bufferMinutes);
    public TimeBlockDto? GetBlock(string id, bool includeDeleted = false) =>
        _core.GetBlock(id, includeDeleted);
    public TimeBlockDto UpdateBlock(TimeBlockDto block) => _core.UpdateBlock(block);
    public void DeleteBlock(string id) => _core.DeleteBlock(id);
    public IReadOnlyList<TimeBlockDto> BlocksInRange(DateTimeOffset from, DateTimeOffset to) =>
        _core.BlocksInRange(from, to);
    public IReadOnlyList<TimeBlockDto> BlocksForTask(string taskId) =>
        _core.BlocksForTask(taskId);

    public CalendarEventDto CreateEvent(string title, DateTimeOffset start,
        DateTimeOffset end, string location = "", string note = "", string? id = null) =>
        _core.CreateEvent(title, start, end, location, note, id);
    public CalendarEventDto? GetEvent(string id, bool includeDeleted = false) =>
        _core.GetEvent(id, includeDeleted);
    public CalendarEventDto UpdateEvent(CalendarEventDto ev) => _core.UpdateEvent(ev);
    public void DeleteEvent(string id) => _core.DeleteEvent(id);
    public IReadOnlyList<CalendarEventDto> EventsInRange(DateTimeOffset from,
        DateTimeOffset to) => _core.EventsInRange(from, to);

    public RecurrenceRuleDto CreateRule(string hostType, string hostId, RecurFreqDto freq,
        int interval = 1, IReadOnlyList<int>? byWeekday = null,
        MonthModeDto monthMode = MonthModeDto.Date, int monthNth = 1, int monthWeekday = 0,
        DateTimeOffset? until = null, long? maxCount = null, int completeRecurDays = 0,
        IReadOnlyList<string>? excludedDates = null) =>
        _core.CreateRule(hostType, hostId, freq, interval, byWeekday, monthMode, monthNth,
            monthWeekday, until, maxCount, completeRecurDays, excludedDates);
    public RecurrenceRuleDto? GetRule(string id) => _core.GetRule(id);
    public RecurrenceRuleDto UpdateRule(RecurrenceRuleDto rule) => _core.UpdateRule(rule);
    public void DeleteRule(string id) => _core.DeleteRule(id);
    public IReadOnlyList<RecurrenceRuleDto> RulesForHost(string hostType, string hostId) =>
        _core.RulesForHost(hostType, hostId);

    public IReadOnlyList<SpanDto> WindowSpans(DateTimeOffset from, DateTimeOffset to,
        int tzOffsetMinutes) => _core.WindowSpans(from, to, tzOffsetMinutes);
    public IReadOnlyList<ConflictDto> WindowConflicts(DateTimeOffset from, DateTimeOffset to,
        int tzOffsetMinutes) => _core.WindowConflicts(from, to, tzOffsetMinutes);
    public IReadOnlyList<FreeSlotDto> FindFreeSlots(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute, int workEndMinute, int workdayMask, long minMinutes,
        int limit = 0) =>
        _core.FindFreeSlots(from, to, workStartMinute, workEndMinute, workdayMask,
            minMinutes, limit);

    public CalendarEventDto DetachOccurrence(string ruleId, DateTimeOffset occurrenceStart,
        int tzOffsetMinutes) => _core.DetachOccurrence(ruleId, occurrenceStart, tzOffsetMinutes);
    public RecurrenceRuleDto SplitSeries(string ruleId, DateTimeOffset occurrenceStart) =>
        _core.SplitSeries(ruleId, occurrenceStart);

    public int ExportIcs(DateTimeOffset from, DateTimeOffset to, string path) =>
        _core.ExportIcs(from, to, path);
    public (int Imported, int Skipped, int Failed) ImportIcs(string path) =>
        _core.ImportIcs(path);
}
