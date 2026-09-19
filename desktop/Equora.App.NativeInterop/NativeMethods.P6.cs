using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

// P6 日历 ABI 的互操作定义(布局与 equora_capi.h 严格对应)。
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EqCalendarView
    {
        public nint Id;
        public nint Name;
        public nint Color;
        public nint Source;
        public int is_visible;
        public long created_at;
        public long updated_at;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqBlockInput
    {
        public nint Id;
        public nint TaskId;
        public nint CalendarId;
        public long start_at;
        public long end_at;
        public int prepare_minutes;
        public int buffer_minutes;
        public int actual_minutes;
        public nint Note;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqBlockView
    {
        public nint Id;
        public nint TaskId;
        public nint CalendarId;
        public long start_at;
        public long end_at;
        public int prepare_minutes;
        public int buffer_minutes;
        public int actual_minutes;
        public nint Note;
        public long created_at;
        public long updated_at;
        public long revision;
        public long deleted_at;
        public int has_deleted;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqEventInput
    {
        public nint Id;
        public nint Title;
        public nint Location;
        public nint Note;
        public nint CalendarId;
        public long start_at;
        public long end_at;
        public int is_all_day;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqEventView
    {
        public nint Id;
        public nint Title;
        public nint Location;
        public nint Note;
        public nint CalendarId;
        public long start_at;
        public long end_at;
        public int is_all_day;
        public long created_at;
        public long updated_at;
        public long revision;
        public long deleted_at;
        public int has_deleted;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqRuleInput
    {
        public nint Id;
        public nint HostType;
        public nint HostId;
        public int freq;
        public int interval;
        public nint ByWeekday;
        public int month_mode;
        public int month_nth;
        public int month_weekday;
        public long until_utc;
        public int has_until;
        public long max_count;
        public int has_max_count;
        public int complete_recur_days;
        public nint ExcludedDates;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqRuleView
    {
        public nint Id;
        public nint HostType;
        public nint HostId;
        public int freq;
        public int interval;
        public nint ByWeekday;
        public int month_mode;
        public int month_nth;
        public int month_weekday;
        public long until_utc;
        public int has_until;
        public long max_count;
        public int has_max_count;
        public int complete_recur_days;
        public nint ExcludedDates;
        public nint RruleText;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqSpanView
    {
        public nint SourceId;
        public nint SourceType;
        public nint Title;
        public nint TaskId;
        public long start;
        public long end;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqConflictView
    {
        public EqSpanView a;
        public EqSpanView b;
        public long overlap_minutes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqSlotView
    {
        public long start;
        public long end;
    }

    // ---- 导入 ----

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_calendar_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_calendar_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_calendar_create(IntPtr core, string name, string? color,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_calendar_list(IntPtr core, out IntPtr list,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_calendar_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_calendar_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_calendar_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_block_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_block_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_block_create(IntPtr core, in EqBlockInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_block_get(IntPtr core, string id, int includeDeleted,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_block_update(IntPtr core, in EqBlockInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_block_set_deleted(IntPtr core, string id, int deleted,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_block_list_range(IntPtr core, long from, long to,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_block_list_for_task(IntPtr core, string taskId,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_block_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_block_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_block_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_event_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_event_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_event_create(IntPtr core, in EqEventInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_event_get(IntPtr core, string id, int includeDeleted,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_event_update(IntPtr core, in EqEventInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_event_set_deleted(IntPtr core, string id, int deleted,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_event_list_range(IntPtr core, long from, long to,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_event_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_event_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_event_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_rule_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_rule_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_rule_create(IntPtr core, in EqRuleInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_rule_get(IntPtr core, string id, out IntPtr handle,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_rule_update(IntPtr core, in EqRuleInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_rule_delete(IntPtr core, string id, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_rule_list_for_host(IntPtr core, string hostType,
        string hostId, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_rule_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_rule_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_rule_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_window_spans(IntPtr core, long from, long to,
        int tzOffsetMinutes, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_span_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_span_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_span_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_window_conflicts(IntPtr core, long from, long to,
        int tzOffsetMinutes, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_conflict_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_conflict_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_conflict_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_find_free_slots(IntPtr core, long from, long to,
        int workStartMinute, int workEndMinute, int workdayMask, long minMinutes, int limit,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_slot_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_slot_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_slot_list_destroy(IntPtr list);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_event_detach_occurrence(IntPtr core, string ruleId,
        long occurrenceStartUtc, int tzOffsetMinutes, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_rule_split_series(IntPtr core, string ruleId,
        long occurrenceStartUtc, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_export_ics(IntPtr core, long from, long to, string path,
        out int count, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_import_ics(IntPtr core, string path, out int imported,
        out int skipped, out int failed, out EqError error);
}
