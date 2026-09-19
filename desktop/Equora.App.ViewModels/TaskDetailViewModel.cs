using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Equora.App.NativeInterop;
using Equora.App.Services;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.ViewModels;

/// <summary>
/// 任务详情编辑(右栏):字段变更即时落库并推进 revision,
/// 每次变更压入撤销栈;标签与检查项同样即时生效。
/// </summary>
public partial class TaskDetailViewModel : ObservableObject
{
    private readonly ITaskService _tasks;
    private readonly IWorkspaceService _workspace;
    private readonly IUndoService _undo;
    private readonly TasksViewModel _owner;

    private TaskDto? _model;

    public TaskDetailViewModel(ITaskService tasks, IWorkspaceService workspace,
        IUndoService undo, TasksViewModel owner)
    {
        _tasks = tasks;
        _workspace = workspace;
        _undo = undo;
        _owner = owner;
    }

    public ObservableCollection<ChecklistItemDto> Checklist { get; } = new();

    public ObservableCollection<TagDto> AllTags { get; } = new();

    public ObservableCollection<string> AssignedTagIds { get; } = new();

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _note = "";

    [ObservableProperty]
    private int _statusIndex;

    [ObservableProperty]
    private int _priorityIndex;

    [ObservableProperty]
    private DateTimeOffset? _dueDate;

    [ObservableProperty]
    private TimeSpan _dueTime = TimeSpan.FromHours(9);

    [ObservableProperty]
    private bool _hasDue;

    [ObservableProperty]
    private string _estimateText = "";

    /// <summary>界面展示用状态/优先级选项(顺序与枚举一致)。</summary>
    public static IReadOnlyList<string> StatusNames { get; } = new[]
    {
        "收集箱", "已安排", "进行中", "等待中", "已完成", "已取消",
    };

    public static IReadOnlyList<string> PriorityNames { get; } = new[]
    {
        "无", "低", "普通", "高", "紧急",
    };

    public string CreatedInfo => _model is null
        ? ""
        : $"创建于 {_model.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 修订 {_model.Revision}";

    // ---- 装载与保存 ----

    public void Load(TaskDto? task)
    {
        _model = task;
        if (task is null)
        {
            IsLoaded = false;
            Checklist.Clear();
            AllTags.Clear();
            AssignedTagIds.Clear();
            return;
        }

        Title = task.Title;
        Note = task.Note;
        StatusIndex = (int)task.Status;
        PriorityIndex = (int)task.Priority;
        HasDue = task.DueAt.HasValue;
        DueDate = task.DueAt?.ToLocalTime();
        DueTime = task.DueAt?.ToLocalTime().TimeOfDay ?? TimeSpan.FromHours(9);
        EstimateText = task.EstimateMinutes.HasValue ? task.EstimateMinutes.ToString() : "";

        Checklist.Clear();
        foreach (var item in _workspace.ListChecklist(task.Id)) Checklist.Add(item);

        AllTags.Clear();
        AssignedTagIds.Clear();
        foreach (var tag in _workspace.ListTags()) AllTags.Add(tag);
        foreach (var tag in _workspace.TagsForTask(task.Id)) AssignedTagIds.Add(tag.Id);

        IsLoaded = true;
        OnPropertyChanged(nameof(CreatedInfo));
    }

    private TaskDto? Current()
    {
        if (_model is null) return null;
        var fresh = _tasks.GetTask(_model.Id);
        if (fresh is null)
        {
            IsLoaded = false;
        }
        return fresh;
    }

    private TaskDto? Apply(Action<TaskDto> mutate, string undoDescription, TaskDto? snapshot)
    {
        var current = Current();
        if (current is null) return null;

        var before = snapshot ?? current;
        mutate(current);
        var updated = _tasks.UpdateTask(current);
        _undo.Push(undoDescription, () =>
        {
            var latest = _tasks.GetTask(updated.Id);
            if (latest is not null) _tasks.UpdateTask(latest with
            {
                Title = before.Title,
                Note = before.Note,
                Status = before.Status,
                Priority = before.Priority,
                Importance = before.Importance,
                DueAt = before.DueAt,
                EstimateMinutes = before.EstimateMinutes,
                ProjectId = before.ProjectId,
            });
        });
        _model = updated;
        OnPropertyChanged(nameof(CreatedInfo));
        _owner.RefreshAfterEdit();
        return updated;
    }

