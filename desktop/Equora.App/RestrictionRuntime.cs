using Equora.App.Services;
using Equora.App.NativeInterop;
using Microsoft.UI.Xaml;

namespace Equora.App;

internal sealed class RestrictionRuntime : IDisposable
{
    public static RestrictionRuntime? Current { get; private set; }
    public RestrictionStore Store { get; } = new(AppPaths.DataDirectory);
    public RestrictionConfiguration Configuration { get; private set; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Action<string> _notify;
    private readonly Dictionary<string, double> _pending = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _last = DateTimeOffset.Now;
    private DateTimeOffset _lastSave = DateTimeOffset.Now;
    private string? _lastProcess;
    private bool _degraded;
    private readonly RestrictionPause _pause = new();
    private readonly ForegroundOverlays _overlays;
    private readonly WindowProcessCache _windowProcesses = new();
    private IntPtr _lastNudgeHost;
    private string? _lastNudge;
    private DateTimeOffset? _allowUntil => _pause.Until;
    public event Action? PauseChanged;
    public TimeSpan PauseRemaining => _pause.Remaining(DateTimeOffset.Now);
    public string Status { get; private set; } = "限制服务已启动";

    public RestrictionRuntime(Action<string> notify)
    {
        Current = this;
        _notify = notify;
        Configuration = Store.Load();
        _overlays = new ForegroundOverlays(GrantAppAllowance);
        _overlays.SyncRequested += OnOverlaySyncRequested;
        _timer.Tick += OnTick;
        _timer.Start();
        Store.Pulse(false, null);
    }

    public void Save(RestrictionConfiguration configuration)
    {
        Store.Save(configuration);
        Configuration = configuration;
    }
    public void AllowTemporarily()
    {
        _pause.Start(DateTimeOffset.Now);
        Store.Pulse(false, _allowUntil);
        Status = "所有限制已暂停 15 分钟";
        PauseChanged?.Invoke();
    }
    public double Usage(UsageRule rule) => Store.Usage(rule.Kind, DateTimeOffset.Now).GetValueOrDefault(rule.Target) +
        (rule.Kind == "app" ? _pending.GetValueOrDefault(rule.Target) : 0);

    private void GrantAppAllowance(string process)
    {
        var target = Configuration.Rules
            .Where(r => r.Enabled && r.Kind == "app" && RestrictionPolicy.Matches("app", r.Target, process))
            .Select(r => r.Target).FirstOrDefault();
        if (target is null) return;
        try
        {
            if (Store.GrantAppAllowance(target, DateTimeOffset.Now) is null) return;
            Status = $"{process} 已临时允许 5 分钟";
            OnTick(null, new object());
        }
        catch (Exception ex) { _notify($"临时允许失败：{ex.Message}"); }
    }

    private void OnOverlaySyncRequested()
    {
        // WinEvent 钩子(新窗口出现/前台切换)请求立即重算遮罩;异常留给秒级轮询兜底。
        try { SyncOverlayWindows(_lastNudgeHost, _lastNudge); }
        catch (Exception ex) { Status = $"限制服务暂不可用：{ex.Message}"; }
    }

    private void OnTick(object? sender, object e)
    {
        var now = DateTimeOffset.Now;
        if (_pause.TryExpire(now))
        {
            Status = Configuration.Enabled ? "暂停已结束，限制已恢复" : "暂停已结束，限制服务未启用";
            PauseChanged?.Invoke();
        }
        try
        {
            var focusing = AppServices.FocusVm.Session?.State == SessionStateDto.Running;
            Store.Pulse(focusing, _allowUntil);
            if (_degraded) { _degraded = false; Status = Configuration.Enabled ? "限制服务已恢复" : "限制服务未启用"; }
            var rules = Configuration.Rules.Where(r => r.Enabled && r.Kind == "app").ToList();
            var (window, process) = ForegroundAccess.Current();
            var foregroundIsBlock = _overlays.IsBlockWindow(window);
            if (_overlays.Owns(window))
            {
                var host = _overlays.HostOf(window);
                if (host != IntPtr.Zero) { window = host; process = _overlays.ProcessOf(host); }
            }
            if (process is null || window == IntPtr.Zero || SafetyWhitelist.IsProtected(process))
            { _overlays.Sync(Array.Empty<(IntPtr, ForegroundOverlays.TargetState)>(), IntPtr.Zero, null); _lastProcess = process; return; }
            string? nudge = null;
            if (Appearance.Current.ActivityMonitoring && focusing)
            {
                if (AppRuleMatcher.IsBlocked(process, Array.Empty<string>(), Appearance.Current.BlockedApps.Split(',', StringSplitOptions.RemoveEmptyEntries)))
                {
                    if (process != _lastProcess) AppServices.FocusVm.ReportBlockedApp(process);
                    nudge = AppServices.FocusVm.NudgeText;
                }
                else AppServices.FocusVm.ClearNudge();
            }
            else AppServices.FocusVm.ClearNudge();
            _lastNudge = nudge;
            _lastNudgeHost = window;
            if (!Configuration.Enabled || rules.Count == 0)
            { _overlays.Sync(Array.Empty<(IntPtr, ForegroundOverlays.TargetState)>(), window, nudge); _lastProcess = process; return; }
            if (now.Date != _last.Date) { Flush(_last); _pending.Clear(); }
            var elapsed = Math.Clamp((now - _last).TotalSeconds, 0, 2);
            foreach (var target in rules.Select(r => r.Target).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!foregroundIsBlock && ForegroundAccess.HasRecentInput() && process == _lastProcess && RestrictionPolicy.Matches("app", target, process))
                    _pending[target] = _pending.GetValueOrDefault(target) + elapsed;
            _lastProcess = process;
            if (now - _lastSave >= TimeSpan.FromSeconds(5)) Flush(now);
            SyncOverlayWindows(window, nudge);
        }
        catch (Exception ex) { _degraded = true; Status = $"限制服务暂不可用：{ex.Message}"; _overlays.Hide(); }
        finally { _last = now; }
    }

    // 逐目标求值:是否封锁、封锁原因、临时允许状态与剩余额度。全部命中窗口(不限于前台)共用这份结果。
    private Dictionary<string, (string? Reason, bool Used, DateTimeOffset? Until, double? Quota)> EvaluateTargets(
        List<UsageRule> rules, DateTimeOffset now, bool focusing)
    {
        var decisions = new Dictionary<string, (string? Reason, bool Used, DateTimeOffset? Until, double? Quota)>(StringComparer.OrdinalIgnoreCase);
        var allowances = new Dictionary<string, (DateTimeOffset Until, bool Used)>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var state = decisions.TryGetValue(rule.Target, out var existing) ? existing : (null, false, null, null);
            if (rule.DailyMinutes > 0)
                state.Quota = Math.Min(state.Quota ?? double.PositiveInfinity, Math.Max(0, rule.DailyMinutes * 60 - Usage(rule)));
            if (!allowances.TryGetValue(rule.Target, out var allowance))
            {
                var stored = Store.AppAllowance(rule.Target, now);
                allowance = (stored.Until ?? DateTimeOffset.MinValue, stored.Used);
                allowances[rule.Target] = allowance;
            }
            if (allowance.Until > now && allowance.Until > (state.Until ?? DateTimeOffset.MinValue)) state.Until = allowance.Until;
            // 注意:可空比较 now >= _allowUntil 在暂停未启用(为 null)时为 false,必须先归一化。
            var pausedUntil = _allowUntil ?? DateTimeOffset.MinValue;
            if (now >= pausedUntil && allowance.Until <= now)
            {
                var reason = RestrictionPolicy.Reason(rule, now, focusing, Usage(rule));
                if (reason is not null && state.Reason is null)
                {
                    state.Reason = reason;
                    state.Used = allowance.Used;
                    Status = $"{rule.Name}：{reason}";
                }
            }
            decisions[rule.Target] = state;
        }
        return decisions;
    }

