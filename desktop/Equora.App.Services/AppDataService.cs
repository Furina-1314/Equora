using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>任务数据访问接口(P3 冒烟实现;P4 扩展智能清单与搜索)。</summary>
public interface ITaskService
{
    string NativeVersion { get; }
    int Ping(int value);
    int SchemaVersion { get; }
    TaskDto CreateTask(TaskDraft draft);
    TaskDto? GetTask(string id);
    TaskDto UpdateTask(TaskDto task);
    void DeleteTask(string id);
    void RestoreTask(string id);
    IReadOnlyList<TaskDto> ListTasks(bool includeDeleted = false);
}

/// <summary>
/// 单机数据服务:持有 EquoraCore(原生数据库上下文)。
/// 打开失败会抛出带原因的 EquoraException,由调用方呈现恢复路径。
/// </summary>
public sealed partial class AppDataService : ITaskService, IWorkspaceService, IDisposable
{
    private readonly EquoraCore _core;

    public AppDataService(string databasePath, string? deviceId = null)
    {
        AppPaths.EnsureCreated();
        _core = EquoraCore.Open(databasePath, deviceId);
    }

    public string NativeVersion => EquoraCore.GetNativeVersion();
    public int Ping(int value) => EquoraCore.Ping(value);
    public int SchemaVersion => _core.SchemaVersion;

    public TaskDto CreateTask(TaskDraft draft) => _core.CreateTask(draft);
    public TaskDto? GetTask(string id) => _core.GetTask(id);
    public TaskDto UpdateTask(TaskDto task) => _core.UpdateTask(task);
    public void DeleteTask(string id) => _core.DeleteTask(id);
    public void RestoreTask(string id) => _core.RestoreTask(id);
    public IReadOnlyList<TaskDto> ListTasks(bool includeDeleted = false) =>
        _core.ListTasks(includeDeleted);

    public void Dispose() => _core.Dispose();
}
