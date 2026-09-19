using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

/// <summary>P9:专注会话全链路(经 C ABI)。</summary>
public class FocusServiceTests : IDisposable
{
    private readonly AppDataService _service;

    public FocusServiceTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p9-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private static readonly DateTimeOffset T0 =
        new(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void LifecycleThroughAbi()
    {
        var started = _service.StartFocus(FocusModeDto.Pomodoro, 25, "写周报", now: T0);
        Assert.Equal(SessionStateDto.Running, started.State);
        Assert.Equal(25, (int)(started.PlannedEnd - started.PlannedStart)!.Value.TotalMinutes);
        Assert.Null(started.ActualEnd);

        // 唯一开放会话。
        var ex = Assert.Throws<EquoraException>(
            () => _service.StartFocus(FocusModeDto.Deep, 60, now: T0.AddMinutes(1)));
        Assert.Equal(4, ex.Code);

        Assert.Equal(started.Id, _service.OpenFocus()!.Id);

        var paused = _service.PauseFocus(started.Id, T0.AddMinutes(10));
        Assert.Equal(SessionStateDto.Paused, paused.State);

        var resumed = _service.ResumeFocus(started.Id, T0.AddMinutes(15));
        Assert.Equal(5, (int)resumed.Paused.TotalMinutes);

        var done = _service.CompleteFocus(started.Id, "初稿完成", 85,
            T0.AddMinutes(40));
        Assert.Equal(SessionStateDto.Completed, done.State);
        Assert.Equal(35, (int)done.Effective().TotalMinutes); // 40 - 5 暂停
        Assert.Equal(85, done.CompletionLevel);
        Assert.Null(_service.OpenFocus());
    }

    [Fact]
    public void LinkedToTaskAndHistoryQuery()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "深度任务" });
        var s = _service.StartFocus(FocusModeDto.Deep, 90, "g", taskId: task.Id, now: T0);
        Assert.Equal(task.Id, s.TaskId);
        _service.CompleteFocus(s.Id, now: T0.AddMinutes(45));

        var history = _service.FocusHistory(T0, T0.AddDays(1));
        Assert.Single(history);
        Assert.Equal(FocusModeDto.Deep, history[0].Mode);
    }

    [Fact]
    public void InterruptionsAndDistractionCapture()
    {
        var s = _service.StartFocus(FocusModeDto.Pomodoro, 25, now: T0);
        _service.AddInterruption(s.Id, T0.AddMinutes(5), TimeSpan.FromMinutes(1),
            "查消息", "manual", "返回");

        // 分心捕获:即时落库,即使随后"崩溃"也能恢复。
        var d = _service.CaptureDistraction("查 XPath 语法", s.Id, T0.AddMinutes(6));
        Assert.Equal("查 XPath 语法", d.Content);

        var pending = _service.PendingDistractions();
        Assert.Single(pending);

        // 整理为任务(resolution=1)。
        var resolved = _service.ResolveDistraction(d.Id, 1, "task-id-x");
        Assert.Equal(1, resolved.Resolution);
        Assert.Empty(_service.PendingDistractions());

        var interruptions = _service.InterruptionsOf(s.Id);
        Assert.Single(interruptions);
        Assert.Equal("查消息", interruptions[0].Reason);
    }

    [Fact]
    public void CrashRecoveryClosesOrphan()
    {
        var s = _service.StartFocus(FocusModeDto.Flowtime, 0, now: T0);
        _service.AddInterruption(s.Id, T0.AddMinutes(20), TimeSpan.FromSeconds(30),
            "走神", "manual", "返回"); // 推进最后活动时刻

        var recovered = _service.RecoverFocusSessions();
        Assert.Equal(1, recovered);

        var closed = _service.FindFocus(s.Id)!;
        Assert.Equal(SessionStateDto.Abandoned, closed.State);
        Assert.Equal(T0.AddMinutes(20), closed.ActualEnd);

        // 恢复后可再开新会话。
        var next = _service.StartFocus(FocusModeDto.Pomodoro, 25, now: T0.AddDays(1));
        Assert.Equal(SessionStateDto.Running, next.State);
    }

    [Fact]
    public void ProfileCrudThroughAbi()
    {
        var created = _service.CreateFocusProfile("写作", FocusModeDto.Deep, 90, 15,
            allowedApps: "[\"word.exe\"]", isDefault: true);
        Assert.True(created.IsDefault);
        Assert.Equal("[]", created.BlockedApps);

        var conflict = Assert.Throws<EquoraException>(
            () => _service.CreateFocusProfile("写作", FocusModeDto.Pomodoro, 25));
        Assert.Equal(4, conflict.Code);

        var updated = _service.UpdateFocusProfile(created with { PlannedMinutes = 120 });
        Assert.Equal(2, updated.Revision);
        Assert.Equal(120, updated.PlannedMinutes);

        Assert.Single(_service.ListFocusProfiles());
        _service.DeleteFocusProfile(created.Id);
        Assert.Null(_service.FindFocusProfile(created.Id));
    }
}
