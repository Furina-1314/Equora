using System.Runtime.InteropServices;
namespace Equora.App.NativeInterop;
public sealed record OutboxEntryDto
{
    public required long RowId { get; init; }
    public required string OperationId { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public long BaseRevision { get; init; }
    public int Kind { get; init; }
    public string Payload { get; init; } = "{}";
    public int Status { get; init; }
    public int Attempts { get; init; }
}
public sealed record SyncStateDto
{
    public long Cursor { get; init; }
    public string ServerUrl { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public long LastSuccessAt { get; init; }
}
public sealed record SyncConflictDto
{
    public required long RowId { get; init; }
    public required string OperationId { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string LocalPayload { get; init; } = "{}";
    public string ServerPayload { get; init; } = "{}";
    public long ServerRevision { get; init; }
    public bool ServerDeleted { get; init; }
    public int Resolution { get; init; }
}
/// <summary>P14:同步客户端操作(Outbox/状态/冲突,经 C ABI)。</summary>
public sealed partial class EquoraCore
{
    private static long MsNow => DateTimeOffset.Now.ToUnixTimeMilliseconds();
    public void OutboxEnqueue(string operationId, string entityType, string entityId,
        long baseRevision, int kind, string payload)
    {
        using var opId = new Utf8NativeString(operationId);
        using var et = new Utf8NativeString(entityType);
        using var eid = new Utf8NativeString(entityId);
        using var pl = new Utf8NativeString(payload.Length == 0 ? "{}" : payload);
        var input = new NativeMethods.EqOutboxInput
        {
            operation_id = opId.Pointer,
            entity_type = et.Pointer,
            entity_id = eid.Pointer,
            base_revision = baseRevision,
            kind = kind,
            payload = pl.Pointer,
        };
        var rc = NativeMethods.eq_outbox_enqueue(_core, in input, out var error);
        EquoraException.ThrowIfFailed(rc, error, "outbox_enqueue");
    }
    public IReadOnlyList<OutboxEntryDto> OutboxDuePending(int limit = 64)
    {
        var rc = NativeMethods.eq_outbox_due_pending(_core, MsNow, limit, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "outbox_due_pending");
        try
        {
            var result = new List<OutboxEntryDto>();
            var count = NativeMethods.eq_outbox_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_outbox_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqOutboxView>(ptr);
                result.Add(new OutboxEntryDto
                {
                    RowId = v.row_id,
                    OperationId = v.operation_id.Str() ?? "",
                    EntityType = v.entity_type.Str() ?? "",
                    EntityId = v.entity_id.Str() ?? "",
                    BaseRevision = v.base_revision,
                    Kind = v.kind,
                    Payload = v.payload.Str() ?? "{}",
                    Status = v.status,
                    Attempts = v.attempts,
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_outbox_list_destroy(list);
        }
    }
    public void OutboxMarkSending(long rowId)
    {
        var rc = NativeMethods.eq_outbox_mark_sending(_core, rowId, out var error);
        EquoraException.ThrowIfFailed(rc, error, "outbox_mark_sending");
    }
    public void OutboxMarkResult(long rowId, bool accepted, int maxAttempts = 10)
    {
        var rc = NativeMethods.eq_outbox_mark_result(_core, rowId, accepted ? 1 : 0,
            MsNow, maxAttempts, out var error);
        EquoraException.ThrowIfFailed(rc, error, "outbox_mark_result");
    }
    public void OutboxResetStuck()
    {
        var rc = NativeMethods.eq_outbox_reset_stuck(_core, out var error);
        EquoraException.ThrowIfFailed(rc, error, "outbox_reset_stuck");
    }
    public int OutboxPendingCount()
    {
        var rc = NativeMethods.eq_outbox_pending_count(_core, out var count,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "outbox_pending_count");
        return count;
    }
    public SyncStateDto LoadSyncState()
    {
        var rc = NativeMethods.eq_sync_state(_core, out var v, out var error);
        EquoraException.ThrowIfFailed(rc, error, "sync_state");
        return new SyncStateDto
        {
            Cursor = v.cursor,
            ServerUrl = v.server_url.Str() ?? "",
            DeviceId = v.device_id.Str() ?? "",
            LastSuccessAt = v.last_success_at,
        };
    }
    public void SaveSyncCursor(long cursor)
    {
        var rc = NativeMethods.eq_sync_save_cursor(_core, cursor, MsNow, out var error);
        EquoraException.ThrowIfFailed(rc, error, "sync_save_cursor");
    }
    public void SaveSyncUrl(string url)
    {
        var rc = NativeMethods.eq_sync_save_url(_core, url, out var error);
        EquoraException.ThrowIfFailed(rc, error, "sync_save_url");
    }
    public void AddSyncConflict(string operationId, string entityType, string entityId,
        string localPayload, string serverPayload, long serverRevision, bool serverDeleted)
    {
        var rc = NativeMethods.eq_sync_conflict_add(_core, operationId, entityType,
            entityId, localPayload, serverPayload, serverRevision, serverDeleted ? 1 : 0,
            MsNow, out var error);
        EquoraException.ThrowIfFailed(rc, error, "sync_conflict_add");
    }
    public IReadOnlyList<SyncConflictDto> OpenSyncConflicts()
    {
        var rc = NativeMethods.eq_sync_conflict_open(_core, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "sync_conflict_open");
        try
        {
            var result = new List<SyncConflictDto>();
            var count = NativeMethods.eq_sync_conflict_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_sync_conflict_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqSyncConflictView>(ptr);
                result.Add(new SyncConflictDto
                {
                    RowId = v.row_id,
                    OperationId = v.operation_id.Str() ?? "",
                    EntityType = v.entity_type.Str() ?? "",
                    EntityId = v.entity_id.Str() ?? "",
                    LocalPayload = v.local_payload.Str() ?? "{}",
                    ServerPayload = v.server_payload.Str() ?? "{}",
                    ServerRevision = v.server_revision,
                    ServerDeleted = v.server_deleted != 0,
                    Resolution = v.resolution,
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_sync_conflict_list_destroy(list);
        }
    }
    public void ResolveSyncConflict(long rowId, int resolution)
    {
        var rc = NativeMethods.eq_sync_conflict_resolve(_core, rowId, resolution,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "sync_conflict_resolve");
    }
    public static long BackoffDelayMs(int attempts, long baseMs = 1000,
        long maxMs = 300_000, double jitter = 0.0)
    {
        _ = NativeMethods.eq_sync_backoff_delay(attempts, baseMs, maxMs, jitter,
            out var delay);
        return delay;
    }
}
