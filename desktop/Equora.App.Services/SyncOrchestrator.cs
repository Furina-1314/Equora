using Equora.App.NativeInterop;
using Equora.App.Services;

namespace Equora.App.Services;

/// <summary>
/// 同步编排:Push(Outbox→服务器)→ Pull(变更流→游标推进)。
/// 结果分派:accepted/duplicate 清队;conflict 登记冲突中心并保留条目待人工处理;
/// 传输异常由调用方按退避重试(本类不自动定时,UI 决定触发时机)。
/// </summary>
public sealed class SyncOrchestrator
{
    private readonly EquoraCore _core;
    private readonly ISyncTransport _transport;
    private readonly Random _random = new();

    public SyncOrchestrator(EquoraCore core, ISyncTransport transport)
    {
        _core = core;
        _transport = transport;
    }

    public string DeviceId => _core.DeviceId;

    /// <summary>执行一轮 Push+Pull;返回本轮统计。</summary>
    public async Task<SyncRoundResult> RunOnceAsync(CancellationToken ct = default)
    {
        // 崩溃恢复:上次 Sending 中断的条目回队。
        _core.OutboxResetStuck();

        var state = _core.LoadSyncState();
        var pending = _core.OutboxDuePending();

        var ops = pending.Select(e => new SyncWireOperation
        {
            OperationId = e.OperationId,
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            BaseRevision = e.BaseRevision,
            Operation = e.Kind switch { 0 => "create", 2 => "delete", _ => "update" },
            Payload = e.Payload,
        }).ToList();

        foreach (var e in pending)
        {
            _core.OutboxMarkSending(e.RowId);
        }

        SyncWireResponse response;
        try
        {
            response = await _transport.SyncAsync(state.DeviceId.Length > 0
                ? state.DeviceId : _core.DeviceId, state.Cursor, ops, ct);
        }
        catch
        {
            // 传输失败:条目回退待重试(退避由调用方调度)。
            foreach (var e in pending)
            {
                _core.OutboxMarkResult(e.RowId, accepted: false);
            }
            throw;
        }

        var result = new SyncRoundResult();
        var byOpId = pending.ToDictionary(e => e.OperationId);

        foreach (var outcome in response.Outcomes)
        {
            if (!byOpId.TryGetValue(outcome.OperationId, out var entry))
            {
                continue;
            }
            switch (outcome.Result)
            {
                case "accepted":
                case "duplicate":
                    _core.OutboxMarkResult(entry.RowId, accepted: true);
                    result.Pushed++;
                    break;
                case "conflict":
                    _core.AddSyncConflict(outcome.OperationId, entry.EntityType,
                        entry.EntityId, entry.Payload, outcome.ServerPayload,
                        outcome.ServerRevision, outcome.ServerDeleted);
                    _core.OutboxMarkResult(entry.RowId, accepted: false);
                    result.Conflicts++;
                    break;
                default: // invalid
                    _core.OutboxMarkResult(entry.RowId, accepted: false);
                    result.Rejected++;
                    break;
            }
        }

        // Pull:应用远端变更(当前版本仅推进游标 + 交给上层应用的回调;冲突变更
        // 不静默覆盖 —— 已在本轮 outcomes 之外由 conflict 分支处理本地侧)。
        result.PullApplied = response.Changes.Count;
        if (response.NextCursor > state.Cursor)
        {
            _core.SaveSyncCursor(response.NextCursor);
        }
        return result;
    }

    /// <summary>传输失败后的退避延迟(毫秒),抖动 [0,1)。</summary>
    public long NextRetryDelayMs(int failedAttempts) =>
        EquoraCore.BackoffDelayMs(failedAttempts, jitter: _random.NextDouble());
}

public sealed class SyncRoundResult
{
    public int Pushed { get; set; }
    public int Conflicts { get; set; }
    public int Rejected { get; set; }
    public int PullApplied { get; set; }
}
