using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equora.App.NativeInterop;
using Equora.App.Services;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App.ViewModels;

public enum Quadrant
{
    First = 0,  // 重要且紧急
    Second = 1, // 重要不紧急
    Third = 2,  // 紧急不重要
    Fourth = 3, // 不重要不紧急
}

/// <summary>四象限视图模型:归类可解释、拖拽换象限、今日三件要事、容量提示。</summary>
public partial class MatrixViewModel : ObservableObject
{
    /// <summary>紧急判定窗口:已逾期或 N 天内到期(可解释规则,可配置)。</summary>
    public const int UrgentWithinDays = 3;
    public const int ImportantThreshold = 3; // importance >= 3 视为重要(1..5)
    public const string BigThreeTagName = "今日要事";

    private readonly ITaskService _tasks;
    private readonly IWorkspaceService _workspace;
    private readonly IUndoService _undo;

    public MatrixViewModel(ITaskService tasks, IWorkspaceService workspace, IUndoService undo)
    {
        _tasks = tasks;
        _workspace = workspace;
        _undo = undo;
    }

    public ObservableCollection<TaskDto> Q1 { get; } = new();
    public ObservableCollection<TaskDto> Q2 { get; } = new();
    public ObservableCollection<TaskDto> Q3 { get; } = new();
    public ObservableCollection<TaskDto> Q4 { get; } = new();
    public ObservableCollection<TaskDto> BigThree { get; } = new();

    [ObservableProperty]
    private string _statusText = "";

    public int Q1Minutes => Q1.Sum(EstimateOf);
    public int Q2Minutes => Q2.Sum(EstimateOf);

    /// <summary>单任务象限判定(纯函数,可解释):重要=用户标记;紧急=截止临近。</summary>
    public static Quadrant QuadrantOf(TaskDto task, DateTimeOffset now)
    {
        var important = task.Importance >= ImportantThreshold;
        var urgent = task.DueAt is not null && task.DueAt.Value.ToLocalTime().Date
            <= now.ToLocalTime().Date.AddDays(UrgentWithinDays - 1);
        return (important, urgent) switch
        {
            (true, true) => Quadrant.First,
            (true, false) => Quadrant.Second,
            (false, true) => Quadrant.Third,
            _ => Quadrant.Fourth,
        };
    }

    private static int EstimateOf(TaskDto t) => t.EstimateMinutes ?? 0;

    [RelayCommand]
    public void Refresh(DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        var open = _tasks.QueryTasks(new TaskQuery
        {
            Statuses = new[] { TaskStatus.Inbox, TaskStatus.Planned, TaskStatus.InProgress,
                               TaskStatus.Waiting },
            Sort = TaskSort.PriorityDesc,
        });

        Q1.Clear(); Q2.Clear(); Q3.Clear(); Q4.Clear();
        foreach (var t in open)
        {
            TargetOf(QuadrantOf(t, reference)).Add(t);
        }

        LoadBigThree();
        OnPropertyChanged(nameof(Q1Minutes));
        OnPropertyChanged(nameof(Q2Minutes));
        StatusText = $"Q1×{Q1.Count}({Q1Minutes / 60}h) Q2×{Q2.Count}({Q2Minutes / 60}h) " +
                     $"Q3×{Q3.Count} Q4×{Q4.Count} · 要事 {BigThree.Count}/3";
    }

    private ObservableCollection<TaskDto> TargetOf(Quadrant q) => q switch
    {
        Quadrant.First => Q1,
        Quadrant.Second => Q2,
        Quadrant.Third => Q3,
        _ => Q4,
    };

