using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equora.App.NativeInterop;
using Equora.App.Services;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.ViewModels;

/// <summary>侧栏导航项(智能清单 / 项目 / 标签)。</summary>
public sealed record NavItem(
    string Key,
    string Title,
    string Glyph,
    SmartListKind? SmartList = null,
    string? ProjectId = null,
    string? TagId = null,
    bool IncludeDeleted = false)
{
    public override string ToString() => Title;
}

/// <summary>
/// 任务页主视图模型:三栏的中枢——侧栏选择决定查询,中栏展示结果,
/// 右栏详情由选中任务驱动。所有数据操作经服务层进入原生核心。
/// </summary>
public partial class TasksViewModel : ObservableObject
{
    private readonly ITaskService _tasks;
    private readonly IWorkspaceService _workspace;
    private readonly IUndoService _undo;

    public TasksViewModel(ITaskService tasks, IWorkspaceService workspace, IUndoService undo)
    {
        _tasks = tasks;
        _workspace = workspace;
        _undo = undo;
        Detail = new TaskDetailViewModel(tasks, workspace, undo, this);
    }

    public ObservableCollection<NavItem> Lists { get; } = new();

    public ObservableCollection<ProjectDto> Projects { get; } = new();

    public ObservableCollection<TagDto> Tags { get; } = new();

    public ObservableCollection<TaskDto> Tasks { get; } = new();

    public TaskDetailViewModel Detail { get; }

    [ObservableProperty]
    private NavItem? _selectedList;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private TaskDto? _selectedTask;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _newTaskTitle = "";

    public bool HasTasks => Tasks.Count > 0;
    public bool IsRefreshing { get; private set; }

    public bool CanUndo => _undo.CanUndo;

    partial void OnSelectedListChanged(NavItem? value)
    {
        Refresh();
    }

    partial void OnSelectedTaskChanged(TaskDto? value)
    {
        if (!IsRefreshing) Detail.Load(value);
    }

    // ---- 初始化与查询 ----

    public void InitializeNav()
    {
        if (Lists.Count == 0)
        {
            Lists.Add(new NavItem("inbox", "收集箱", "\uE7C1", SmartList: SmartListKind.Inbox));
            Lists.Add(new NavItem("today", "今天", "\uE787", SmartList: SmartListKind.Today));
            Lists.Add(new NavItem("upcoming", "近期 7 天", "\uE823", SmartList: SmartListKind.Upcoming7));
            Lists.Add(new NavItem("overdue", "已逾期", "\uE783", SmartList: SmartListKind.Overdue));
            Lists.Add(new NavItem("nodate", "无日期", "\uE8FD", SmartList: SmartListKind.NoDate));
            Lists.Add(new NavItem("waiting", "等待中", "\uE7E8", SmartList: SmartListKind.Waiting));
            Lists.Add(new NavItem("completed", "已完成", "\uE73E", SmartList: SmartListKind.Completed));
            Lists.Add(new NavItem("all", "全部任务", "\uE71D"));
            Lists.Add(new NavItem("trash", "回收站", "\uE74D", IncludeDeleted: true));
        }

        Projects.Clear();
        foreach (var p in _workspace.ListProjects()) Projects.Add(p);

        Tags.Clear();
        foreach (var t in _workspace.ListTags()) Tags.Add(t);
    }

    [RelayCommand]
    public void Refresh()
    {
        var selectedId = SelectedTask?.Id;
        var query = BuildQuery();
        var result = _tasks.QueryTasks(query);

        // 回收站语义:只显示已删除条目(原生过滤器暂无 only_deleted,客户端过滤)。
        if (SelectedList is { IncludeDeleted: true })
        {
            result = result.Where(t => t.IsDeleted).ToList();
        }

        IsRefreshing = true;
        try
        {
            // Keep selection stable while ListView processes collection notifications.
            for (var i = Tasks.Count - 1; i >= 0; i--)
                if (!result.Any(t => t.Id == Tasks[i].Id)) Tasks.RemoveAt(i);
            for (var i = 0; i < result.Count; i++)
            {
                var existing = Tasks.FirstOrDefault(t => t.Id == result[i].Id);
                if (existing is null) Tasks.Insert(i, result[i]);
                else
                {
                    var index = Tasks.IndexOf(existing);
                    if (index != i) Tasks.Move(index, i);
                    if (Tasks[i] != result[i]) Tasks[i] = result[i];
                }
            }
            SelectedTask = Tasks.FirstOrDefault(t => t.Id == selectedId);
            Detail.Load(SelectedTask);
        }
        finally { IsRefreshing = false; }
        OnPropertyChanged(nameof(HasTasks));
        StatusText = SelectedList?.Title is null
            ? $"共 {Tasks.Count} 项"
            : $"{SelectedList.Title} · {Tasks.Count} 项";
        OnPropertyChanged(nameof(CanUndo));
    }

