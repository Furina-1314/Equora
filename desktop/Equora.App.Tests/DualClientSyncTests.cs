using System.Collections.Concurrent;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

/// <summary>
/// 进程内传输:忠实实现服务端协议语义(与 server/SyncService 15 例测试同规则)。
/// 用它验证 C# 客户端编排层的线格式与结果分派 —— 双客户端端到端。
/// </summary>
public sealed class InProcessSyncServer : ISyncTransport
{
    private sealed record Entity(long Revision, bool Deleted, string Payload);

    private readonly ConcurrentDictionary<(string, string), Entity> _entities = new();
    private readonly ConcurrentDictionary<string, SyncWireOutcome> _processed = new();
    private readonly List<SyncWireChange> _changes = new();
    private readonly object _lock = new();
    private long _seq;

    public Task<SyncWireResponse> SyncAsync(string deviceId, long cursor,
        IReadOnlyList<SyncWireOperation> operations, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var outcomes = new List<SyncWireOutcome>();
            var newChanges = new List<SyncWireChange>();

            foreach (var op in operations)
            {
                if (_processed.TryGetValue(op.OperationId, out var prior))
                {
                    outcomes.Add(prior with { Result = "duplicate" });
                    continue;
                }

                var key = (op.EntityType, op.EntityId);
                _entities.TryGetValue(key, out var existing);
                var baseOk = existing is null
                    ? op.BaseRevision == 0
                    : op.BaseRevision == existing.Revision;

                SyncWireOutcome outcome;
                if (!baseOk)
                {
                    outcome = new SyncWireOutcome
                    {
                        OperationId = op.OperationId,
                        Result = "conflict",
                        ServerRevision = existing?.Revision ?? 0,
                        ServerDeleted = existing is { Deleted: true },
                        ServerPayload = existing?.Payload ?? "{}",
                    };
                }
                else
                {
                    var revision = (existing?.Revision ?? 0) + 1;
                    var deleted = op.Operation == "delete";
                    _entities[key] = new Entity(revision, deleted,
                        deleted ? "{}" : op.Payload);
                    outcome = new SyncWireOutcome
                    {
                        OperationId = op.OperationId,
                        Result = "accepted",
                        NewRevision = revision,
                    };
                    newChanges.Add(new SyncWireChange
                    {
                        Seq = ++_seq,
                        EntityType = op.EntityType,
                        EntityId = op.EntityId,
                        Revision = revision,
                        Kind = deleted ? "delete" : "update",
                        Payload = deleted ? "{}" : op.Payload,
                        DeviceId = deviceId,
                    });
                }

                _processed[op.OperationId] = outcome;
                outcomes.Add(outcome);
            }

            var changes = _changes.Where(c => c.Seq > cursor).ToList();
            _changes.AddRange(newChanges);

            return Task.FromResult(new SyncWireResponse
            {
                Outcomes = outcomes,
                Changes = changes,
                NextCursor = _seq,
            });
        }
    }
}

public sealed class DualClientSyncTests : IDisposable
{
    private readonly AppDataService _deviceA;
    private readonly AppDataService _deviceB;
    private readonly InProcessSyncServer _server = new();

    public DualClientSyncTests()
    {
        _deviceA = NewService("device-A");
        _deviceB = NewService("device-B");
    }

    private static AppDataService NewService(string deviceId) => new(NewDb(), deviceId);

    private static string NewDb() => Path.Combine(Path.GetTempPath(),
        $"equora-e2e-{Guid.NewGuid():N}.db");

    private SyncOrchestrator Orchestrator(AppDataService svc) =>
        new(svc.Core, _server);

    public void Dispose()
    {
        _deviceA.Dispose();
        _deviceB.Dispose();
    }

