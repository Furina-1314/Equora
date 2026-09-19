using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Tests;

/// <summary>P8:自然语言快速收集解析(预览语义)。</summary>
public class QuickCaptureParserTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 19, 10, 0, 0, TimeSpan.FromHours(8)); // 周六

    private static CaptureDraft Parse(string text) => QuickCaptureParser.Parse(text, Now);

    [Fact]
    public void PlainTextBecomesTitleVerbatim()
    {
        var draft = Parse("买牛奶");
        Assert.Equal("买牛奶", draft.Title);
        Assert.Null(draft.DueAt);
        Assert.Empty(draft.Tags);
        Assert.Equal(2, draft.PriorityValue);
        Assert.Equal("作为普通任务收集", draft.Preview());
    }

    [Fact]
    public void TomorrowAfternoonThreePmForOneHour()
    {
        var draft = Parse("明天下午3点开会一小时");
        Assert.Equal("开会", draft.Title);
        Assert.NotNull(draft.DueAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 15, 0, 0, Now.Offset),
            draft.DueAt);
        Assert.True(draft.HasExplicitTime);
        Assert.Equal(60, draft.EstimateMinutes);
    }

    [Fact]
    public void EveryFridayFivePmWeeklyReport()
    {
        var draft = Parse("每周五17点提醒写周报");
        Assert.Equal("提醒写周报", draft.Title);
        Assert.NotNull(draft.DueAt);
        Assert.Equal(17, draft.DueAt!.Value.Hour);
        Assert.Equal("每周五", draft.RecurrenceText);
    }

    [Fact]
    public void FortyFiveMinutesTonight()
    {
        var draft = Parse("今晚安排45分钟复习电路");
        Assert.Equal("安排复习电路", draft.Title);
        Assert.Equal(45, draft.EstimateMinutes);
        Assert.NotNull(draft.DueAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 0, 0, 0, Now.Offset),
            draft.DueAt); // 今晚 = 今天
    }

    [Fact]
    public void NextWeekDeadlineWithTagsAndPriority()
    {
        var draft = Parse("下周三前完成设计稿 #产品 #评审 !高");
        Assert.Equal("前完成设计稿", draft.Title);
        Assert.Equal(new[] { "产品", "评审" }, draft.Tags);
        Assert.Equal(3, draft.PriorityValue);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 0, 0, 0, Now.Offset),
            draft.DueAt); // 周六的下周三 = 9-23
    }

    [Fact]
    public void HalfPastAndHalfHourForms()
    {
        var draft = Parse("明晚8点半跑步半小时");
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 20, 30, 0, Now.Offset),
            draft.DueAt);
        Assert.Equal(30, draft.EstimateMinutes);
        Assert.Equal("跑步", draft.Title);
    }

    [Fact]
    public void ProjectAtAndColonTime()
    {
        var draft = Parse("和 @设计 组对齐 14:30 讨论方案");
        Assert.Equal("设计", draft.ProjectName);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 14, 30, 0, Now.Offset),
            draft.DueAt);
    }

    [Fact]
    public void PastTimeRollsToTomorrow()
    {
        // now=10:00,说"9点"(已过)→ 明天 9 点。
        var draft = Parse("9点站会");
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 9, 0, 0, Now.Offset),
            draft.DueAt);
    }

    [Fact]
    public void UnrecognizedTextIsNeverLost()
    {
        var weird = "zzz???!!! ### 123";
        var draft = Parse(weird);
        // 无法识别的内容保留在标题里。
        Assert.Contains("zzz", draft.Title);
        Assert.Contains("123", draft.Title);
    }

    [Fact]
    public void SpansCoverHighlightForPreview()
    {
        var draft = Parse("明天下午3点开会一小时 #产品");
        Assert.NotEmpty(draft.Spans);
        Assert.Contains(draft.Spans, s => s.Kind == CaptureKind.Date);
        Assert.Contains(draft.Spans, s => s.Kind == CaptureKind.TimeOfDay);
        Assert.Contains(draft.Spans, s => s.Kind == CaptureKind.Duration);
        Assert.Contains(draft.Spans, s => s.Kind == CaptureKind.Tag);
    }
}

/// <summary>P8:四象限视图模型。</summary>
public class MatrixViewModelTests : IDisposable
{
    private readonly AppDataService _service;
    private readonly UndoService _undo;
    private readonly MatrixViewModel _vm;

