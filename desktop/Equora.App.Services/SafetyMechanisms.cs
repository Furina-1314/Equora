namespace Equora.App.Services;

/// <summary>
/// 严格限制的安全白名单(需求 §12.3):以下应用永不被限制,
/// 无论用户如何配置黑名单。违反此原则的规则在匹配前被剥离。
/// </summary>
public static class SafetyWhitelist
{
    /// <summary>系统关键进程(可维护扩充;匹配方式与 AppRuleMatcher 一致:精确名或无扩展名)。</summary>
    public static readonly IReadOnlyList<string> AlwaysAllowed = new[]
    {
        // 系统设置与控制面板
        "systemsettings", "settings", "control", "ms-settings",
        // 安全软件
        "msmpeng", "msascuil", "securityhealthservice", "securityhealthsystray",
        "windowsdefender", "defender",
        // 文件管理器与命令行(恢复工具入口)
        "explorer", "cmd", "powershell", "pwsh", "windowsterminal",
        // 辅助功能
        "narrator", "magnify", "osk", "displayswitch", "atbroker",
        // 紧急通信
        "dialer", "phoneexperiencehost",
        // 恢复工具
        "taskmgr", "regedit", "msconfig", "eventvwr", "recoverydrive",
        // Equora 自身(避免自锁)
        "equora.app", "equora-server", "com.equora.nativehost",
    };

    /// <summary>判断进程是否受安全白名单保护(永不限)。</summary>
    public static bool IsProtected(string processName) =>
        AppRuleMatcher.IsBlocked(processName, AlwaysAllowed, Array.Empty<string>()) ||
        AlwaysAllowed.Any(a => Matches(processName, a));

    private static bool Matches(string processName, string rule)
    {
        var p = Path.GetFileName(processName).ToLowerInvariant();
        if (p.EndsWith(".exe", StringComparison.Ordinal)) p = p[..^4];
        var r = rule.ToLowerInvariant().Trim();
        return p == r || processName.ToLowerInvariant() == r;
    }

    /// <summary>过滤黑名单:剥离受保护进程,返回安全后的黑名单。</summary>
    public static IReadOnlyList<string> SanitizeBlockedList(
        IReadOnlyList<string> blocked) =>
        blocked.Where(b => !IsProtected(b)).ToList();
}

/// <summary>
/// 紧急解锁:所有限制的安全出口。触发后冷却期间内解除全部应用限制。
/// 冷却默认 15 分钟;触发记录留审计痕迹(供用户回顾而非惩罚)。
/// </summary>
public sealed class EmergencyUnlock
{
    private readonly DateTimeOffset _epoch;
    private DateTimeOffset? _unlockedUntil;

    public EmergencyUnlock(TimeSpan? cooldown = null)
    {
        _epoch = DateTimeOffset.Now;
        Cooldown = cooldown ?? TimeSpan.FromMinutes(15);
    }

    public TimeSpan Cooldown { get; }

    public bool IsActive(DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.Now;
        return _unlockedUntil.HasValue && t < _unlockedUntil.Value;
    }

    /// <summary>触发紧急解锁;返回是否生效(已在解锁期内时幂等返回 true)。</summary>
    public bool Trigger(DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.Now;
        if (IsActive(t))
        {
            TriggerCount++; // 审计:解锁期内再次触发也计数
            return true;
        }
        _unlockedUntil = t + Cooldown;
        TriggeredAt = t;
        TriggerCount++;
        return true;
    }

    public DateTimeOffset? TriggeredAt { get; private set; }
    public int TriggerCount { get; private set; }

    /// <summary>剩余解锁时间;未激活返回 TimeSpan.Zero。</summary>
    public TimeSpan Remaining(DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.Now;
        return IsActive(t) ? _unlockedUntil!.Value - t : TimeSpan.Zero;
    }
}

/// <summary>开机启动(注册表 HKCU Run;不写 HKLM 避免权限)。</summary>
public static class StartupRegistration
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Equora";

    public static bool IsRegistered()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>注册/取消开机启动(默认清晰告知用户,由设置页调用)。</summary>
    public static void SetRegistered(bool enable, string exePath)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
        {
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