    private List<(IntPtr Window, ForegroundOverlays.TargetState State)> CollectOverlayTargets(
        Dictionary<string, (string? Reason, bool Used, DateTimeOffset? Until, double? Quota)> decisions)
    {
        var result = new List<(IntPtr, ForegroundOverlays.TargetState)>();
        foreach (var hwnd in ForegroundAccess.VisibleTopWindows())
        {
            if (_overlays.Owns(hwnd)) continue;
            var process = _windowProcesses.Resolve(hwnd);
            if (process is null || SafetyWhitelist.IsProtected(process)) continue;
            var target = decisions.Keys.FirstOrDefault(key => RestrictionPolicy.Matches("app", key, process));
            if (target is null) continue;
            var state = decisions[target];
            result.Add((hwnd, new ForegroundOverlays.TargetState(process, state.Reason, state.Used, state.Until, state.Quota)));
        }
        return result;
    }

    private void SyncOverlayWindows(IntPtr nudgeHost, string? nudge)
    {
        var rules = Configuration.Rules.Where(r => r.Enabled && r.Kind == "app").ToList();
        if (!Configuration.Enabled || rules.Count == 0)
        { _overlays.Sync(Array.Empty<(IntPtr, ForegroundOverlays.TargetState)>(), nudgeHost, nudge); return; }
        var decisions = EvaluateTargets(rules, DateTimeOffset.Now, AppServices.FocusVm.Session?.State == SessionStateDto.Running);
        _overlays.Sync(CollectOverlayTargets(decisions), nudgeHost, nudge);
    }

    private void Flush(DateTimeOffset now)
    {
        if (_pending.Count > 0) Store.AddUsage("app", _pending, now);
        _pending.Clear();
        _lastSave = now;
    }
    public void FlushUsage() => Flush(DateTimeOffset.Now);
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _overlays.Dispose();
        try { Flush(_last); Store.Pulse(false, DateTimeOffset.Now.AddMinutes(1)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Current = null;
    }
}
