using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>IWorkspaceService 实现:直接转发到原生核心。</summary>
public sealed partial class AppDataService
{
    public string DeviceId => _core.DeviceId;

    public ProjectDto CreateProject(string name, string color = "", string goal = "") =>
        _core.CreateProject(name, color, goal);
    public ProjectDto? GetProject(string id) => _core.GetProject(id);
    public ProjectDto UpdateProject(ProjectDto project) => _core.UpdateProject(project);
    public ProjectDto SetProjectArchived(string id, bool archived) =>
        _core.SetProjectArchived(id, archived);
    public void DeleteProject(string id) => _core.DeleteProject(id);
    public IReadOnlyList<ProjectDto> ListProjects(bool includeArchived = true) =>
        _core.ListProjects(includeArchived);

    public TagDto CreateTag(string name, string color = "") => _core.CreateTag(name, color);
    public TagDto? GetTag(string id) => _core.GetTag(id);
    public TagDto UpdateTag(TagDto tag) => _core.UpdateTag(tag);
    public void DeleteTag(string id) => _core.DeleteTag(id);
    public IReadOnlyList<TagDto> ListTags() => _core.ListTags();
    public void AddTagToTask(string taskId, string tagId) => _core.AddTagToTask(taskId, tagId);
    public void RemoveTagFromTask(string taskId, string tagId) =>
        _core.RemoveTagFromTask(taskId, tagId);
    public IReadOnlyList<TagDto> TagsForTask(string taskId) => _core.TagsForTask(taskId);

    public ChecklistItemDto AddChecklistItem(string taskId, string content) =>
        _core.AddChecklistItem(taskId, content);
    public ChecklistItemDto UpdateChecklistItem(ChecklistItemDto item) =>
        _core.UpdateChecklistItem(item);
    public void RemoveChecklistItem(string id) => _core.RemoveChecklistItem(id);
    public IReadOnlyList<ChecklistItemDto> ListChecklist(string taskId) =>
        _core.ListChecklist(taskId);

    public IReadOnlyList<TaskDto> QueryTasks(TaskQuery query) => _core.QueryTasks(query);

    public string CreateBackup(string directory) => _core.CreateBackup(directory);
    public bool VerifyBackup(string path) => _core.VerifyBackup(path);
    public void RestoreBackup(string path) => _core.RestoreBackup(path);
    public int ExportTasksJson(string path) => _core.ExportTasksJson(path);
    public int ExportTasksCsv(string path) => _core.ExportTasksCsv(path);
    public (int Imported, int Skipped) ImportTasksJson(string path) =>
        _core.ImportTasksJson(path);
}
