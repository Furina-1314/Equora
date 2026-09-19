using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>P6 日历数据面:日历、时间块、事件、重复规则、窗口/冲突/空闲、例外编辑、ICS。</summary>
public interface ICalendarService
{
    CalendarDto CreateCalendar(string name, string color = "");
    IReadOnlyList<CalendarDto> ListCalendars();

    TimeBlockDto CreateBlock(string? taskId, DateTimeOffset start, DateTimeOffset end,
        string note = "", int prepareMinutes = 0, int bufferMinutes = 0);
    TimeBlockDto? GetBlock(string id, bool includeDeleted = false);
    TimeBlockDto UpdateBlock(TimeBlockDto block);
    void DeleteBlock(string id);
    IReadOnlyList<TimeBlockDto> BlocksInRange(DateTimeOffset from, DateTimeOffset to);
    IReadOnlyList<TimeBlockDto> BlocksForTask(string taskId);

    CalendarEventDto CreateEvent(string title, DateTimeOffset start, DateTimeOffset end,
        string location = "", string note = "", string? id = null);
    CalendarEventDto? GetEvent(string id, bool includeDeleted = false);
    CalendarEventDto UpdateEvent(CalendarEventDto ev);
    void DeleteEvent(string id);
    IReadOnlyList<CalendarEventDto> EventsInRange(DateTimeOffset from, DateTimeOffset to);

    RecurrenceRuleDto CreateRule(string hostType, string hostId, RecurFreqDto freq,
        int interval = 1, IReadOnlyList<int>? byWeekday = null,
        MonthModeDto monthMode = MonthModeDto.Date, int monthNth = 1, int monthWeekday = 0,
        DateTimeOffset? until = null, long? maxCount = null, int completeRecurDays = 0,
        IReadOnlyList<string>? excludedDates = null);
    RecurrenceRuleDto? GetRule(string id);
    RecurrenceRuleDto UpdateRule(RecurrenceRuleDto rule);
    void DeleteRule(string id);
    IReadOnlyList<RecurrenceRuleDto> RulesForHost(string hostType, string hostId);

    IReadOnlyList<SpanDto> WindowSpans(DateTimeOffset from, DateTimeOffset to,
        int tzOffsetMinutes);
    IReadOnlyList<ConflictDto> WindowConflicts(DateTimeOffset from, DateTimeOffset to,
        int tzOffsetMinutes);
    IReadOnlyList<FreeSlotDto> FindFreeSlots(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute, int workEndMinute, int workdayMask, long minMinutes,
        int limit = 0);

    CalendarEventDto DetachOccurrence(string ruleId, DateTimeOffset occurrenceStart,
        int tzOffsetMinutes);
    RecurrenceRuleDto SplitSeries(string ruleId, DateTimeOffset occurrenceStart);

    int ExportIcs(DateTimeOffset from, DateTimeOffset to, string path);
    (int Imported, int Skipped, int Failed) ImportIcs(string path);
}
