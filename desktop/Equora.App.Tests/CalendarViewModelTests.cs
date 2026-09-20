using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;
using Xunit;

namespace Equora.App.Tests;

/// <summary>P7:日历视图模型(窗口刷新、冲突标记、块编辑、待办拖入、撤销)。</summary>
public class CalendarViewModelTests : IDisposable
{
    private readonly AppDataService _service;
    private readonly UndoService _undo;
    private readonly CalendarViewModel _vm;

    public CalendarViewModelTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
        _undo = new UndoService();
        _vm = new CalendarViewModel(_service, _service, _undo);
        _vm.GoToday(); // 对齐本周
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p7-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private DateTimeOffset WeekStartLocal => _vm.WeekStart;

    [Fact]
    public void RefreshMaterializesBlocksAndConflicts()
    {
        var day = WeekStartLocal.AddDays(2).AddHours(10);
        _service.CreateBlock(null, day, day.AddHours(1));
        _service.CreateBlock(null, day.AddMinutes(30), day.AddHours(2)); // 重叠

        _vm.Refresh();
        Assert.Equal(2, _vm.Items.Count);
        Assert.Equal(2, _vm.Items.Count(i => i.IsConflict)); // 两者都标红
        Assert.Contains("冲突 1", _vm.StatusText);
    }

    [Fact]
    public void CreateBlockAtFromDragAndUndo()
    {
        var slot = WeekStartLocal.AddDays(1).AddHours(14);
        var task = _service.CreateTask(new TaskDraft { Title = "拖入日历的任务" });
        // 模拟从"未安排"侧栏拖入。
        _vm.CreateBlockAt(slot, 90, task.Id);

        var blocks = _service.BlocksForTask(task.Id);
        Assert.Single(blocks);
        Assert.Equal(90, (int)(blocks[0].EndAt - blocks[0].StartAt).TotalMinutes);
        Assert.Equal(TaskStatus.Planned, _service.GetTask(task.Id)!.Status);

        _vm.UndoIfAny(); // 撤销安排
        Assert.Empty(_service.BlocksForTask(task.Id));
        Assert.Equal(TaskStatus.Inbox, _service.GetTask(task.Id)!.Status);
    }

    [Fact]
    public void MoveAndResizeBlockKeepOptimisticConcurrency()
    {
        var start = WeekStartLocal.AddDays(3).AddHours(9);
        var created = _service.CreateBlock(null, start, start.AddHours(1));

        _vm.MoveBlock(created.Id, start.AddDays(3).AddHours(5)); // 周四 14:00(9+5)
        var moved = _service.GetBlock(created.Id)!;
        Assert.Equal(14, moved.StartAt.ToLocalTime().Hour);
        Assert.Equal(2, moved.Revision);

        _vm.ResizeBlock(created.Id, moved.StartAt.AddHours(2));
        var resized = _service.GetBlock(created.Id)!;
        Assert.Equal(3, resized.Revision);
        Assert.Equal(2 * 60, (int)(resized.EndAt - resized.StartAt).TotalMinutes);

        _vm.UndoIfAny(); // 撤销缩放
        Assert.Equal(60, (int)(_service.GetBlock(created.Id)!.EndAt
                              - _service.GetBlock(created.Id)!.StartAt).TotalMinutes);
    }

    [Fact]
    public void UnscheduledTaskListExcludesTasksWithBlocks()
    {
        var free = _service.CreateTask(new TaskDraft { Title = "未安排" });
        var scheduled = _service.CreateTask(new TaskDraft { Title = "已安排" });
        var day = WeekStartLocal.AddDays(4).AddHours(11);
        _service.CreateBlock(scheduled.Id, day, day.AddHours(1));

        _vm.Refresh();
        Assert.Contains(_vm.UnscheduledTasks, t => t.Id == free.Id);
        Assert.DoesNotContain(_vm.UnscheduledTasks, t => t.Id == scheduled.Id);
    }

    [Fact]
    public void WeekNavigationKeepsMondayAnchor()
    {
        var before = _vm.WeekStart;
        Assert.Equal(DayOfWeek.Monday, before.DayOfWeek);

        _vm.NextWeek();
        Assert.Equal(7, (_vm.WeekStart - before).TotalDays);
        _vm.PrevWeek();
        Assert.Equal(0, (_vm.WeekStart - before).TotalDays);
    }

    [Fact]
    public void SlotMathSnapsToQuarterHour()
    {
        var t = new DateTimeOffset(2026, 9, 18, 10, 7, 33, TimeSpan.FromHours(8));
        Assert.Equal(10, SlotMath.Snap(t).Hour);
        Assert.Equal(0, SlotMath.Snap(t).Minute);

        var t2 = new DateTimeOffset(2026, 9, 18, 10, 53, 0, TimeSpan.FromHours(8));
        Assert.Equal(45, SlotMath.Snap(t2).Minute);

        // 像素往返:1 小时 = 56px,半小时 = 28px。
        var dayStart = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.FromHours(8));
        var at930 = dayStart.AddMinutes(9 * 60 + 30);
        Assert.Equal(9.5 * 56, SlotMath.YOffsetWithinDay(at930, dayStart, 56));
        var back = SlotMath.TimeAtOffset(9.5 * 56, dayStart, 56);
        Assert.Equal(at930, back);
    }

    [Fact]
    public void ExtractBlockIdRejectsRecurringInstances()
    {
        Assert.Null(CalendarViewModel.ExtractBlockId(
            "2f092985-6293-488d-9da2-4989751e3109:1789725600000"));
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef",
            CalendarViewModel.ExtractBlockId("01234567-89ab-cdef-0123-456789abcdef"));
    }
    [Fact]
    public void DayNavigationMovesSelectedDayInsteadOfWeekAnchor()
    {
        _vm.ViewModeIndex = 1;
        var start = _vm.WindowStart;
        _vm.NextWeek();
        Assert.Equal(start.AddDays(1), _vm.WindowStart);
        Assert.Equal(_vm.WindowStart.AddDays(1), _vm.WindowEnd);
        _vm.PrevWeek();
        Assert.Equal(start, _vm.WindowStart);
    }

    [Fact]
    public void UnscheduledListExcludesCompletedTasks()
    {
        _service.CreateTask(new TaskDraft { Title = "已完成", Status = TaskStatus.Done });
        _service.CreateTask(new TaskDraft { Title = "待安排" });
        _vm.Refresh();
        Assert.Equal("待安排", Assert.Single(_vm.UnscheduledTasks).Title);
    }

}

file static class CalendarTestExtensions
{
    public static void UndoIfAny(this CalendarViewModel vm)
    {
        var command = vm.GetType().GetProperty("UndoCommand")?.GetValue(vm) as System.Windows.Input.ICommand;
        command?.Execute(null);
    }
}
