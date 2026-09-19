using System.Text;
using System.Text.Json;

namespace Equora.App.NativeInterop;

/// <summary>Native Messaging 帧编解码(Chrome 规范:4 字节小端长度前缀 + UTF-8 JSON)。</summary>
public static class NativeMessaging
{
    public const int MaxMessageBytes = 1024 * 1024; // Chrome 上限 1MB
    public const int ProtocolVersion = 1;

    public static byte[] Encode(object message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        if (json.Length > MaxMessageBytes)
        {
            throw new InvalidOperationException("native message exceeds 1MB limit");
        }
        var frame = new byte[4 + json.Length];
        frame[0] = (byte)(json.Length & 0xFF);
        frame[1] = (byte)((json.Length >> 8) & 0xFF);
        frame[2] = (byte)((json.Length >> 16) & 0xFF);
        frame[3] = (byte)((json.Length >> 24) & 0xFF);
        json.CopyTo(frame, 4);
        return frame;
    }

    public static JsonDocument? Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4) return null;
        var length = frame[0] | (frame[1] << 8) | (frame[2] << 16) | (frame[3] << 24);
        if (length <= 0 || length > MaxMessageBytes || 4 + length > frame.Length)
        {
            return null;
        }
        return JsonDocument.Parse(frame.Slice(4, length).ToArray());
    }

    /// <summary>轻量校验和:UTF-8 JSON 的 FNV-1a(64 位)十六进制 —— 防传输损坏,非安全边界。</summary>
    public static string Checksum(string json)
    {
        ulong hash = 14695981039346656037;
        foreach (var b in Encoding.UTF8.GetBytes(json))
        {
            hash ^= b;
            hash *= 1099511628211;
        }
        return hash.ToString("x16");
    }
}

/// <summary>host → 扩展的当前专注限制状态。</summary>
public sealed record FocusGateState
{
    public int ProtocolVersion { get; init; } = NativeMessaging.ProtocolVersion;
    public bool Focusing { get; init; }
    public string? SessionId { get; init; }
    public string? TaskTitle { get; init; }
    /// <summary>截止 UTC 毫秒;0 = 无计划。</summary>
    public long EndUtcMs { get; init; }
    /// <summary>受限域名(含子域名匹配由扩展负责)。</summary>
    public IReadOnlyList<string> BlockedDomains { get; init; } = Array.Empty<string>();
    /// <summary>白名单域名(优先于受限)。</summary>
    public IReadOnlyList<string> AllowedDomains { get; init; } = Array.Empty<string>();
    /// <summary>娱乐预算(分钟/日);域名 → 分钟。</summary>
    public IReadOnlyDictionary<string, int> DomainBudgetMinutes { get; init; }
        = new Dictionary<string, int>();
}

/// <summary>FocusGateState 与扩展规则构造(纯函数,可测)。</summary>
public static class FocusGate
{
    /// <summary>从专注会话与预设 JSON 计算限制状态(白名单去重、预算解析)。</summary>
    public static FocusGateState FromSession(FocusSessionDto? session, string? taskTitle,
        string? profileAllowedSites, string? profileBlockedSites,
        IReadOnlyDictionary<string, int>? budget = null)
    {
        if (session is null || session.State is not (SessionStateDto.Running
            or SessionStateDto.Paused))
        {
            return new FocusGateState { Focusing = false };
        }

        return new FocusGateState
        {
            Focusing = true,
            SessionId = session.Id,
            TaskTitle = taskTitle ?? "",
            EndUtcMs = session.PlannedEnd?.ToUnixTimeMilliseconds() ?? 0,
            AllowedDomains = ParseDomains(profileAllowedSites),
            BlockedDomains = ParseDomains(profileBlockedSites),
            DomainBudgetMinutes = budget ?? new Dictionary<string, int>(),
        };
    }

    private static string[] ParseDomains(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => Normalize(e.GetString()!))
                .Where(d => d.Length > 0)
                .Distinct()
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>规范化域名:去协议/路径/端口、转小写、去首部 www.。</summary>
    public static string Normalize(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s.Contains("://"))
        {
            s = s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }
        var cut = s.IndexOfAny(new[] { '/', ':', '?' });
        if (cut >= 0) s = s[..cut];
        if (s.StartsWith("www.")) s = s[4..];
        return s;
    }

    /// <summary>域名是否命中规则(白名单优先;子域名匹配:x.com 覆盖 a.x.com)。</summary>
    public static bool IsBlocked(string host, IReadOnlyList<string> allowed,
        IReadOnlyList<string> blocked)
    {
        var h = Normalize(host);
        foreach (var a in allowed)
        {
            if (DomainMatches(h, a)) return false;
        }
        foreach (var b in blocked)
        {
            if (DomainMatches(h, b)) return true;
        }
        return false;
    }

    private static bool DomainMatches(string host, string rule) =>
        host == rule || host.EndsWith("." + rule, StringComparison.Ordinal);
}
