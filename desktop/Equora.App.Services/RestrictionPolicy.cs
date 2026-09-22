using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Equora.App.Services;

public sealed record UsageRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "app";
    public string Name { get; init; } = "";
    public string Target { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool DuringFocus { get; init; }
    public bool HasSchedule { get; init; }
    public int StartMinute { get; init; } = 9 * 60;
    public int EndMinute { get; init; } = 18 * 60;
    public int DailyMinutes { get; init; }
    public string Description => $"{(Kind == "app" ? "应用" : "网站")} · {Target} · " +
        string.Join(" / ", new[] { DuringFocus ? "专注时限制" : null,
            HasSchedule ? $"每天 {StartMinute / 60:00}:{StartMinute % 60:00}–{EndMinute / 60:00}:{EndMinute % 60:00}" : null,
            DailyMinutes > 0 ? $"每日 {DailyMinutes} 分钟" : null }.Where(s => s is not null));
}

public sealed record RestrictionConfiguration
{
    public bool Enabled { get; init; } = true;
    public List<UsageRule> Rules { get; init; } = new();
}

public sealed record RestrictionHeartbeat(DateTimeOffset UpdatedAt, bool Focusing, DateTimeOffset? AllowUntil);

public static class RestrictionPolicy
{
    public static string NormalizeTarget(string kind, string raw)
    {
        raw = raw.Trim();
        if (kind == "app")
        {
            var name = Path.GetFileName(raw).ToLowerInvariant();
            if (!name.EndsWith(".exe", StringComparison.Ordinal)) name += ".exe";
            return name;
        }
        if (!Uri.TryCreate(raw.Contains("://") ? raw : "https://" + raw, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.HostNameType == UriHostNameType.Unknown)
            throw new ArgumentException("请输入有效域名，例如 example.com。");
        return uri.IdnHost.ToLowerInvariant().TrimEnd('.');
    }

    public static UsageRule Validate(UsageRule rule)
    {
        if (rule.Kind is not ("app" or "website")) throw new ArgumentException("无效的限制类型。");
        if (string.IsNullOrWhiteSpace(rule.Target)) throw new ArgumentException("请选择应用或填写域名。");
        var target = NormalizeTarget(rule.Kind, rule.Target);
        if (rule.Kind == "app" && (SafetyWhitelist.IsProtected(target) || target == "applicationframehost.exe"))
            throw new ArgumentException("系统恢复工具、共享应用宿主和 Equora 本身不能被限制。");
        if (!rule.DuringFocus && !rule.HasSchedule && rule.DailyMinutes == 0)
            throw new ArgumentException("请至少选择专注、时间段或每日时长中的一种条件。");
        if (rule.DailyMinutes < 0 || rule.DailyMinutes > 1440 || rule.StartMinute is < 0 or > 1439 || rule.EndMinute is < 0 or > 1439)
            throw new ArgumentException("时间范围无效。");
        return rule with { Target = target, Name = string.IsNullOrWhiteSpace(rule.Name) ? target : rule.Name.Trim() };
    }

    public static bool Matches(string kind, string target, string actual) => kind == "app"
        ? AppRuleMatcher.IsBlocked(actual, Array.Empty<string>(), new[] { target })
        : actual.Equals(target, StringComparison.OrdinalIgnoreCase) || actual.EndsWith("." + target, StringComparison.OrdinalIgnoreCase);

    public static string? Reason(UsageRule rule, DateTimeOffset now, bool focusing, double usedSeconds)
    {
        if (!rule.Enabled) return null;
        if (rule.Kind == "app" && SafetyWhitelist.IsProtected(rule.Target)) return null;
        if (rule.DuringFocus && focusing) return "专注期间暂停使用";
        var minute = now.Hour * 60 + now.Minute;
        if (rule.HasSchedule && (rule.StartMinute == rule.EndMinute ||
            (rule.StartMinute < rule.EndMinute ? minute >= rule.StartMinute && minute < rule.EndMinute
                : minute >= rule.StartMinute || minute < rule.EndMinute))) return "当前处于限制时间段";
        if (rule.DailyMinutes > 0 && usedSeconds >= rule.DailyMinutes * 60) return "今日使用额度已用完";
        return null;
    }
}

/// <summary>Atomic JSON files shared by the desktop and the browser's native host.</summary>
public sealed class RestrictionStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public RestrictionConfiguration Load() => Read<RestrictionConfiguration>("restrictions.json") ?? new();
    public void Save(RestrictionConfiguration config) => Write("restrictions.json", config);
    public RestrictionHeartbeat? Heartbeat => Read<RestrictionHeartbeat>("restriction-heartbeat.json");
    public void Pulse(bool focusing, DateTimeOffset? allowUntil) => Write("restriction-heartbeat.json", new RestrictionHeartbeat(DateTimeOffset.Now, focusing, allowUntil));
    public bool IsDesktopActive(DateTimeOffset now) => Heartbeat is { } h && now - h.UpdatedAt < TimeSpan.FromSeconds(12) && now >= h.UpdatedAt;
    public DateTimeOffset? BrowserSeen => Read<DateTimeOffset?>("browser-heartbeat.json");
    public void MarkBrowserSeen() => Write("browser-heartbeat.json", DateTimeOffset.Now);

    public Dictionary<string, double> Usage(string kind, DateTimeOffset now)
    {
        var data = Read<DailyUsage>(kind + "-usage.json");
        return data?.Date == now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? data.Seconds : new(StringComparer.OrdinalIgnoreCase);
    }

    public void AddUsage(string kind, IReadOnlyDictionary<string, double> increments, DateTimeOffset now)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())))[..24];
        using var mutex = new Mutex(false, "Local\\EquoraUsage" + hash + kind);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            var seconds = Usage(kind, now);
            foreach (var (target, delta) in increments)
                if (double.IsFinite(delta) && delta > 0) seconds[target] = seconds.GetValueOrDefault(target) + delta;
            Write(kind + "-usage.json", new DailyUsage(now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), seconds));
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private T? Read<T>(string name)
    {
        try
        {
            // FileShare.Delete 允许并发的原子替换继续进行，读取方拿到的是旧流的内容。
            using var stream = new FileStream(Path.Combine(directory, name), FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }
    private void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            for (var attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(temp, json); Replace(temp, path); return; }
                // 目标文件被读方或杀毒软件短暂占用时，替换会被拒绝；短暂重试即可成功。
                catch (Exception ex) when (attempt < 3 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(15); }
            }
        }
        finally { TryDelete(temp); }
    }
    // Move 的覆盖替换在目标文件被任何句柄打开时都会被 Windows 拒绝(即使读方共享了 Delete)。
    // 读方以 FileShare.Delete 打开时，可先把旧文件改名让位、再把新文件移入空出的路径。
    private static void Replace(string temp, string path)
    {
        try { File.Move(temp, path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var retired = path + "." + Guid.NewGuid().ToString("N") + ".old";
            File.Move(path, retired);
            try { File.Move(temp, path); }
            finally { TryDelete(retired); }
        }
    }
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public sealed record DailyUsage(string Date, Dictionary<string, double> Seconds);
}
