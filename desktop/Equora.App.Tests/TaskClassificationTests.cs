using Equora.App.NativeInterop;
using Equora.App.Services;
using Equora.App.ViewModels;
using Xunit;

namespace Equora.App.Tests;
public class TaskClassificationTests
{
    [Fact]
    public void ProjectAssignmentIsSavedAndDeletionKeepsTheTask()
    {
        using var service = new AppDataService(Path.Combine(Path.GetTempPath(), $"equora-classify-{Guid.NewGuid():N}.db"), "test");
        var project = service.CreateProject("Project");
        var tag = service.CreateTag("Tag");
        var task = service.CreateTask(new TaskDraft { Title = "Keep me" });
        var undo = new UndoService();
        var owner = new TasksViewModel(service, service, undo);
        var detail = new TaskDetailViewModel(service, service, undo, owner);
        detail.Load(task);
        detail.SelectedProjectId = project.Id;
        Assert.Equal(project.Id, service.GetTask(task.Id)!.ProjectId);
        detail.Load(service.GetTask(task.Id)!);
        Assert.Equal(project.Id, detail.SelectedProjectId);
        service.AddTagToTask(task.Id, tag.Id);
        service.DeleteTag(tag.Id);
        service.DeleteProject(project.Id);
        Assert.Equal("Keep me", service.GetTask(task.Id)!.Title);
        Assert.Empty(service.TagsForTask(task.Id));
        Assert.Empty(service.ListProjects());
    }
}
