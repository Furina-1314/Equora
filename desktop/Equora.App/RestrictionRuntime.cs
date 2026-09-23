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
    private string? _overlayProcess;
    private string? _blockedTarget;
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
        if (_blockedTarget is null || !RestrictionPolicy.Matches("app", _blockedTarget, process)) return;
        try
        {
            if (Store.GrantAppAllowance(_blockedTarget, DateTimeOffset.Now) is null) return;
            Status = $"{process} 已临时允许 5 分钟";
            OnTick(null, new object());
        }
        catch (Exception ex) { _notify($"临时允许失败：{ex.Message}"); }
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
            if (_overlays.Owns(window) && _overlays.TargetWindow != IntPtr.Zero)
            { window = _overlays.TargetWindow; process = _overlayProcess; }
            if (process is null || window == IntPtr.Zero || SafetyWhitelist.IsProtected(process))
            { _overlays.Hide(); _overlayProcess = null; _lastProcess = process; return; }
            _overlayProcess = process;
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
            if (!Configuration.Enabled || rules.Count == 0)
            { _overlays.Update(window, process, null, false, null, nudge, null); _lastProcess = process; return; }
            if (now.Date != _last.Date) { Flush(_last); _pending.Clear(); }
            var elapsed = Math.Clamp((now - _last).TotalSeconds, 0, 2);
            foreach (var target in rules.Select(r => r.Target).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!_overlays.IsBlocking && ForegroundAccess.HasRecentInput() && process == _lastProcess && RestrictionPolicy.Matches("app", target, process))
                    _pending[target] = _pending.GetValueOrDefault(target) + elapsed;
            _lastProcess = process;
            if (now - _lastSave >= TimeSpan.FromSeconds(5)) Flush(now);
            string? blockedReason = null;
            var allowanceUsed = false;
            DateTimeOffset? allowanceUntil = null;
            _blockedTarget = null;
            double? quota = null;
            foreach (var rule in rules.Where(r => RestrictionPolicy.Matches("app", r.Target, process)))
            {
                if (rule.DailyMinutes > 0)
                    quota = Math.Min(quota ?? double.PositiveInfinity, Math.Max(0, rule.DailyMinutes * 60 - Usage(rule)));
                var allowance = Store.AppAllowance(rule.Target, now);
                if (allowance.Until > now) allowanceUntil = allowance.Until;
                if (now < _allowUntil || allowance.Until > now) continue;
                var reason = RestrictionPolicy.Reason(rule, now, focusing, Usage(rule));
                if (reason is null) continue;
                blockedReason = reason;
                allowanceUsed = allowance.Used;
                _blockedTarget = rule.Target;
                Status = $"{rule.Name}：{reason}";
                break;
            }
            _overlays.Update(window, process, blockedReason, allowanceUsed, allowanceUntil, nudge, quota);
        }
        catch (Exception ex) { _degraded = true; Status = $"限制服务暂不可用：{ex.Message}"; _overlays.Hide(); }
        finally { _last = now; }
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