    /// <summary>拖拽换象限:按目标象限改重要性/截止(可解释、可撤销)。</summary>
    public void MoveToQuadrant(TaskDto task, Quadrant target)
    {
        var fresh = _tasks.GetTask(task.Id);
        if (fresh is null) return;

        var updated = fresh;
        switch (target)
        {
            case Quadrant.First:
                updated = updated with { Importance = Math.Max(updated.Importance, 4) };
                updated = EnsureUrgent(updated);
                break;
            case Quadrant.Second:
                updated = PushBeyondUrgentWindow(updated with
                { Importance = Math.Max(updated.Importance, 4) });
                break;
            case Quadrant.Third:
                updated = updated with { Importance = Math.Min(updated.Importance, 2) };
                updated = EnsureUrgent(updated);
                break;
            case Quadrant.Fourth:
                updated = PushBeyondUrgentWindow(updated with
                { Importance = Math.Min(updated.Importance, 2) });
                break;
        }

        var before = fresh;
        var saved = _tasks.UpdateTask(updated);
        _undo.Push($"移动「{fresh.Title}」到{QuadrantName(target)}", () =>
        {
            var latest = _tasks.GetTask(saved.Id);
            if (latest is not null)
            {
                _tasks.UpdateTask(latest with
                {
                    Importance = before.Importance,
                    DueAt = before.DueAt,
                });
            }
        });
        Refresh();
    }

    private static TaskDto EnsureUrgent(TaskDto t)
    {
        var now = DateTimeOffset.Now.ToLocalTime();
        if (t.DueAt is not null &&
            t.DueAt.Value.ToLocalTime().Date <= now.Date.AddDays(UrgentWithinDays - 1))
        {
            return t; // 已在紧急窗口内
        }
        return t with { DueAt = new DateTimeOffset(now.Date.AddDays(1).AddHours(18), now.Offset) };
    }

    /// 紧急窗口内的截止在 Q2/Q4 清除(从容安排/减少投入),而不是悄悄顺延。
    private static TaskDto PushBeyondUrgentWindow(TaskDto t)
    {
        if (t.DueAt is null) return t;
        var now = DateTimeOffset.Now.ToLocalTime();
        if (t.DueAt.Value.ToLocalTime().Date > now.Date.AddDays(UrgentWithinDays - 1)) return t;
        return t with { DueAt = null };
    }

    public static string QuadrantName(Quadrant q) => q switch
    {
        Quadrant.First => "第一象限(重要且紧急)",
        Quadrant.Second => "第二象限(重要不紧急)",
        Quadrant.Third => "第三象限(紧急不重要)",
        _ => "第四象限(不重要不紧急)",
    };

    // ---- 今日三件要事 ----

    private void LoadBigThree()
    {
        BigThree.Clear();
        var tag = EnsureBigThreeTag();
        var now = DateTimeOffset.Now;
        foreach (var t in _tasks.QueryTasks(new TaskQuery { TagId = tag.Id }))
        {
            if (QuadrantOf(t, now) is Quadrant.First or Quadrant.Second)
            {
                BigThree.Add(t);
            }
        }
    }

    private TagDto EnsureBigThreeTag() =>
        _workspace.ListTags().FirstOrDefault(t => t.Name == BigThreeTagName)
        ?? _workspace.CreateTag(BigThreeTagName, "#E2A03F");

    public string ToggleBigThree(TaskDto task)
    {
        var tag = EnsureBigThreeTag();
        if (BigThree.Any(t => t.Id == task.Id))
        {
            _workspace.RemoveTagFromTask(task.Id, tag.Id);
            LoadBigThree();
            return $"已从要事移除:{task.Title}";
        }
        if (BigThree.Count >= 3)
        {
            return "今日要事已满 3 件,先移除一件再添加。";
        }
        _workspace.AddTagToTask(task.Id, tag.Id);
        LoadBigThree();
        StatusText = $"要事 {BigThree.Count}/3";
        return $"已设为今日要事:{task.Title}";
    }

    [RelayCommand]
    public void Undo()
    {
        var description = _undo.Undo();
        StatusText = description is null ? "没有可撤销的操作" : $"已撤销:{description}";
        Refresh();
    }
}
