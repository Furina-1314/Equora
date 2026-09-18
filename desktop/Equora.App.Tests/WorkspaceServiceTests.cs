using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Xunit;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Tests;

/// <summary>P4 全链路:项目/标签/检查项/智能清单查询/备份/导入导出。</summary>
public class WorkspaceServiceTests : IDisposable
{
    private readonly AppDataService _service;

    public WorkspaceServiceTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p4-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    [Fact]
    public void DeviceIdIsStableUuid()
    {
        var id = _service.DeviceId;
        Assert.Equal(36, id.Length);
        Assert.Equal(id, _service.DeviceId);
    }

    [Fact]
    public void ProjectLifecycle()
    {
        var created = _service.CreateProject("产品重构", "#3366CC", "Q4 完成架构迁移");
        Assert.Equal("产品重构", created.Name);
        Assert.Equal(ProjectStatusDto.Active, created.Status);

        var renamed = _service.UpdateProject(created with { Name = "平台重构" });
        Assert.Equal(2, renamed.Revision);
        Assert.Equal("平台重构", _service.GetProject(created.Id)!.Name);

        var archived = _service.SetProjectArchived(created.Id, archived: true);
        Assert.Equal(ProjectStatusDto.Archived, archived.Status);
        Assert.NotNull(archived.ArchivedAt);

        Assert.Empty(_service.ListProjects(includeArchived: false));
        Assert.Single(_service.ListProjects());
    }

    [Fact]
    public void TagAssignUnassignAndConflict()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "任务" });
        var tag = _service.CreateTag("focus", "#FF0000");

        var conflict = Assert.Throws<EquoraException>(() => _service.CreateTag("FOCUS"));
        Assert.Equal(4, conflict.Code); // 大小写不敏感唯一

        _service.AddTagToTask(task.Id, tag.Id);
        _service.AddTagToTask(task.Id, tag.Id); // 幂等
        var tags = _service.TagsForTask(task.Id);
        Assert.Single(tags);
        Assert.Equal("focus", tags[0].Name);

        _service.RemoveTagFromTask(task.Id, tag.Id);
        Assert.Empty(_service.TagsForTask(task.Id));
    }

    [Fact]
    public void ChecklistLifecycle()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "发布" });
        var a = _service.AddChecklistItem(task.Id, "写发布说明");
        var b = _service.AddChecklistItem(task.Id, "通知用户");
        Assert.Equal(1, a.SortOrder);
        Assert.Equal(2, b.SortOrder);

        var done = _service.UpdateChecklistItem(a with { IsChecked = true });
        Assert.True(done.IsChecked);
        Assert.Equal(2, done.Revision);

        _service.RemoveChecklistItem(b.Id);
        var items = _service.ListChecklist(task.Id);
        Assert.Single(items);
        Assert.Equal("写发布说明", items[0].Content);
    }

    [Fact]
    public void SmartListQueries()
    {
        // 用本地日界锚定,避免测试在午夜附近运行时跨越"今天"。
        var offsetMin = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes;
        long DayStart(long ms)
        {
            var shifted = ms + offsetMin * 60_000L;
            var day = (long)Math.Floor((double)shifted / 86_400_000);
            return day * 86_400_000 - offsetMin * 60_000L;
        }
        var dayStart = DayStart(DateTimeOffset.Now.ToUnixTimeMilliseconds());
        DateTimeOffset At(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

        _service.CreateTask(new TaskDraft { Title = "昨天的", DueAt = At(dayStart - 12 * 3_600_000) });
        _service.CreateTask(new TaskDraft { Title = "今天的", DueAt = At(dayStart + 12 * 3_600_000) });
        _service.CreateTask(new TaskDraft { Title = "下周的", DueAt = At(dayStart + 6 * 86_400_000 + 18 * 3_600_000) });
        var done = _service.CreateTask(new TaskDraft
        {
            Title = "已完成的",
            DueAt = At(dayStart - 12 * 3_600_000),
        });
        _service.UpdateTask(_service.GetTask(done.Id)! with { Status = TaskStatus.Done });

        var today = _service.QueryTasks(new TaskQuery { SmartList = SmartListKind.Today });
        Assert.Single(today);
        Assert.Equal("今天的", today[0].Title);

        var overdue = _service.QueryTasks(new TaskQuery { SmartList = SmartListKind.Overdue });
        // 逾期语义:已过截止且未完成。"今天的"(当地中午)在午后运行时也确实逾期,
        // 因此不用 Single,改为确定不变的边界断言。
        Assert.Contains(overdue, t => t.Title == "昨天的");
        Assert.DoesNotContain(overdue, t => t.Title == "已完成的");
        Assert.DoesNotContain(overdue, t => t.Title == "下周的");

        var upcoming = _service.QueryTasks(new TaskQuery { SmartList = SmartListKind.Upcoming7 });
        Assert.Contains(upcoming, t => t.Title == "下周的");
        Assert.DoesNotContain(upcoming, t => t.Title == "昨天的");
        Assert.DoesNotContain(upcoming, t => t.Title == "已完成的");

        var search = _service.QueryTasks(new TaskQuery { Search = "不存在的词" });
        Assert.Empty(search);
    }

    [Fact]
    public void BackupExportImportRoundtrip()
    {
        // 文件库:备份/恢复需要真实路径。
        using var fileService = new AppDataService(NewDbPath(), "device-tests");
        fileService.CreateTask(new TaskDraft { Title = "备份源" });

        var dir = Path.Combine(Path.GetTempPath(), $"equora-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var backupPath = fileService.CreateBackup(dir);
        Assert.True(File.Exists(backupPath));
        Assert.True(fileService.VerifyBackup(backupPath));

        var jsonPath = Path.Combine(dir, "tasks.json");
        Assert.Equal(1, fileService.ExportTasksJson(jsonPath));

        // 备份后再新增,恢复后应回到 1 条。
        fileService.CreateTask(new TaskDraft { Title = "备份后" });
        fileService.RestoreBackup(backupPath);
        Assert.Single(fileService.ListTasks());

        // 导入同一 JSON:幂等跳过。
        var (imported, skipped) = fileService.ImportTasksJson(jsonPath);
        Assert.Equal(0, imported);
        Assert.Equal(1, skipped);

        Directory.Delete(dir, recursive: true);
    }
}
