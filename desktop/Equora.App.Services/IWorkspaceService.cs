using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>P4 扩展数据面:项目、标签、检查项、查询、备份与导入导出。</summary>
public interface IWorkspaceService
{
    string DeviceId { get; }

    // 项目
    ProjectDto CreateProject(string name, string color = "", string goal = "");
    ProjectDto? GetProject(string id);
    ProjectDto UpdateProject(ProjectDto project);
    ProjectDto SetProjectArchived(string id, bool archived);
    void DeleteProject(string id);
    IReadOnlyList<ProjectDto> ListProjects(bool includeArchived = true);

    // 标签
    TagDto CreateTag(string name, string color = "");
    TagDto? GetTag(string id);
    TagDto UpdateTag(TagDto tag);
    void DeleteTag(string id);
    IReadOnlyList<TagDto> ListTags();
    void AddTagToTask(string taskId, string tagId);
    void RemoveTagFromTask(string taskId, string tagId);
    IReadOnlyList<TagDto> TagsForTask(string taskId);

    // 检查项
    ChecklistItemDto AddChecklistItem(string taskId, string content);
    ChecklistItemDto UpdateChecklistItem(ChecklistItemDto item);
    void RemoveChecklistItem(string id);
    IReadOnlyList<ChecklistItemDto> ListChecklist(string taskId);

    // 查询
    IReadOnlyList<TaskDto> QueryTasks(TaskQuery query);

    // 备份与导入导出
    string CreateBackup(string directory);
    bool VerifyBackup(string path);
    void RestoreBackup(string path);
    int ExportTasksJson(string path);
    int ExportTasksCsv(string path);
    (int Imported, int Skipped) ImportTasksJson(string path);
}
