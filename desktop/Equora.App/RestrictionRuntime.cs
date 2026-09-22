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
    private DateTimeOffset? _allowUntil => _pause.Until;
    public event Action? PauseChanged;
    public TimeSpan PauseRemaining => _pause.Remaining(DateTimeOffset.Now);
    public string Status { get; private set; } = "限制服务已启动";

    public RestrictionRuntime(Action<string> notify)
    {
        Current = this;
        _notify = notify;
        Configuration = Store.Load();
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
            if (Appearance.Current.ActivityMonitoring && focusing && process != _lastProcess)
            {
                if (process is not null && AppRuleMatcher.IsBlocked(process, Array.Empty<string>(), Appearance.Current.BlockedApps.Split(',', StringSplitOptions.RemoveEmptyEntries)))
                { AppServices.FocusVm.ReportBlockedApp(process); _notify(AppServices.FocusVm.NudgeText); }
                else AppServices.FocusVm.ClearNudge();
            }
            if (!Configuration.Enabled || rules.Count == 0) { _lastProcess = process; return; }
            if (now.Date != _last.Date) { Flush(_last); _pending.Clear(); }
            var elapsed = Math.Clamp((now - _last).TotalSeconds, 0, 2);
            foreach (var target in rules.Select(r => r.Target).Distinct(StringComparer.OrdinalIgnoreCase))
                if (process is not null && process == _lastProcess && RestrictionPolicy.Matches("app", target, process))
                    _pending[target] = _pending.GetValueOrDefault(target) + elapsed;
            _lastProcess = process;
            if (now - _lastSave >= TimeSpan.FromSeconds(5)) Flush(now);
            if (process is null || SafetyWhitelist.IsProtected(process) || now < _allowUntil) return;
            foreach (var rule in rules.Where(r => RestrictionPolicy.Matches("app", r.Target, process)))
            {
                var reason = RestrictionPolicy.Reason(rule, now, focusing, Usage(rule));
                if (reason is null) continue;
                ForegroundAccess.Minimize(window);
                Status = $"{rule.Name}：{reason}";
                _notify(Status);
                break;
            }
        }
        catch (Exception ex) { _degraded = true; Status = $"限制服务暂不可用：{ex.Message}"; }
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
        try { Flush(_last); Store.Pulse(false, DateTimeOffset.Now.AddMinutes(1)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Current = null;
    }
}
