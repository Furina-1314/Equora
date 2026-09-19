using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Equora.App.NativeInterop;
using Equora.App.Services;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

/// <summary>专注页视图模型:会话状态机 UI 绑定 + 计时 + 分心捕获 + 今日统计。</summary>
public partial class FocusViewModel : ObservableObject
{
    private readonly IFocusService _focus;
    private readonly ITaskService _tasks;

    public FocusViewModel(IFocusService focus, ITaskService tasks)
    {
        _focus = focus;
        _tasks = tasks;
        _clock = DateTimeOffset.Now;
    }

    [ObservableProperty]
    private FocusSessionDto? _session;

    /// <summary>UI 时钟(页面计时器每秒推进;测试可注入)。</summary>
    [ObservableProperty]
    private DateTimeOffset _clock;

    [ObservableProperty]
    private int _modeIndex; // 0 番茄 1 深度 2 Flowtime 3 正计时 4 无计时

    [ObservableProperty]
    private int _plannedMinutes = 25;

    [ObservableProperty]
    private string _goal = "";

    [ObservableProperty]
    private string? _selectedTaskId;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _nudgeText = ""; // 温和提醒(前台检测)

    public ObservableCollection<TaskDto> CandidateTasks { get; } = new();

    public ObservableCollection<DistractionDto> Pending { get; } = new();

    public ObservableCollection<FocusProfileDto> Profiles { get; } = new();

    [ObservableProperty]
    private FocusProfileDto? _selectedProfile;

    public IReadOnlyList<string> ModeNames { get; } = new[]
    {
        "番茄钟", "深度工作", "Flowtime", "正计时", "无计时",
    };

    // ---- 派生状态 ----

    public bool IsRunning => Session is { State: SessionStateDto.Running or SessionStateDto.Paused };

    public string ClockText
    {
        get
        {
            if (Session is not { } s) return "25:00";
            var effective = s.Effective(Clock);
            if (s.PlannedEnd is { } end && s.Mode is FocusModeDto.Pomodoro or FocusModeDto.Deep)
            {
                var left = end.ToLocalTime() - Clock.ToLocalTime() + s.Paused;
                return left > TimeSpan.Zero ? Format(left) : "00:00";
            }
            return Format(effective); // Flowtime/正计时/无计时:正向累计
        }
    }