    [Fact]
    public async Task TwoDevicesOfflineCreateThenConverge()
    {
        var taskA = _deviceA.CreateTask(new TaskDraft { Title = "来自A" });
        _deviceA.OutboxEnqueue(Guid.NewGuid().ToString(), "task", taskA.Id, 0, 0,
            "{\"title\":\"来自A\"}");
        Assert.Equal(1, _deviceA.OutboxPendingCount());

        var taskB = _deviceB.CreateTask(new TaskDraft { Title = "来自B" });
        _deviceB.OutboxEnqueue(Guid.NewGuid().ToString(), "task", taskB.Id, 0, 0,
            "{\"title\":\"来自B\"}");

        var roundA = await Orchestrator(_deviceA).RunOnceAsync();
        Assert.True(roundA.Pushed == 1,
            $"Pushed={roundA.Pushed} Pending={_deviceA.OutboxPendingCount()}");
        Assert.Equal(0, _deviceA.OutboxPendingCount());

        var roundB = await Orchestrator(_deviceB).RunOnceAsync();
        Assert.Equal(1, roundB.Pushed);
        Assert.Equal(1, roundB.PullApplied);

        var roundA2 = await Orchestrator(_deviceA).RunOnceAsync();
        Assert.Equal(1, roundA2.PullApplied);
        Assert.Equal(_deviceB.LoadSyncState().Cursor, _deviceA.LoadSyncState().Cursor);
    }

    [Fact]
    public async Task ConcurrentEditRegistersConflictOnLoser()
    {
        var t = _deviceA.CreateTask(new TaskDraft { Title = "共享" });
        _deviceA.OutboxEnqueue(Guid.NewGuid().ToString(), "task", t.Id, 0, 0,
            "{\"title\":\"共享\"}");
        await Orchestrator(_deviceA).RunOnceAsync();

        _deviceA.OutboxEnqueue(Guid.NewGuid().ToString(), "task", t.Id, 1, 1,
            "{\"title\":\"A改的\"}");
        await Orchestrator(_deviceA).RunOnceAsync();

        _deviceB.OutboxEnqueue(Guid.NewGuid().ToString(), "task", t.Id, 1, 1,
            "{\"title\":\"B改的\"}");
        var roundB = await Orchestrator(_deviceB).RunOnceAsync();

        Assert.Equal(1, roundB.Conflicts);
        var conflicts = _deviceB.OpenSyncConflicts();
        var c = Assert.Single(conflicts);
        Assert.Equal("B改的", JsonText(c.LocalPayload, "title"));
        Assert.Equal("A改的", JsonText(c.ServerPayload, "title"));
        Assert.Equal(2, c.ServerRevision);

        _deviceB.ResolveSyncConflict(c.RowId, 2);
        Assert.Empty(_deviceB.OpenSyncConflicts());
    }

    [Fact]
    public async Task DuplicateRetryIsIdempotent()
    {
        var t = _deviceA.CreateTask(new TaskDraft { Title = "幂等" });
        var opId = Guid.NewGuid().ToString();
        _deviceA.OutboxEnqueue(opId, "task", t.Id, 0, 0, "{\"title\":\"幂等\"}");
        await Orchestrator(_deviceA).RunOnceAsync();

        _deviceA.OutboxEnqueue(opId, "task", t.Id, 0, 0, "{\"title\":\"幂等\"}");
        var round = await Orchestrator(_deviceA).RunOnceAsync();
        Assert.Equal(1, round.Pushed);
        Assert.Equal(0, _deviceA.OutboxPendingCount());
    }

    [Fact]
    public async Task BackoffSchedulesAfterTransportFailure()
    {
        var failing = new FailingTransport();
        var orchestrator = new SyncOrchestrator(_deviceA.Core, failing);

        var t = _deviceA.CreateTask(new TaskDraft { Title = "待推" });
        _deviceA.OutboxEnqueue(Guid.NewGuid().ToString(), "task", t.Id, 0, 0,
            "{\"title\":\"待推\"}");
        Assert.Equal(1, _deviceA.OutboxPendingCount());

        var delay1 = orchestrator.NextRetryDelayMs(1);
        var delay5 = orchestrator.NextRetryDelayMs(5);
        Assert.InRange(delay1, 1000, 2000);
        Assert.True(delay5 > delay1);
        Assert.True(delay5 <= 600_000);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => orchestrator.RunOnceAsync());
        Assert.Equal(1, _deviceA.OutboxPendingCount());
    }

    private static string JsonText(string json, string property)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(property).GetString() ?? "";
    }

    private sealed class FailingTransport : ISyncTransport
    {
        public Task<SyncWireResponse> SyncAsync(string deviceId, long cursor,
            IReadOnlyList<SyncWireOperation> operations, CancellationToken ct = default) =>
            throw new HttpRequestException("network down");
    }
}
