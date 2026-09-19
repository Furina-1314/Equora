using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EqOutboxInput
    {
        public nint operation_id;
        public nint entity_type;
        public nint entity_id;
        public long base_revision;
        public int kind;
        public nint payload;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqOutboxView
    {
        public long row_id;
        public nint operation_id;
        public nint entity_type;
        public nint entity_id;
        public long base_revision;
        public int kind;
        public nint payload;
        public int status;
        public int attempts;
        public long next_attempt_at;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqSyncStateView
    {
        public long cursor;
        public nint server_url;
        public nint device_id;
        public long last_success_at;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EqSyncConflictView
    {
        public long row_id;
        public nint operation_id;
        public nint entity_type;
        public nint entity_id;
        public nint local_payload;
        public nint server_payload;
        public long server_revision;
        public int server_deleted;
        public long created_at;
        public int resolution;
    }

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_outbox_enqueue(IntPtr core, in EqOutboxInput input,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_outbox_due_pending(IntPtr core, long nowMs, int limit,
        out IntPtr list, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_outbox_mark_sending(IntPtr core, long rowId,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_outbox_mark_result(IntPtr core, long rowId,
        int accepted, long nowMs, int maxAttempts, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_outbox_reset_stuck(IntPtr core, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_outbox_pending_count(IntPtr core, out int count,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_outbox_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_outbox_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_outbox_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_sync_state(IntPtr core, out EqSyncStateView state,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_sync_save_cursor(IntPtr core, long cursor, long nowMs,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_sync_save_url(IntPtr core, string url,
        out EqError error);

    [LibraryImport("equora_capi", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int eq_sync_conflict_add(IntPtr core, string operationId,
        string entityType, string entityId, string? localPayload, string? serverPayload,
        long serverRevision, int serverDeleted, long nowMs, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_sync_conflict_open(IntPtr core, out IntPtr list,
        out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_sync_conflict_resolve(IntPtr core, long rowId,
        int resolution, out EqError error);

    [LibraryImport("equora_capi")]
    internal static partial int eq_sync_conflict_list_count(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial IntPtr eq_sync_conflict_list_get(IntPtr list, int index);

    [LibraryImport("equora_capi")]
    internal static partial void eq_sync_conflict_list_destroy(IntPtr list);

    [LibraryImport("equora_capi")]
    internal static partial int eq_sync_backoff_delay(int attempts, long baseMs,
        long maxMs, double jitter, out long delayMs);
}