    public string SessionHint => Session switch
    {
        { State: SessionStateDto.Paused } => "已暂停",
        { Mode: FocusModeDto.Flowtime } => "自由专注 · 完成时记录",
        { Mode: FocusModeDto.Untimed } => "沉浸模式 · 无计时",
        null => "选择模式与时长,开始专注",
        _ => "专注中",
    };

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    partial void OnSessionChanged(FocusSessionDto? value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(SessionHint));
    }

    partial void OnClockChanged(DateTimeOffset value) => OnPropertyChanged(nameof(ClockText));

    // ---- 初始化 ----

    public void Load()
    {
        Session = _focus.OpenFocus();
        CandidateTasks.Clear();
        foreach (var t in _tasks.QueryTasks(new TaskQuery
                 {
                     Statuses = new[]
                     {
                         TaskStatus.Inbox, TaskStatus.Planned, TaskStatus.InProgress,
                     },
                     Sort = TaskSort.PriorityDesc,
                     Limit = 20,
                 }))
        {
            CandidateTasks.Add(t);
        }

        Profiles.Clear();
        foreach (var p in _focus.ListFocusProfiles()) Profiles.Add(p);

        Pending.Clear();
        foreach (var d in _focus.PendingDistractions(50)) Pending.Add(d);

        if (Session is { TaskId: { } taskId })
        {
            SelectedTaskId = taskId;
        }
        StatusText = Session is null
            ? "未在专注。今日已专注 " + (int)TodayFocusMinutes().TotalMinutes + " 分钟"
            : $"恢复会话:{SessionHint}";
    }

    public TimeSpan TodayFocusMinutes()
    {
        var today = DateTimeOffset.Now.ToLocalTime().Date;
        var from = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now));
        return _focus.FocusHistory(from, from.AddDays(1))
            .Where(s => s.State == SessionStateDto.Completed)
            .Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Effective());
    }

    // ---- 会话命令 ----

    [RelayCommand]
    public void Start()
    {
        try
        {
            var mode = (FocusModeDto)ModeIndex;
            var minutes = ModeIndex == 2 || ModeIndex == 4 ? 0 : PlannedMinutes;
            Session = _focus.StartFocus(mode, minutes, Goal, SelectedTaskId, now: Clock);
            if (SelectedTaskId is not null)
            {
                var task = _tasks.GetTask(SelectedTaskId);
                if (task is { Status: TaskStatus.Inbox })
                {
                    _tasks.UpdateTask(task with { Status = TaskStatus.InProgress });
                }
            }
            StatusText = "专注开始。" + (Session.PlannedEnd is { }
                ? $"计划 {(int)(Session.PlannedEnd - Session.PlannedStart)!.Value.TotalMinutes} 分钟"
                : "自由模式");
        }
        catch (EquoraException ex)
        {
            StatusText = $"无法开始:{ex.Message}";
        }
    }

    [RelayCommand]
    public void Pause() => Wrap("已暂停", () =>
        Session = _focus.PauseFocus(Session!.Id, now: Clock));

    [RelayCommand]
    public void Resume() => Wrap("继续", () =>
        Session = _focus.ResumeFocus(Session!.Id, now: Clock));

    [RelayCommand]
    public void Complete(string note = "")
    {
        if (Session is null) return;
        var effective = Session.Effective(Clock);
        var closed = _focus.CompleteFocus(Session.Id, note, -1, now: Clock);
        RestoreTaskStatus();
        Session = null;
        var today = TodayFocusMinutes();
        StatusText = $"完成:有效专注 {(int)effective.TotalMinutes} 分钟 · 今日累计 " +
                     $"{(int)today.TotalMinutes} 分钟。建议休息 5 分钟 🌿";
    }

    [RelayCommand]
    public void Abandon()
    {
        if (Session is null) return;
        _focus.AbandonFocus(Session.Id, now: Clock);
        RestoreTaskStatus();
        Session = null;
        StatusText = "已放弃本会话";
    }

    private void RestoreTaskStatus()
    {
        if (Session?.TaskId is not { } taskId) return;
        var task = _tasks.GetTask(taskId);
        if (task is { Status: TaskStatus.InProgress })
        {
            _tasks.UpdateTask(task with { Status = TaskStatus.Planned });
        }
    }

    private void Wrap(string ok, Action action)
    {
        if (Session is null) return;
        try
        {
            action();
            StatusText = ok;
        }
        catch (EquoraException ex)
        {
            StatusText = ex.Message;
        }
    }

    // ---- 分心捕获(即时落库;整理入口) ----

    public DistractionDto? Capture(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var d = _focus.CaptureDistraction(content.Trim(), Session?.Id, now: Clock);
        Pending.Insert(0, d);
        StatusText = "已捕获(不切焦点)";
        return d;
    }

    /// <summary>整理为任务:resolution=1,创建真实任务并关联。</summary>
    public TaskDto? ResolveAsTask(DistractionDto d)
    {
        var task = _tasks.CreateTask(new TaskDraft
        {
            Title = d.Content,
            Status = TaskStatus.Inbox,
        });
        _focus.ResolveDistraction(d.Id, 1, task.Id);
        Pending.Remove(d);
        StatusText = $"已转为任务:{task.Title}";
        return task;
    }

    [RelayCommand]
    public void Discard(DistractionDto? d)
    {
        if (d is null) return;
        _focus.ResolveDistraction(d.Id, 5, "");
        Pending.Remove(d);
    }

    // ---- 预设 ----

    [RelayCommand]
    public void ApplyProfile(FocusProfileDto? p)
    {
        if (p is null) return;
        ModeIndex = (int)p.Mode;
        PlannedMinutes = p.PlannedMinutes;
        SelectedProfile = p;
        StatusText = $"已应用预设「{p.Name}」";
    }

    // ---- 前台检测(温和提醒) ----

    /// <summary>由 AppMonitor 调用:受限应用出现时给出温和文案并记录中断。</summary>
    public void ReportBlockedApp(string processName)
    {
        if (Session is not { State: SessionStateDto.Running }) return;
        NudgeText = $"「{processName}」不在专注白名单内 —— 回到任务,或允许 5 分钟";
        _focus.AddInterruption(Session.Id, Clock, TimeSpan.Zero, processName,
            "app-switch", "提醒");
    }

    public void ClearNudge() => NudgeText = "";
}
