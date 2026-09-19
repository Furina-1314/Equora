using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Xunit;

namespace Equora.App.Tests;

/// <summary>P6 日历服务全链路(经 C ABI)。</summary>
public class CalendarServiceTests : IDisposable
{
    private readonly AppDataService _service;

    public CalendarServiceTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p6-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private static readonly DateTimeOffset Friday =
        DateTimeOffset.FromUnixTimeMilliseconds(1_789_689_600_000); // 2026-09-18T00:00Z

    [Fact]
    public void CalendarCreateAndList()
    {
        var created = _service.CreateCalendar("工作", "#3366CC");
        Assert.Equal("工作", created.Name);
        var list = _service.ListCalendars();
        Assert.Single(list);
    }

    [Fact]
    public void BlockCrudLinkedToTask()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "要排期的任务" });
        var start = Friday.AddHours(9);
        var block = _service.CreateBlock(task.Id, start, start.AddHours(2), "深度工作");

        Assert.Equal(task.Id, block.TaskId);
        Assert.Equal(2 * 60, (int)(block.EndAt - block.StartAt).TotalMinutes);

        var moved = _service.UpdateBlock(block with
        {
            EndAt = block.EndAt.AddHours(1),
        });
        Assert.Equal(2, moved.Revision);
        Assert.Single(_service.BlocksForTask(task.Id));

        _service.DeleteBlock(block.Id);
        Assert.Empty(_service.BlocksInRange(Friday, Friday.AddDays(1)));
        // 任务本体不受影响(时间块删除 ≠ 任务删除)。
        Assert.NotNull(_service.GetTask(task.Id));
    }

    [Fact]
    public void EventSeriesWindowConflictsAndDetach()
    {
        // 每周五 10:00 例会(与自身种子同日)。
        var start = Friday.AddHours(10);
        var ev = _service.CreateEvent("周例会", start, start.AddHours(1));
        var rule = _service.CreateRule("event", ev.Id, RecurFreqDto.Weekly,
            byWeekday: new[] { 4 });
        Assert.Equal("FREQ=WEEKLY;BYDAY=FR", rule.RruleText);

        // 窗口物化:宿主事件 + 非种子的 3 个周五实例(种子去重)。
        var spans = _service.WindowSpans(Friday, Friday.AddDays(28), 0);
        Assert.Equal(4, spans.Count);

        // 构造冲突:第二周五 10:30 的块。
        var clashStart = Friday.AddDays(7).AddHours(10).AddMinutes(30);
        _service.CreateBlock(null, clashStart, clashStart.AddMinutes(30));
        var conflicts = _service.WindowConflicts(Friday, Friday.AddDays(28), 0);
        Assert.Single(conflicts);
        Assert.Equal(30, conflicts[0].OverlapMinutes);

        // 仅修改本次:第二周五物化 + 例外。
        var detached = _service.DetachOccurrence(rule.Id, Friday.AddDays(7).AddHours(10), 0);
        Assert.NotEqual(ev.Id, detached.Id);
        Assert.Equal("周例会", detached.Title);

        var updatedRule = _service.GetRule(rule.Id)!;
        Assert.Contains("2026-09-25", updatedRule.ExcludedDates);

        // 本次及以后:第三周五起另立新规则。
        var tail = _service.SplitSeries(updatedRule.Id, Friday.AddDays(14).AddHours(10));
        Assert.NotEqual(rule.Id, tail.Id);
        Assert.NotNull(_service.GetRule(rule.Id)); // 旧规则仍在(已截断)
    }

    [Fact]
    public void FreeSlotsExcludeBusyBlocks()
    {
        var start = Friday.AddHours(10);
        _service.CreateBlock(null, start, start.AddHours(1)); // 10-11 占用

        // 工作日 09:00-18:00,最少 60 分钟:09-10 与 11-18。
        var slots = _service.FindFreeSlots(Friday, Friday.AddDays(1),
            9 * 60, 18 * 60, workdayMask: 0b0011111, minMinutes: 60);
        Assert.Equal(2, slots.Count);
        Assert.Equal(60, slots[0].Minutes);
        Assert.Equal(7 * 60, slots[1].Minutes);
    }

    [Fact]
    public void IcsExportImportRoundtrip()
    {
        var start = Friday.AddHours(15);
        _service.CreateEvent("里程碑评审", start, start.AddMinutes(45), "线上");

        var path = Path.Combine(Path.GetTempPath(), $"equora-{Guid.NewGuid():N}.ics");
        _service.ExportIcs(Friday, Friday.AddDays(1), path);
        Assert.Contains("SUMMARY:里程碑评审", File.ReadAllText(path));

        // 导出到新服务并导入回来:幂等。
        using var other = new AppDataService(NewDbPath(), "device-ics");
        var first = other.ImportIcs(path);
        Assert.Equal(1, first.Imported);
        var second = other.ImportIcs(path);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);

        File.Delete(path);
    }

    [Fact]
    public void RuleRejectsInvalidHost()
    {
        var ex = Assert.Throws<EquoraException>(
            () => _service.CreateRule("bogus", "x", RecurFreqDto.Daily));
        Assert.Equal(2, ex.Code); // InvalidArgument
    }
}