    public MatrixViewModelTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
        _undo = new UndoService();
        _vm = new MatrixViewModel(_service, _service, _undo);
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p8-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private static readonly DateTimeOffset Ref =
        new(2026, 9, 19, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void QuadrantClassificationIsExplainable()
    {
        var q1 = _service.CreateTask(new TaskDraft
        {
            Title = "重要紧急",
            Importance = 4,
            DueAt = Ref.AddDays(1),
        });
        var q2 = _service.CreateTask(new TaskDraft
        {
            Title = "重要不紧急",
            Importance = 5,
            DueAt = Ref.AddDays(10),
        });
        var q3 = _service.CreateTask(new TaskDraft { Title = "紧急不重要", DueAt = Ref.AddDays(2) });
        var q4 = _service.CreateTask(new TaskDraft { Title = "其他" });

        Assert.Equal(Quadrant.First, MatrixViewModel.QuadrantOf(
            _service.GetTask(q1.Id)!, Ref));
        Assert.Equal(Quadrant.Second, MatrixViewModel.QuadrantOf(
            _service.GetTask(q2.Id)!, Ref));
        Assert.Equal(Quadrant.Third, MatrixViewModel.QuadrantOf(
            _service.GetTask(q3.Id)!, Ref));
        Assert.Equal(Quadrant.Fourth, MatrixViewModel.QuadrantOf(
            _service.GetTask(q4.Id)!, Ref));

        _vm.Refresh(Ref);
        Assert.Single(_vm.Q1);
        Assert.Single(_vm.Q2);
        Assert.Single(_vm.Q3);
        Assert.Single(_vm.Q4);
    }

    [Fact]
    public void DragToQuadrantChangesFieldsAndUndoRestores()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "移动我" });
        _vm.Refresh(Ref);

        _vm.MoveToQuadrant(_service.GetTask(task.Id)!, Quadrant.First);
        var moved = _service.GetTask(task.Id)!;
        Assert.True(moved.Importance >= MatrixViewModel.ImportantThreshold);
        Assert.NotNull(moved.DueAt); // Q1 需要紧急性

        _vm.MoveToQuadrant(moved, Quadrant.Second);
        var q2 = _service.GetTask(task.Id)!;
        Assert.True(q2.Importance >= MatrixViewModel.ImportantThreshold);
        Assert.Null(q2.DueAt); // Q2 清除近程截止

        _vm.Undo();
        var undone = _service.GetTask(task.Id)!;
        Assert.NotNull(undone.DueAt); // 回到 Q1 状态
    }

    [Fact]
    public void BigThreeCapsAtThreeFromFirstOrSecondQuadrant()
    {
        var a = _service.CreateTask(new TaskDraft { Title = "A", Importance = 5 });
        var b = _service.CreateTask(new TaskDraft { Title = "B", Importance = 5 });
        var c = _service.CreateTask(new TaskDraft { Title = "C", Importance = 5 });
        var d = _service.CreateTask(new TaskDraft { Title = "D", Importance = 5 });
        _vm.Refresh(Ref);

        Assert.Equal("已设为今日要事:A", _vm.ToggleBigThree(_service.GetTask(a.Id)!));
        _vm.ToggleBigThree(_service.GetTask(b.Id)!);
        _vm.ToggleBigThree(_service.GetTask(c.Id)!);
        Assert.Equal(3, _vm.BigThree.Count);

        var refused = _vm.ToggleBigThree(_service.GetTask(d.Id)!);
        Assert.Contains("已满", refused);
        Assert.Equal(3, _vm.BigThree.Count);

        _vm.ToggleBigThree(_service.GetTask(a.Id)!); // 再点 = 移除
        Assert.Equal(2, _vm.BigThree.Count);
    }

    [Fact]
    public void CapacitySumsEstimates()
    {
        _service.CreateTask(new TaskDraft
        {
            Title = "Q1 任务",
            Importance = 4,
            DueAt = Ref.AddDays(1),
            EstimateMinutes = 90,
        });
        _service.CreateTask(new TaskDraft
        {
            Title = "Q2 任务",
            Importance = 4,
            DueAt = Ref.AddDays(10),
            EstimateMinutes = 120,
        });
        _vm.Refresh(Ref);
        Assert.Equal(90, _vm.Q1Minutes);
        Assert.Equal(120, _vm.Q2Minutes);
    }
}
