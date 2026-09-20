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
    private DateTimeOffset? _pausedAt;
    [ObservableProperty] private int _rounds = 4;
    [ObservableProperty] private int _breakMinutes = 5;
    [ObservableProperty] private bool _isBreak;
    private bool _breakPaused, _continuing;
    private DateTimeOffset _breakEnd;
    private TimeSpan _breakLeft;
    private int _round = 1, _cycleRounds = 4, _cycleBreak = 5;
    public string RoundStatus => ModeIndex == 0 ? $"第 {_round} / {(IsRunning ? _cycleRounds : Rounds)} 轮 · {(IsBreak ? "休息" : "专注")}" : ModeDescription;
    public string ModeDescription => ModeIndex switch { 0 => "番茄钟：按设定轮数自动交替专注和休息，最后一轮结束后停止。", 1 => "深度工作：单次倒计时，到时结束，不自动安排休息。", _ => "正计时：从零累计有效专注时间，由你手动结束。" };
    public bool IsPomodoro => ModeIndex == 0;
    public bool IsTimed => ModeIndex != 2;
    partial void OnModeIndexChanged(int value) { NotifyPhase(); OnPropertyChanged(nameof(IsPomodoro)); OnPropertyChanged(nameof(IsTimed)); OnPropertyChanged(nameof(ModeDescription)); }
    partial void OnRoundsChanged(int value) => OnPropertyChanged(nameof(RoundStatus));
    partial void OnIsBreakChanged(bool value) => NotifyPhase();
    private void NotifyPhase()
    {
        OnPropertyChanged(nameof(ClockText)); OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(SessionHint));
        OnPropertyChanged(nameof(PrimaryActionText)); OnPropertyChanged(nameof(PrimaryActionGlyph)); OnPropertyChanged(nameof(RoundStatus));
    }

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
        "番茄钟", "深度工作", "正计时",
    };

    // ---- 派生状态 ----

    public bool IsRunning => IsBreak || Session is { State: SessionStateDto.Running or SessionStateDto.Paused };

    public string ClockText
    {
        get
        {
            if (IsBreak) return Format(TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((_breakPaused ? _breakLeft : _breakEnd - Clock).TotalSeconds))));
            if (Session is not { } s) return ModeIndex == 2 ? "00:00" : Format(TimeSpan.FromMinutes(PlannedMinutes));
            var displayClock = s.State == SessionStateDto.Paused ? _pausedAt ?? Clock : Clock;
            var effective = s.Effective(displayClock);
            if (s.PlannedEnd is { } end && s.Mode is FocusModeDto.Pomodoro or FocusModeDto.Deep)
            {
                var left = end.ToLocalTime() - displayClock.ToLocalTime() + s.Paused;
                return left > TimeSpan.Zero ? Format(TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds))) : "00:00";
            }
            return Format(effective); // Flowtime/正计时/无计时:正向累计
        }
    }

    public string PrimaryActionText => IsBreak ? (_breakPaused ? "继续" : "暂停") : Session?.State switch
    {
        SessionStateDto.Running => "暂停", SessionStateDto.Paused => "继续", _ => "开始"
    };
    public string PrimaryActionGlyph => Session?.State == SessionStateDto.Running || IsBreak && !_breakPaused ? "\uE769" : "\uE768";
    [RelayCommand]
    public void ToggleSession()
    {
        if (IsBreak)
        {
            if (_breakPaused) _breakEnd = Clock + _breakLeft; else _breakLeft = _breakEnd - Clock;
            _breakPaused = !_breakPaused; NotifyPhase(); return;
        }
        if (Session?.State == SessionStateDto.Running) Pause();
        else if (Session?.State == SessionStateDto.Paused) Resume();
        else Start();
    }
    partial void OnPlannedMinutesChanged(int value) => OnPropertyChanged(nameof(ClockText));

    public string SessionHint => IsBreak ? (_breakPaused ? "休息已暂停" : "休息中，到时自动开始下一轮") : Session switch
    {
        { State: SessionStateDto.Paused } => "已暂停",
        { Mode: FocusModeDto.Flowtime } => "自由专注 · 完成时记录",
        { Mode: FocusModeDto.Untimed } => "沉浸模式 · 无计时",
        null => "选择模式与时长,开始专注",
        _ => "专注中",
    };

    private static string Format(TimeSpan t) => t.TotalHours >= 1
        ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
        : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    partial void OnSessionChanged(FocusSessionDto? value)
    {
        if (value?.State == SessionStateDto.Paused) _pausedAt ??= Clock;
        else _pausedAt = null;
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(PrimaryActionGlyph));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ClockText));
        OnPropertyChanged(nameof(SessionHint));
    }

    partial void OnClockChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(ClockText));
        if (IsBreak && !_breakPaused && value >= _breakEnd)
        {
            IsBreak = false; _round++; _continuing = true;
            try { Start(); } finally { _continuing = false; }
            NotifyPhase();
        }
        else if (Session is { State: SessionStateDto.Running, PlannedEnd: { } end } s &&
                 s.Mode is FocusModeDto.Pomodoro or FocusModeDto.Deep && value >= end + s.Paused)
        {
            var pomodoro = s.Mode == FocusModeDto.Pomodoro;
            _focus.CompleteFocus(s.Id, "计时完成", -1, now: end + s.Paused);
            RestoreTaskStatus(); Session = null;
            if (pomodoro && _round < _cycleRounds)
            { _breakPaused = false; _breakLeft = TimeSpan.FromMinutes(_cycleBreak); _breakEnd = value + _breakLeft; IsBreak = true; StatusText = "本轮完成，开始休息。"; }
            else StatusText = pomodoro ? $"已完成全部 {_cycleRounds} 轮番茄钟。" : "深度工作计时完成。";
            NotifyPhase();
        }
    }

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
        var today = Clock.ToLocalTime().Date;
        var from = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(Clock));
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
            if (IsRunning) return;
            if (!_continuing) { _round = 1; _cycleRounds = Math.Clamp(Rounds, 1, 20); _cycleBreak = Math.Clamp(BreakMinutes, 1, 60); }
            var mode = ModeIndex == 2 ? FocusModeDto.Stopwatch : (FocusModeDto)ModeIndex;
            var minutes = ModeIndex == 2 ? 0 : PlannedMinutes;
            Session = _focus.StartFocus(mode, minutes, Goal, SelectedTaskId, now: Clock);
            NotifyPhase();
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
        if (IsBreak) { IsBreak = false; StatusText = $"番茄钟已结束。今日有效专注 {(int)TodayFocusMinutes().TotalMinutes} 分钟。"; return; }
        if (Session is null) return;
        var closed = _focus.CompleteFocus(Session.Id, note, -1, now: Clock);
        var effective = closed.Effective();
        RestoreTaskStatus();
        Session = null;
        var today = TodayFocusMinutes();
        StatusText = $"完成:有效专注 {(int)effective.TotalMinutes} 分钟 · 今日累计 " +
                     $"{(int)today.TotalMinutes} 分钟。建议休息 5 分钟。";
    }

    [RelayCommand]
    public void Abandon()
    {
        if (IsBreak) { IsBreak = false; StatusText = "番茄钟已结束。"; return; }
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
        ModeIndex = p.Mode is FocusModeDto.Pomodoro or FocusModeDto.Deep ? (int)p.Mode : 2;
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
