using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;

namespace Equora.App.Tests;
public class DesktopFollowupTests
{
    private static AppDataService Service() => new(Path.Combine(Path.GetTempPath(), $"equora-followup-{Guid.NewGuid():N}.db"), "tests");
    [Fact]
    public void PomodoroAlternatesRoundsAndPauseFreezesRest()
    {
        using var data = Service();
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.Now.ToUnixTimeSeconds());
        var vm = new FocusViewModel(data, data) { Clock = start, PlannedMinutes = 1, Rounds = 2, BreakMinutes = 1 };
        vm.Start(); vm.Clock = start.AddMinutes(1);
        Assert.True(vm.IsBreak); Assert.Null(data.OpenFocus()); Assert.Equal("01:00", vm.ClockText);
        vm.ToggleSession(); vm.Clock = start.AddMinutes(3); Assert.Equal("01:00", vm.ClockText);
        vm.ToggleSession(); vm.Clock = start.AddMinutes(4);
        Assert.False(vm.IsBreak); Assert.NotNull(vm.Session); Assert.Contains("2 / 2", vm.RoundStatus);
        vm.Clock = start.AddMinutes(5);
        Assert.False(vm.IsRunning); Assert.Contains("全部 2 轮", vm.StatusText);
        Assert.Equal(2, data.FocusHistory(start, start.AddHours(1)).Count);
    }
    [Fact]
    public void ModesDifferAtTheTimeLimit()
    {
        using var data = Service(); var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.Now.ToUnixTimeSeconds());
        var vm = new FocusViewModel(data, data) { Clock = start, ModeIndex = 1, PlannedMinutes = 1 };
        vm.Start(); vm.Clock = start.AddMinutes(1); Assert.False(vm.IsRunning); Assert.False(vm.IsBreak);
        vm.ModeIndex = 2; Assert.Equal("00:00", vm.ClockText); vm.Start(); vm.Clock = start.AddMinutes(3);
        Assert.True(vm.IsRunning); Assert.Equal(FocusModeDto.Stopwatch, vm.Session!.Mode); Assert.Equal("02:00", vm.ClockText);
    }
    [Fact]
    public void CalendarEditPersistsNotesTimesAndColorAndDeletePreservesTask()
    {
        using var data = Service(); var vm = new CalendarViewModel(data, data, new UndoService()); var start = vm.WindowStart.AddHours(9);
        var block = vm.SaveTimeBlock(null, start, start.AddHours(1), "Meeting notes", "#008272", "Meeting");
        Assert.Equal("#008272", vm.Items.Single().Color);
        Assert.Equal("Meeting notes", data.GetBlock(block.Id)!.Note);
        vm.SaveTimeBlock(block.Id, start.AddMinutes(15), start.AddHours(2), "Updated", "#744DA9");
        Assert.Equal("#744DA9", vm.Items.Single().Color);
        Assert.Equal(start.AddHours(2), vm.Items.Single().Span.End);
        Assert.Throws<ArgumentException>(() => vm.SaveTimeBlock(block.Id, start, start, "", "#0078D4"));
        vm.DeleteItem(vm.Items.Single()); Assert.Empty(vm.Items); Assert.NotNull(data.GetTask(block.TaskId!));
    }
    [Fact]
    public void DataMigrationUsesConsistentSnapshotAndDoesNotOverwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "equora-migration-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); var target = Path.Combine(root, "target"); Directory.CreateDirectory(source);
        using var data = new AppDataService(Path.Combine(source, "equora.db"), "tests");
        data.CreateTask(new TaskDraft { Title = "Preserve me" }); File.WriteAllText(Path.Combine(source, "preferences.json"), "{}");
        DataDirectoryMigration.CopySnapshot(source, target, data.CreateBackup);
        using var copied = new AppDataService(Path.Combine(target, "equora.db"), "copy");
        Assert.Single(copied.QueryTasks(new TaskQuery())); Assert.True(File.Exists(Path.Combine(target, "preferences.json")));
        Assert.Single(data.QueryTasks(new TaskQuery()));
        Assert.Throws<ArgumentException>(() => DataDirectoryMigration.CopySnapshot(source, target, data.CreateBackup));
        Assert.Throws<ArgumentException>(() => DataDirectoryMigration.CopySnapshot(source, Path.Combine(source, "nested"), data.CreateBackup));
    }
}
