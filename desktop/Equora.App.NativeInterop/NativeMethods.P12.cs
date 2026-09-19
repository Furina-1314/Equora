using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EqProposedView
    {
        public nint task_id;
        public long start;
        public long end;
        public nint reason;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EqDayLoadView
    {
        public fixed byte local_date[11]; // C 侧 UTF-8 字节
        public long planned_minutes;
        public long capacity_minutes;
        public int overloaded;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EqSplitView
    {
        public fixed byte task_id[64];
        public fixed byte title[128]; // C 侧 UTF-8 字节
        public int total_minutes;
        public int blocks;
        public int block_minutes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqDailyOut
    {
        public int completed;
        public int deferred;
        public int cancelled;
        public int big_three_done;
        public int distraction_count;
        public long planned_minutes;
        public long actual_minutes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqWeeklyOut
    {
        public long deep_work_minutes;
        public double focus_ratio;
        public double estimate_accuracy;
        public int best_focus_hour;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqAutomationInput
    {
        public nint Id;
        public nint Name;
        public nint Trigger;
        public nint Conditions;
        public nint Actions;
        public int enabled;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqAutomationView
    {
        public nint id;
        public nint name;
        public nint trigger;
        public nint conditions;
        public nint actions;
        public int enabled;
        public long revision;
    }

    [LibraryImport("equora_capi")]
    internal static partial int eq_plan_week(IntPtr core, long from, long to,
        int workStartMinute, int workEndMinute, int workdayMask, int maxBlockMinutes,
        int tzOffsetMinutes, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_proposed_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_proposed_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_proposed_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_day_loads(IntPtr core, long from, long to,
        int workStartMinute, int workEndMinute, int workdayMask, int tzOffsetMinutes,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_load_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_load_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_load_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_suggest_splits(IntPtr core, long from, long to,
        int maxBlockMinutes, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_split_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_split_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_split_list_destroy(IntPtr list);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_correct_estimate(IntPtr core, string taskId,
        out int suggested, out int samples, out int has, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_review_daily_compute(IntPtr core, long dayStartUtc,
        int tzOffsetMinutes, out EqDailyOut output, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_review_daily_save(IntPtr core, long dayStartUtc,
        int tzOffsetMinutes, string? notes, string? focusTomorrow, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_review_weekly_compute(IntPtr core, long weekStartUtc,
        int tzOffsetMinutes, out EqWeeklyOut output, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_review_weekly_save(IntPtr core, long weekStartUtc,
        int tzOffsetMinutes, string? notes, string? nextWeekGoals, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_automation_create(IntPtr core, in EqAutomationInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_automation_find(IntPtr core, string id,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_automation_update(IntPtr core, in EqAutomationInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_automation_set_enabled(IntPtr core, string id,
        int enabled, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_automation_delete(IntPtr core, string id,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_automation_list(IntPtr core, out IntPtr list,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_automation_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_automation_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_automation_list_destroy(IntPtr list);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_automation_evaluate(IntPtr core, string trigger,
        string taskId, out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_id_list_count(IntPtr list);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr eq_id_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_id_list_destroy(IntPtr list);
}

internal static partial class NativeMethods
{
    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_automation_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_automation_handle_destroy(IntPtr handle);
}

internal static partial class NativeMethods
{
    /// <summary>固定 UTF-8 字节缓冲 → 长度前缀字符串(NUL 截断)。</summary>
    internal static unsafe string FixedString(byte* buffer, int capacity)
    {
        int len = 0;
        while (len < capacity && buffer[len] != 0) len++;
        return System.Text.Encoding.UTF8.GetString(buffer, len);
    }
}