    // ---- 字段变更(即时保存;批内首变更快照供整组撤销) ----

    partial void OnTitleChanged(string value)
    {
        if (!IsLoaded || _model is null || value == _model.Title) return;
        Apply(t => t.Title = value, $"修改标题「{_model.Title}」", snapshot: null);
    }

    partial void OnNoteChanged(string value)
    {
        if (!IsLoaded || _model is null || value == _model.Note) return;
        Apply(t => t.Note = value, "修改备注", snapshot: null);
    }

    partial void OnStatusIndexChanged(int value)
    {
        if (!IsLoaded || _model is null || value == (int)_model.Status) return;
        var snapshot = _model;
        Apply(t => t.Status = (TaskStatus)value, $"状态改为「{StatusNames[value]}」", snapshot);
    }

    partial void OnPriorityIndexChanged(int value)
    {
        if (!IsLoaded || _model is null || value == (int)_model.Priority) return;
        var snapshot = _model;
        Apply(t => t.Priority = (Priority)value, $"优先级改为「{PriorityNames[value]}」", snapshot);
    }

    partial void OnHasDueChanged(bool value)
    {
        if (!IsLoaded || _model is null) return;
        if (value && _model.DueAt.HasValue) return;
        if (!value && !_model.DueAt.HasValue) return;

        var snapshot = _model;
        if (!value)
        {
            Apply(t => t.DueAt = null, "清除截止时间", snapshot);
        }
    }

    public void CommitDue()
    {
        if (!IsLoaded || _model is null || !HasDue || DueDate is null) return;
        var snapshot = _model;
        var local = DueDate.Value;
        var due = new DateTimeOffset(local.Year, local.Month, local.Day,
            (int)DueTime.TotalHours, DueTime.Minutes, 0, local.Offset);
        if (_model.DueAt == due) return;
        Apply(t => t.DueAt = due, "修改截止时间", snapshot);
    }

    public void CommitEstimate()
    {
        if (!IsLoaded || _model is null) return;
        int? estimate = int.TryParse(EstimateText.Trim(), out var v) ? v : null;
        if (estimate == _model.EstimateMinutes) return;
        var snapshot = _model;
        Apply(t => t.EstimateMinutes = estimate, "修改预计时长", snapshot);
    }

    // ---- 标签 ----

    public bool IsTagAssigned(string tagId) => AssignedTagIds.Contains(tagId);

    public void ToggleTag(string tagId)
    {
        if (_model is null) return;
        if (AssignedTagIds.Contains(tagId))
        {
            _workspace.RemoveTagFromTask(_model.Id, tagId);
            AssignedTagIds.Remove(tagId);
        }
        else
        {
            _workspace.AddTagToTask(_model.Id, tagId);
            AssignedTagIds.Add(tagId);
        }
    }

    // ---- 检查项 ----

    public void AddChecklistItem(string content)
    {
        if (_model is null || string.IsNullOrWhiteSpace(content)) return;
        var item = _workspace.AddChecklistItem(_model.Id, content.Trim());
        Checklist.Add(item);
    }

    public void ToggleChecklistItem(ChecklistItemDto item)
    {
        var updated = _workspace.UpdateChecklistItem(item with { IsChecked = !item.IsChecked });
        ReplaceChecklist(updated);
    }

    public void RemoveChecklistItem(ChecklistItemDto item)
    {
        _workspace.RemoveChecklistItem(item.Id);
        Checklist.Remove(item);
    }

    private void ReplaceChecklist(ChecklistItemDto updated)
    {
        for (var i = 0; i < Checklist.Count; i++)
        {
            if (Checklist[i].Id == updated.Id)
            {
                Checklist[i] = updated;
                return;
            }
        }
    }
}
