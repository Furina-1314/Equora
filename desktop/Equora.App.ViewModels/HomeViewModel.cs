using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equora.App.NativeInterop;
using Equora.App.Services;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.ViewModels;

/// <summary>首页视图模型:P3 阶段承载互操作自检与真实任务冒烟操作。</summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly ITaskService _tasks;

    [ObservableProperty]
    private string _nativeVersion = "";

    [ObservableProperty]
    private int _schemaVersion;

    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    private string _statusText = "就绪。点击「运行自检」验证 C#/C++ 边界。";

    public ObservableCollection<string> Diagnostics { get; } = new();

    public ObservableCollection<TaskDto> Tasks { get; } = new();

    public HomeViewModel(ITaskService tasks) => _tasks = tasks;

    [RelayCommand]
    private void RunDiagnostics()
    {
        Diagnostics.Clear();
        try
        {
            var pingResult = _tasks.Ping(41);
            NativeVersion = _tasks.NativeVersion;
            SchemaVersion = _tasks.SchemaVersion;
            ReloadTasks();

            Diagnostics.Add($"原生核心版本:{NativeVersion}");
            Diagnostics.Add($"eq_ping(41) = {pingResult}(期望 42)");
            Diagnostics.Add($"数据库模式版本:v{SchemaVersion}");
            Diagnostics.Add($"当前任务数:{_tasks.ListTasks().Count}");
            StatusText = pingResult == 42 ? "自检通过 ✓" : "自检异常:ping 结果不符 ✗";
        }
        catch (EquoraException ex)
        {
            StatusText = $"自检失败:{ex.Message}";
            Diagnostics.Add($"错误 {ex.Code}:{ex.Message}");
        }
    }

    [RelayCommand]
    private void AddSampleTask()
    {
        try
        {
            var created = _tasks.CreateTask(new TaskDraft
            {
                Title = $"冒烟任务 {DateTime.Now:HH:mm:ss}",
                Priority = Priority.Normal,
                EstimateMinutes = 25,
            });
            ReloadTasks();
            StatusText = $"已创建:{created.Title}(revision {created.Revision})";
        }
        catch (EquoraException ex)
        {
            StatusText = $"创建失败:{ex.Message}";
        }
    }

    [RelayCommand]
    private void CompleteTask(TaskDto task)
    {
        try
        {
            _tasks.UpdateTask(task with { Status = TaskStatus.Done });
            ReloadTasks();
            StatusText = $"已完成:{task.Title}";
        }
        catch (EquoraException ex)
        {
            StatusText = $"更新失败:{ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteTask(TaskDto task)
    {
        try
        {
            _tasks.DeleteTask(task.Id);
            ReloadTasks();
            StatusText = $"已删除:{task.Title}";
        }
        catch (EquoraException ex)
        {
            StatusText = $"删除失败:{ex.Message}";
        }
    }

    private void ReloadTasks()
    {
        Tasks.Clear();
        foreach (var t in _tasks.ListTasks())
        {
            Tasks.Add(t);
        }
        TaskCount = Tasks.Count;
    }
}
