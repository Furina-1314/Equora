using Equora.App.NativeInterop;


namespace Equora.App.Services;

/// <summary>P14:同步客户端操作转发(Outbox/状态/冲突)。</summary>
public sealed partial class AppDataService
{
    public void OutboxEnqueue(string operationId, string entityType, string entityId,
        long baseRevision, int kind, string payload) =>
        _core.OutboxEnqueue(operationId, entityType, entityId, baseRevision, kind, payload);
    public IReadOnlyList<OutboxEntryDto> OutboxDuePending(int limit = 64) =>
        _core.OutboxDuePending(limit);
    public void OutboxMarkSending(long rowId) => _core.OutboxMarkSending(rowId);
    public void OutboxMarkResult(long rowId, bool accepted, int maxAttempts = 10) =>
        _core.OutboxMarkResult(rowId, accepted, maxAttempts);
    public void OutboxResetStuck() => _core.OutboxResetStuck();
    public int OutboxPendingCount() => _core.OutboxPendingCount();

    public SyncStateDto LoadSyncState() => _core.LoadSyncState();
    public void SaveSyncCursor(long cursor) => _core.SaveSyncCursor(cursor);
    public void SaveSyncUrl(string url) => _core.SaveSyncUrl(url);

    public void AddSyncConflict(string operationId, string entityType, string entityId,
        string localPayload, string serverPayload, long serverRevision,
        bool serverDeleted) =>
        _core.AddSyncConflict(operationId, entityType, entityId, localPayload,
            serverPayload, serverRevision, serverDeleted);
    public IReadOnlyList<SyncConflictDto> OpenSyncConflicts() =>
        _core.OpenSyncConflicts();
    public void ResolveSyncConflict(long rowId, int resolution) =>
        _core.ResolveSyncConflict(rowId, resolution);

    /// <summary>暴露核心句柄供编排器使用(同进程内)。</summary>
    internal EquoraCore Core => _core;
}