    private TaskQuery BuildQuery()
    {
        var nav = SelectedList;
        if (nav is { IncludeDeleted: true })
        {
            return new TaskQuery
            {
                IncludeDeleted = true,
                Sort = TaskSort.UpdatedDesc,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            };
        }
        if (nav?.ProjectId is not null)
        {
            return new TaskQuery
            {
                ProjectId = nav.ProjectId,
                Sort = TaskSort.UpdatedDesc,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            };
        }
        if (nav?.TagId is not null)
        {
            return new TaskQuery
            {
                TagId = nav.TagId,
                Sort = TaskSort.UpdatedDesc,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            };
        }
        if (nav?.SmartList is not null)
        {
            return new TaskQuery
            {
                SmartList = nav.SmartList,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            };
        }
        return new TaskQuery
        {
            Sort = TaskSort.UpdatedDesc,
            Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
        };
    }

    // ---- 任务操作(全部接撤销栈) ----

    [RelayCommand]
    public void NewTask()
    {
        var nav = SelectedList;
        var now = DateTimeOffset.Now;
        var created = _tasks.CreateTask(new TaskDraft
        {
            Title = string.IsNullOrWhiteSpace(NewTaskTitle) ? "新任务" : NewTaskTitle.Trim(),
            Status = nav?.SmartList == SmartListKind.Waiting ? TaskStatus.Waiting : TaskStatus.Inbox,
            ProjectId = nav?.ProjectId,
            DueAt = nav?.SmartList is SmartListKind.Today or SmartListKind.Upcoming7 ? now.Date.AddHours(18) : null,
        });
        if (nav?.TagId is string tag) _workspace.AddTagToTask(created.Id, tag);
        NewTaskTitle = "";
        SearchText = "";
        if (nav?.IncludeDeleted == true || nav?.SmartList is SmartListKind.Completed or SmartListKind.Overdue)
            SelectedList = Lists.First(l => l.Key == "inbox");
        _undo.Push($"新建「{created.Title}」", () => _tasks.DeleteTask(created.Id));
        Refresh();
        SelectedTask = Tasks.FirstOrDefault(t => t.Id == created.Id);
        StatusText = $"已新建:{created.Title}(Ctrl+Z 撤销)";
        OnPropertyChanged(nameof(CanUndo));
    }

    [RelayCommand]
    public void ToggleDone(TaskDto? task)
    {
        if (task is null) return;
        var fresh = _tasks.GetTask(task.Id);
        if (fresh is null || fresh.IsDeleted) return;

        var target = fresh.Status == TaskStatus.Done
            ? TaskStatus.Inbox
            : TaskStatus.Done;
        var updated = _tasks.UpdateTask(fresh with { Status = target });
        _undo.Push($"{(target == TaskStatus.Done ? "完成" : "重开")}「{updated.Title}」", () =>
        {
            var current = _tasks.GetTask(updated.Id);
            if (current is not null) _tasks.UpdateTask(current with { Status = fresh.Status });
        });
        Refresh();
        OnPropertyChanged(nameof(CanUndo));
    }

    [RelayCommand]
    public void DeleteTask(TaskDto? task)
    {
        if (task is null) return;
        if (task.IsDeleted)
        {
            _tasks.PermanentlyDeleteTask(task.Id);
            _undo.Clear();
            if (SelectedTask?.Id == task.Id) SelectedTask = null;
            Refresh();
            StatusText = $"已永久删除:{task.Title}";
            OnPropertyChanged(nameof(CanUndo));
            return;
        }
        if (_tasks.GetTask(task.Id) is null) return;

        _tasks.DeleteTask(task.Id);
        _undo.Push($"删除「{task.Title}」", () => _tasks.RestoreTask(task.Id));
        if (SelectedTask?.Id == task.Id) SelectedTask = null;
        Refresh();
        StatusText = $"已移入回收站:{task.Title}(Ctrl+Z 撤销)";
        OnPropertyChanged(nameof(CanUndo));
    }

    [RelayCommand]
    public void RestoreTask(TaskDto? task)
    {
        if (task is null) return;
        _tasks.RestoreTask(task.Id);
        _undo.Push($"恢复「{task.Title}」", () => _tasks.DeleteTask(task.Id));
        Refresh();
        StatusText = $"已恢复:{task.Title}";
    }

    [RelayCommand]
    public void Undo()
    {
        var description = _undo.Undo();
        Refresh();
        StatusText = description is null ? "没有可撤销的操作" : $"已撤销:{description}";
        OnPropertyChanged(nameof(CanUndo));
    }

    [RelayCommand]
    public void RunSearch()
    {
        Refresh();
    }

    /// <summary>详情编辑后由 TaskDetailViewModel 回调刷新列表。</summary>
    public void RefreshAfterEdit()
    {
        Refresh();
    }
}
