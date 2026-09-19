using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Tests;

/// <summary>P10:专注视图模型 + 前台应用规则匹配。</summary>
public class FocusViewModelTests : IDisposable
{
    private readonly AppDataService _service;
    private readonly FocusViewModel _vm;

    public FocusViewModelTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
        _vm = new FocusViewModel(_service, _service);
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p10-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private static readonly DateTimeOffset T0 =
        new(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void StartPauseCompleteAndTodayStats()
    {
        _vm.Clock = T0;
        _vm.ModeIndex = 0; // 番茄
        _vm.PlannedMinutes = 25;
        _vm.Goal = "写方案";
        _vm.Start();

        Assert.NotNull(_vm.Session);
        Assert.True(_vm.IsRunning);
        Assert.Contains("计划 25 分钟", _vm.StatusText);

        _vm.Clock = T0.AddMinutes(10);
        _vm.Pause();
        Assert.Equal(SessionStateDto.Paused, _vm.Session!.State);

        _vm.Clock = T0.AddMinutes(15);
        _vm.Resume();
        _vm.Clock = T0.AddMinutes(30);
        _vm.Complete();

        Assert.Null(_vm.Session);
        Assert.False(_vm.IsRunning);
        Assert.Contains("有效专注 25 分钟", _vm.StatusText); // 30 - 5 暂停
        Assert.Contains("今日累计 25 分钟", _vm.StatusText);
        Assert.Null(_service.OpenFocus());
    }

    [Fact]
    public void LinkedTaskStatusTransitions()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "专注对象" });
        _vm.Clock = T0;
        _vm.SelectedTaskId = task.Id;
        _vm.ModeIndex = 2; // Flowtime(不需要时长)
        _vm.Start();

        Assert.Equal(TaskStatus.InProgress, _service.GetTask(task.Id)!.Status);

        _vm.Clock = T0.AddMinutes(12);
        _vm.Complete();
        Assert.Equal(TaskStatus.Planned, _service.GetTask(task.Id)!.Status);
    }

    [Fact]
    public void CaptureAndResolveAsTask()
    {
        _vm.Clock = T0;
        _vm.ModeIndex = 2;
        _vm.Start();

        var d = _vm.Capture("查一下参考价格");
        Assert.NotNull(d);
        Assert.Single(_vm.Pending);
        Assert.Contains(d!, _service.PendingDistractions()); // 已即时落库

        var task = _vm.ResolveAsTask(d!);
        Assert.NotNull(task);
        Assert.Equal("查一下参考价格", task!.Title);
        Assert.Equal(TaskStatus.Inbox, _service.GetTask(task.Id)!.Status);
        Assert.Empty(_vm.Pending); // 从待整理移除
        Assert.Empty(_service.PendingDistractions());
    }

    [Fact]
    public void BlockedAppNudgeRecordsInterruption()
    {
        _vm.Clock = T0;
        _vm.ModeIndex = 2;
        _vm.Start();
        var sessionId = _vm.Session!.Id;

        _vm.ReportBlockedApp("game.exe");
        Assert.Contains("game.exe", _vm.NudgeText);
        var interruptions = _service.InterruptionsOf(sessionId);
        Assert.Single(interruptions);
        Assert.Equal("app-switch", interruptions[0].Source);
        Assert.Equal("game.exe", interruptions[0].Reason);

        _vm.ClearNudge();
        Assert.Equal("", _vm.NudgeText);
    }

    [Fact]
    public void ApplyProfileSetsModeAndDuration()
    {
        var profile = _service.CreateFocusProfile("编程", FocusModeDto.Deep, 120, 15);
        _vm.Load(); // 载入预设列表
        _vm.ApplyProfile(_vm.Profiles.Single(p => p.Id == profile.Id));

        Assert.Equal(1, _vm.ModeIndex); // Deep
        Assert.Equal(120, _vm.PlannedMinutes);
    }
}

public class AppRuleMatcherTests
{
    [Fact]
    public void BlockedListMatchesWithOrWithoutExtension()
    {
        var blocked = new[] { "game.exe", "steam" };
        Assert.True(AppRuleMatcher.IsBlocked("game.exe", Array.Empty<string>(), blocked));
        Assert.True(AppRuleMatcher.IsBlocked("Game", Array.Empty<string>(), blocked)); // 大小写不敏感
        Assert.True(AppRuleMatcher.IsBlocked("Steam.exe", Array.Empty<string>(), blocked));
        Assert.False(AppRuleMatcher.IsBlocked("msedge.exe", Array.Empty<string>(), blocked));
    }

    [Fact]
    public void EmptyBlockedListMeansNoRestriction()
    {
        Assert.False(AppRuleMatcher.IsBlocked("anything.exe", Array.Empty<string>(),
            Array.Empty<string>()));
    }

    [Fact]
    public void AllowlistWinsOverBlocklist()
    {
        var blocked = new[] { "devenv.exe" };
        Assert.True(AppRuleMatcher.IsBlocked("devenv.exe", Array.Empty<string>(), blocked));
        Assert.False(AppRuleMatcher.IsBlocked("devenv.exe", new[] { "devenv.exe" }, blocked));
    }

    [Fact]
    public void NoSubstringFalsePositive()
    {
        var blocked = new[] { "note.exe" };
        Assert.False(AppRuleMatcher.IsBlocked("notepad.exe", Array.Empty<string>(), blocked));
    }
}
