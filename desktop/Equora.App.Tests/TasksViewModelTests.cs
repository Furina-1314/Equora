using System.IO;
using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.Tests;

/// <summary>P5:任务页三栏视图模型与撤销栈。</summary>
public class TasksViewModelTests : IDisposable
{
    private readonly AppDataService _service;
    private readonly UndoService _undo;
    private readonly TasksViewModel _vm;

    public TasksViewModelTests()
    {
        _service = new AppDataService(NewDbPath(), "device-tests");
        _undo = new UndoService();
        _vm = new TasksViewModel(_service, _service, _undo);
        _vm.InitializeNav();
    }

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(),
        $"equora-p5-{Guid.NewGuid():N}.db");

    public void Dispose() => _service.Dispose();

    private void SelectList(string key)
    {
        _vm.SelectedList = _vm.Lists.First(l => l.Key == key);
    }

    [Fact]
    public void InitializeNavBuildsListsAndSmartSections()
    {
        Assert.Equal(9, _vm.Lists.Count); // 8 个清单 + 回收站
        Assert.Contains(_vm.Lists, l => l.Key == "today");
    }

    [Fact]
    public void SelectingTodayShowsOnlyTodayTasks()
    {
        var now = DateTimeOffset.Now;
        _service.CreateTask(new TaskDraft { Title = "今天稍后", DueAt = now.AddMinutes(30) });
        _service.CreateTask(new TaskDraft { Title = "下周", DueAt = now.AddDays(6) });

        SelectList("today");
        Assert.Single(_vm.Tasks);
        Assert.Equal("今天稍后", _vm.Tasks[0].Title);
    }

    [Fact]
    public void SearchFiltersCurrentList()
    {
        _service.CreateTask(new TaskDraft { Title = "周报汇总" });
        _service.CreateTask(new TaskDraft { Title = "买菜" });

        SelectList("all");
        _vm.SearchText = "周报";
        _vm.RunSearch();
        Assert.Single(_vm.Tasks);

        _vm.SearchText = "";
        _vm.RunSearch();
        Assert.Equal(2, _vm.Tasks.Count);
    }

    [Fact]
    public void NewTaskCreatesAndUndoDeletes()
    {
        SelectList("inbox");
        _vm.NewTask();

        var created = Assert.Single(_vm.Tasks);
        Assert.Equal("新任务", created.Title);
        Assert.True(_vm.CanUndo);

        _vm.Undo();
        Assert.Empty(_vm.Tasks); // 回收站也不该有?软删除应存在于回收站
        Assert.Single(_service.ListTasks(includeDeleted: true));
        Assert.False(_vm.CanUndo);
    }

    [Fact]
    public void UndoOfDeleteRestoresTask()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "要被删除的" });
        SelectList("all");

        _vm.DeleteTask(_vm.Tasks.First(t => t.Id == task.Id));
        Assert.Empty(_vm.Tasks);

        _vm.Undo();
        Assert.Single(_vm.Tasks);
        Assert.Equal("要被删除的", _vm.Tasks[0].Title);
    }

    [Fact]
    public void ToggleDoneRoundTrip()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "完成我" });
        SelectList("all");

        _vm.ToggleDone(_vm.Tasks.First(t => t.Id == task.Id));
        Assert.Equal(TaskStatus.Done, _service.GetTask(task.Id)!.Status);

        _vm.Undo();
        Assert.Equal(TaskStatus.Inbox, _service.GetTask(task.Id)!.Status);
    }

    [Fact]
    public void TrashListShowsDeletedAndRestoreWorks()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "回收站测试" });
        _service.DeleteTask(task.Id);

        SelectList("trash");
        Assert.Single(_vm.Tasks);
        Assert.True(_vm.Tasks[0].IsDeleted);

        _vm.RestoreTask(_vm.Tasks[0]);
        Assert.Empty(_vm.Tasks);
        Assert.NotNull(_service.GetTask(task.Id));
    }

    [Fact]
    public void DetailEditPersistsAndRefreshesList()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "原标题" });
        SelectList("all");
        _vm.SelectedTask = _vm.Tasks.First(t => t.Id == task.Id);

        Assert.True(_vm.Detail.IsLoaded);
        _vm.Detail.Title = "新标题";
        Assert.Equal("新标题", _service.GetTask(task.Id)!.Title);
        Assert.Equal("新标题", _vm.Tasks.First(t => t.Id == task.Id).Title);

        _vm.Detail.PriorityIndex = 4; // 紧急
        Assert.Equal(Priority.Urgent, _service.GetTask(task.Id)!.Priority);

        _vm.Undo(); // 撤销优先级修改
        Assert.Equal(Priority.Normal, _service.GetTask(task.Id)!.Priority);
        Assert.Equal("新标题", _service.GetTask(task.Id)!.Title);
    }

    [Fact]
    public void DetailChecklistAndTags()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "带检查项" });
        var tag = _service.CreateTag("p5标签");
        SelectList("all");
        _vm.SelectedTask = _vm.Tasks.First(t => t.Id == task.Id);

        _vm.Detail.AddChecklistItem("第一步");
        _vm.Detail.AddChecklistItem("第二步");
        Assert.Equal(2, _vm.Detail.Checklist.Count);

        var first = _vm.Detail.Checklist[0];
        _vm.Detail.ToggleChecklistItem(first);
        Assert.True(_service.ListChecklist(task.Id)[0].IsChecked);

        _vm.Detail.RemoveChecklistItem(_vm.Detail.Checklist[1]);
        Assert.Single(_service.ListChecklist(task.Id));

        _vm.Detail.ToggleTag(tag.Id);
        Assert.Equal("p5标签", _service.TagsForTask(task.Id).Single().Name);
        _vm.Detail.ToggleTag(tag.Id);
        Assert.Empty(_service.TagsForTask(task.Id));
    }

    [Fact]
    public void DetailDueLifecycle()
    {
        var task = _service.CreateTask(new TaskDraft { Title = "截止测试" });
        SelectList("all");
        _vm.SelectedTask = _vm.Tasks.First(t => t.Id == task.Id);

        _vm.Detail.HasDue = true;
        _vm.Detail.DueDate = DateTimeOffset.Now.AddDays(1);
        _vm.Detail.CommitDue();
        Assert.NotNull(_service.GetTask(task.Id)!.DueAt);

        _vm.Detail.HasDue = false;
        Assert.Null(_service.GetTask(task.Id)!.DueAt);
    }

    [Fact]
    public void ProjectAndTagNavFilterTasks()
    {
        var project = _service.CreateProject("P5项目");
        var tag = _service.CreateTag("P5标签");
        var inProject = _service.CreateTask(new TaskDraft { Title = "项目内" });
        var tagged = _service.CreateTask(new TaskDraft { Title = "带标签" });

        var updated = _service.GetTask(inProject.Id)!;
        updated.ProjectId = project.Id;
        _service.UpdateTask(updated);
        _service.AddTagToTask(tagged.Id, tag.Id);

        _vm.InitializeNav(); // 重新加载项目/标签侧栏
        _vm.SelectedList = new NavItem($"project:{project.Id}", project.Name, "E8F1",
            ProjectId: project.Id);
        Assert.Single(_vm.Tasks);
        Assert.Equal("项目内", _vm.Tasks[0].Title);

        _vm.SelectedList = new NavItem($"tag:{tag.Id}", tag.Name, "E8EC", TagId: tag.Id);
        Assert.Single(_vm.Tasks);
        Assert.Equal("带标签", _vm.Tasks[0].Title);
    }
}
