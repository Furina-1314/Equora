using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;

namespace Equora.App.Tests;

public class ReleaseV1Tests
{
    private static SemesterSettings Semester => new() { Enabled = true, Name = "秋季", StartDate = new(2026, 9, 1), EndDate = new(2026, 10, 1) };
    private static AppDataService Service() => new(Path.Combine(Path.GetTempPath(), $"equora-v1-{Guid.NewGuid():N}.db"), "v1-tests");

    [Fact]
    public void OptionalTasksAndTitlesPersistSeparatelyFromNotesAndExport()
    {
        using var service = Service();
        var vm = new CalendarViewModel(service, service, new UndoService());
        var blocks = vm.CreateSemesterBlocks(Semester, "1-3", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "课程标题", "教室 201", "#0078D4", false);
        Assert.Empty(service.ListTasks());
        Assert.All(blocks, b => { Assert.Null(b.TaskId); Assert.Equal("课程标题", service.GetBlock(b.Id)!.Title); });
        Assert.Single(blocks.Select(b => b.BatchId).Distinct());
        var block = vm.SaveTimeBlock(null, blocks[0].StartAt, blocks[0].EndAt, "独立备注", "#0078D4", title: "独立标题");
        Assert.Null(block.TaskId);
        Assert.Equal("独立备注", block.Note);
        Assert.Contains(service.WindowSpans(block.StartAt, block.EndAt, 480), s => s.SourceId == block.Id && s.Title == "独立标题");
        var file = Path.GetTempFileName();
        try { service.ExportIcs(block.StartAt, block.EndAt, file); Assert.Contains("SUMMARY:独立标题", File.ReadAllText(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void BatchSelectionDoesNotCrossBatchesAndEditsPreserveIndependentFields()
    {
        using var service = Service();
        var vm = new CalendarViewModel(service, service, new UndoService());
        var first = vm.CreateSemesterBlocks(Semester, "1-3", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "同名", "原备注", "#0078D4", false);
        var other = vm.CreateSemesterBlocks(Semester, "1-3", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "同名", "另一批", "#0078D4", false);
        Assert.Equal(first.Select(b => b.Id), vm.BatchCandidates(first[0].Id).Select(b => b.Id));
        vm.EditBlocks(first.Take(2).ToArray(), "新标题", null, null, TimeSpan.FromHours(14), TimeSpan.FromHours(15));
        Assert.Equal("新标题", service.GetBlock(first[0].Id)!.Title);
        Assert.Equal("原备注", service.GetBlock(first[0].Id)!.Note);
        Assert.Equal(14, service.GetBlock(first[0].Id)!.StartAt.ToLocalTime().Hour);
        Assert.Equal(first[0].StartAt.ToLocalTime().Date, service.GetBlock(first[0].Id)!.StartAt.ToLocalTime().Date);
        Assert.Equal("同名", service.GetBlock(first[2].Id)!.Title);
        Assert.All(other, b => Assert.Equal("同名", service.GetBlock(b.Id)!.Title));
    }

    [Fact]
    public void StaleBatchUpdateAndDeleteRollBackEveryItem()
    {
        using var service = Service();
        var vm = new CalendarViewModel(service, service, new UndoService());
        var blocks = vm.CreateSemesterBlocks(Semester, "1-2", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "课程", "", "#0078D4");
        service.UpdateBlock(blocks[1] with { Note = "已单独编辑" });
        Assert.Throws<EquoraException>(() => vm.EditBlocks(blocks, "不应保存", null, null, null, null));
        Assert.Equal("课程", service.GetBlock(blocks[0].Id)!.Title);
        Assert.Throws<EquoraException>(() => vm.DeleteBlocks(blocks));
        Assert.NotNull(service.GetBlock(blocks[0].Id));
        var latest = blocks.Select(b => service.GetBlock(b.Id)!).ToArray();
        vm.DeleteBlocks(latest);
        Assert.All(blocks, b => { Assert.Null(service.GetBlock(b.Id)); Assert.NotNull(service.GetTask(b.TaskId!)); });
    }

    [Fact]
    public void AssociatedTasksCanBeEditedAndDeletedAtomically()
    {
        using var service = Service();
        var vm = new CalendarViewModel(service, service, new UndoService());
        var blocks = vm.CreateSemesterBlocks(Semester, "1-2", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "课程", "", "#0078D4");
        vm.EditBlocks(blocks, "新课程", null, null, null, null, true);
        Assert.All(blocks, b => Assert.Equal("新课程", service.GetTask(b.TaskId!)!.Title));
        var latest = blocks.Select(b => service.GetBlock(b.Id)!).ToArray();
        service.UpdateBlock(latest[1] with { Note = "改变版本" });
        Assert.Throws<EquoraException>(() => vm.DeleteBlocks(latest, true));
        Assert.All(blocks, b => Assert.NotNull(service.GetTask(b.TaskId!)));
        vm.DeleteBlocks(blocks.Select(b => service.GetBlock(b.Id)!).ToArray(), true);
        Assert.All(blocks, b => { Assert.Null(service.GetBlock(b.Id)); Assert.Null(service.GetTask(b.TaskId!)); Assert.NotNull(service.GetTask(b.TaskId!, true)); });
    }

    [Fact]
    public void TitlesAndBatchIdentitySurviveDatabaseReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"equora-reopen-{Guid.NewGuid():N}.db");
        string id, batch;
        using (var service = new AppDataService(path, "test"))
        {
            var vm = new CalendarViewModel(service, service, new UndoService());
            var block = vm.CreateSemesterBlocks(Semester, "1", DayOfWeek.Wednesday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "持久标题", "备注", "#0078D4", false)[0];
            id = block.Id; batch = block.BatchId;
        }
        using var reopened = new AppDataService(path, "test");
        Assert.Equal("持久标题", reopened.GetBlock(id)!.Title);
        Assert.Equal(batch, reopened.GetBlock(id)!.BatchId);
        Assert.Empty(reopened.ListTasks());
    }

    [Fact]
    public void SemesterArchivesOnlyAfterEndDateAndDoesNotArchiveTwice()
    {
        var current = new AppPreferences { Semester = Semester };
        Assert.Same(current, SemesterLifecycle.ArchiveExpired(current, current.Semester.EndDate));
        var archived = SemesterLifecycle.ArchiveExpired(current, current.Semester.EndDate.AddDays(1));
        Assert.Equal(current.Semester, Assert.Single(archived.ArchivedSemesters));
        Assert.True(archived.Semester.Enabled);
        Assert.Empty(archived.Semester.Name);
        Assert.Same(archived, SemesterLifecycle.ArchiveExpired(archived, new(2027, 1, 1)));
        var next = archived with { Semester = new SemesterSettings { Enabled = true, Name = "春季", StartDate = new(2027, 2, 1), EndDate = new(2027, 6, 1) } };
        Assert.Equal("秋季", Assert.Single(next.ArchivedSemesters).Name);
        var path = Path.GetTempFileName();
        try { new PreferencesStore(path).Save(next); Assert.Equivalent(next, new PreferencesStore(path).Load()); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(2, false, 2)]
    [InlineData(3, false, 3)]
    [InlineData(4, true, 3)]
    public async Task ResetRequiresAllThreeConfirmations(int cancelAt, bool expected, int expectedCalls)
    {
        var calls = 0;
        var accepted = await DataReset.ConfirmAsync(step => { calls++; return Task.FromResult(step != cancelAt); });
        Assert.Equal(expected, accepted);
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public void ResetClearsManagedDataAndPreservesSnapshotAndUnrelatedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "equora-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = Path.Combine(root, "equora.db");
            using (var service = new AppDataService(db, "reset-test")) service.CreateTask(new TaskDraft { Title = "旧任务" });
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{}");
            File.WriteAllText(Path.Combine(root, "personal.txt"), "keep");
            File.WriteAllText(Path.Combine(root, "data-location.txt"), "keep location");
            var backup = DataReset.Clear(root);
            Assert.False(File.Exists(db));
            Assert.False(File.Exists(Path.Combine(root, "preferences.json")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "personal.txt")));
            Assert.True(File.Exists(Path.Combine(root, "data-location.txt")));
            using (var restored = new AppDataService(Path.Combine(backup, "equora.db"), "restored")) Assert.Single(restored.ListTasks());
            using var fresh = new AppDataService(db, "fresh");
            Assert.Empty(fresh.ListTasks());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResetRollsBackWhenFileIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "equora-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "equora.db"), "original");
            var prefs = Path.Combine(root, "preferences.json");
            File.WriteAllText(prefs, "settings");
            using (var locked = new FileStream(prefs, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => DataReset.Clear(root));
            Assert.Equal("original", File.ReadAllText(Path.Combine(root, "equora.db")));
            Assert.Equal("settings", File.ReadAllText(prefs));
        }
        finally { Directory.Delete(root, true); }
    }
}
