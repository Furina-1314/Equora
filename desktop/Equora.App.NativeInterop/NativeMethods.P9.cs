using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EqFocusSessionView
    {
        public nint id;
        public nint task_id;
        public nint block_id;
        public int mode;
        public long planned_start;
        public long planned_end;
        public int has_planned_end;
        public long actual_start;
        public long actual_end;
        public int has_actual_end;
        public long paused_ms;
        public int state;
        public nint goal;
        public nint completion_note;
        public int completion_level;
        public long created_at;
        public long updated_at;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqInterruptionView
    {
        public nint id;
        public nint session_id;
        public long occurred_at;
        public long duration_ms;
        public nint reason;
        public nint source;
        public nint handling;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqDistractionView
    {
        public nint id;
        public nint session_id;
        public nint content;
        public long captured_at;
        public int resolution;
        public nint resolved_ref;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqFocusProfileInput
    {
        public nint Id;
        public nint Name;
        public int mode;
        public int planned_minutes;
        public int break_minutes;
        public nint AllowedApps;
        public nint BlockedApps;
        public nint AllowedSites;
        public nint BlockedSites;
        public int notify_policy;
        public int is_default;
        public long revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqFocusProfileView
    {
        public nint id;
        public nint name;
        public int mode;
        public int planned_minutes;
        public int break_minutes;
        public nint allowed_apps;
        public nint blocked_apps;
        public nint allowed_sites;
        public nint blocked_sites;
        public int notify_policy;
        public int is_default;
        public long revision;
    }

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_focus_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_focus_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_start(IntPtr core, int mode, int plannedMinutes,
        string? goal, string? taskId, string? blockId, long now, out IntPtr handle,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_find(IntPtr core, string id, out IntPtr handle,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_open(IntPtr core, out IntPtr handle,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_pause(IntPtr core, string id, long now,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_resume(IntPtr core, string id, long now,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_complete(IntPtr core, string id, long now,
        string? note, int completionLevel, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_abandon(IntPtr core, string id, long now,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_history(IntPtr core, long from, long to, int limit,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_focus_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_focus_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_recover_interrupted(IntPtr core, long now,
        out int recovered, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_interruption_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_interruption_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_add_interruption(IntPtr core, string sessionId,
        long occurredAt, long durationMs, string? reason, string? source, string? handling,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_interruptions(IntPtr core, string sessionId,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_interruption_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_interruption_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_interruption_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_distraction_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_distraction_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_capture(IntPtr core, string content,
        string? sessionId, long now, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_pending_distractions(IntPtr core, int limit,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_resolve_distraction(IntPtr core, string id,
        int resolution, string? resolvedRef, out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_distraction_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_distraction_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_distraction_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_focus_profile_view(IntPtr handle);

    [LibraryImport("equora_capi")]
    internal static partial void eq_focus_profile_handle_destroy(IntPtr handle);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_profile_create(IntPtr core, in EqFocusProfileInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_profile_find(IntPtr core, string id,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_profile_update(IntPtr core, in EqFocusProfileInput input,
        out IntPtr handle, out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_focus_profile_delete(IntPtr core, string id,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_profile_list(IntPtr core, out IntPtr list,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_focus_profile_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_focus_profile_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_focus_profile_list_destroy(IntPtr list);
}
