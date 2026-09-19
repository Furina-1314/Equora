using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Xunit;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Tests;

/// <summary>P12b:智能规划 / 复盘 / 自动化全链路(经 C ABI)。</summary>
public class PlanningServiceTests : IDisposable
{
    private readonly AppDataService _service;

    public PlanningServiceTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p12-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private static DateTimeOffset NextMonday()
    {
        var today = DateTimeOffset.Now.ToLocalTime().Date;
        var daysAhead = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysAhead == 0) daysAhead = 7;
        return new DateTimeOffset(today.AddDays(daysAhead),
            TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now));
    }

    [Fact]
    public void PlanWeekProposesBlocksForEstimatedTasks()
    {
        var week = NextMonday();
        _service.CreateTask(new TaskDraft
        {
            Title = "排我",
            EstimateMinutes = 60,
            DueAt = week.AddDays(2),
        });

        var plan = _service.PlanWeek(week, week.AddDays(7));
        Assert.Single(plan);
        // 落在周一 09:00(本地工作时段首段)。
        Assert.Equal(week.AddHours(9), plan[0].Start);
        Assert.Equal(60, (int)(plan[0].End - plan[0].Start).TotalMinutes);
        Assert.NotEmpty(plan[0].Reason);
    }

    [Fact]
    public void DayLoadsAndSplits()
    {
        var week = NextMonday();
        _service.CreateBlock(null, week.AddHours(9), week.AddHours(12)); // 3h

        var loads = _service.DayLoads(week, week.AddDays(7));
        Assert.Contains(loads, l => l.PlannedMinutes == 180);

        _service.CreateTask(new TaskDraft { Title = "巨大", EstimateMinutes = 300 });
        var splits = _service.SuggestSplits(week, week.AddDays(7));
        Assert.Contains(splits, s => s.Title == "巨大" && s.Blocks == 3);
    }

    [Fact]
    public void DailyAndWeeklyReviewComputeAndSave()
    {
        var today = DateTimeOffset.Now.ToLocalTime();
        var task = _service.CreateTask(new TaskDraft { Title = "做完" });
        _service.UpdateTask(_service.GetTask(task.Id)!.Rebuild() with
        {
            Status = TaskStatus.Done,
            ActualMinutes = 40,
        });

        var daily = _service.ComputeDailyReview(today);
        Assert.Equal(1, daily.Completed);
        Assert.Equal(40, daily.ActualMinutes);

        _service.SaveDailyReview(today, "今天不错", "明天继续");
        // 再次保存幂等(覆盖更新)。
        _service.SaveDailyReview(today, "今天不错", "明天继续");

        var weekly = _service.ComputeWeeklyReview(
            today.AddDays(-((int)today.DayOfWeek + 6) % 7));
        Assert.True(weekly.DeepWorkMinutes >= 0);
        Assert.InRange(weekly.FocusRatio, 0, 1);
    }

    [Fact]
    public void AutomationLifecycleAndEvaluate()
    {
        var rule = _service.CreateAutomationRule("大任务提醒拆分",
            """{"type":"task_created"}""",
            """[{"type":"suggest_split"}]""",
            """{"estimate_gte":120}""");
        Assert.True(rule.Enabled);

        var small = _service.CreateTask(new TaskDraft { Title = "小", EstimateMinutes = 30 });
        Assert.Empty(_service.EvaluateAutomation("task_created", small.Id));

        var big = _service.CreateTask(new TaskDraft { Title = "大", EstimateMinutes = 180 });
        var hits = _service.EvaluateAutomation("task_created", big.Id);
        Assert.Equal(new[] { rule.Id }, hits);

        _service.SetAutomationEnabled(rule.Id, false);
        Assert.Empty(_service.EvaluateAutomation("task_created", big.Id));
        Assert.Empty(_service.ListAutomationRules()); // 只列启用中的

        _service.DeleteAutomationRule(rule.Id);
    }

    [Fact]
    public void EstimateCorrectionNeedsSamples()
    {
        var task = _service.CreateTask(new TaskDraft
        {
            Title = "重复任务",
            EstimateMinutes = 30,
        });
        // 无历史 → 无建议。
        Assert.Null(_service.CorrectEstimate(task.Id));
    }
}

file static class TaskDtoExtensions
{
    public static TaskDto Rebuild(this TaskDto dto) => dto;
}
