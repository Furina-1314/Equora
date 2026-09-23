using System.Text;
using System.Text.Json;
using Equora.App.NativeInterop;
using Equora.App.Services;

namespace Equora.NativeHost;

/// <summary>
/// Equora Native Messaging host(Chrome/Edge 规范)。
/// 扩展通过长度前缀 JSON 查询桌面端限制规则，并报告前台域名使用秒数。
/// 桌面端心跳过期后停止限制。协议 v1，nonce 严格递增。
/// </summary>
public static class Program
{
    // 默认数据库与主程序一致(LocalAppData\Equora\equora.db)。
    private static RestrictionStore Rules => new(AppPaths.ResolveDataDirectory());
    private static DateTimeOffset _lastQuery = DateTimeOffset.Now;

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--state")
        {
            Console.Write(JsonSerializer.Serialize(BuildState(null)));
            return 0;
        }
        if (args.Length > 0 && args[0] == "--print-manifest")
        {
            Console.Out.Write(BuildHostManifest(args.Length > 1 ? args[1]
                : AssemblyLocation() ));
            return 0;
        }

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        ulong lastNonce = 0;
        while (ReadFrame(stdin) is { } frame)
        {
            JsonDocument? doc;
            try
            {
                doc = NativeMessaging.Decode(frame);
            }
            catch (JsonException)
            {
                WriteJson(stdout, new { type = "error", reason = "bad-json" });
                continue;
            }
            if (doc is null)
            {
                WriteJson(stdout, new { type = "error", reason = "bad-frame" });
                continue;
            }

            using var document = doc;
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeEl))
            {
                WriteJson(stdout, new { type = "error", reason = "missing-type" });
                continue;
            }

            // 版本检查。
            if (root.TryGetProperty("protocolVersion", out var v) &&
                v.GetInt32() != NativeMessaging.ProtocolVersion)
            {
                WriteJson(stdout, new { type = "error", reason = "version-mismatch" });
                continue;
            }

            // 校验和(对整个消息 JSON 的 FNV-1a)。
            var rawJson = Encoding.UTF8.GetString(
                frame.AsSpan(4, frame.Length - 4).ToArray());
            if (root.TryGetProperty("checksum", out var c) &&
                c.GetString() != NativeMessaging.Checksum(rawJson))
            {
                WriteJson(stdout, new { type = "error", reason = "checksum-mismatch" });
                continue;
            }

            // 重放保护:nonce 必须严格递增。
            if (root.TryGetProperty("nonce", out var n) && n.TryGetUInt64(out var nonce))
            {
                if (nonce <= lastNonce)
                {
                    WriteJson(stdout, new { type = "error", reason = "replay" });
                    continue;
                }
                lastNonce = nonce;
            }

            switch (typeEl.GetString())
            {
                case "hello":
                    WriteJson(stdout, new
                    {
                        type = "hello",
                        protocolVersion = NativeMessaging.ProtocolVersion,
                        app = "Equora",
                    });
                    break;
                case "query":
                    WriteJson(stdout, new { type = "state", state = BuildState(root) });
                    break;
                default:
                    WriteJson(stdout, new { type = "error", reason = "unknown-type" });
                    break;
            }
        }
        return 0;
    }

    /// <summary>读取一帧(4 字节长度 + 载荷);EOF 返回 null。</summary>
    private static byte[]? ReadFrame(Stream input)
    {
        var header = new byte[4];
        if (!ReadExact(input, header)) return null;
        var length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
        if (length <= 0 || length > NativeMessaging.MaxMessageBytes) return null;
        var payload = new byte[length];
        if (!ReadExact(input, payload)) return null;

        var frame = new byte[4 + length];
        header.CopyTo(frame, 0);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static bool ReadExact(Stream input, byte[] buffer)
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = input.Read(buffer, filled, buffer.Length - filled);
            if (read <= 0) return false;
            filled += read;
        }
        return true;
    }

    private static void WriteJson(Stream output, object message)
    {
        var frame = NativeMessaging.Encode(message);
        output.Write(frame, 0, frame.Length);
        output.Flush();
    }

    private static object BuildState(JsonElement? request)
    {
        var now = DateTimeOffset.Now;
        Rules.MarkBrowserSeen();
        var configuration = Rules.Load();
        var heartbeat = Rules.Heartbeat;
        var active = configuration.Enabled && Rules.IsDesktopActive(now);
        var websiteRules = configuration.Rules.Where(r => r.Enabled && r.Kind == "website").ToList();
        if (active && request is { } root && root.TryGetProperty("usage", out var report) && report.ValueKind == JsonValueKind.Object &&
            report.TryGetProperty("domain", out var domainValue) && domainValue.ValueKind == JsonValueKind.String &&
            report.TryGetProperty("seconds", out var secondsValue) && secondsValue.TryGetDouble(out var seconds))
        {
            try
            {
                var domain = RestrictionPolicy.NormalizeTarget("website", domainValue.GetString()!);
                var elapsed = Math.Min(Math.Clamp((now - _lastQuery).TotalSeconds, 0, 10), Math.Clamp(seconds, 0, 10));
                if (double.IsFinite(elapsed) && now.Date == _lastQuery.Date)
                {
                    var increments = websiteRules.Select(r => r.Target).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(target => RestrictionPolicy.Matches("website", target, domain)).ToDictionary(target => target, _ => elapsed);
                    Rules.AddUsage("website", increments, now);
                }
            }
            catch (ArgumentException) { }
        }
        _lastQuery = now;
        var usage = Rules.Usage("website", now);
        var temporary = heartbeat?.AllowUntil > now;
        var blocked = active && !temporary ? websiteRules.Where(r => RestrictionPolicy.Reason(r, now,
            heartbeat?.Focusing == true, usage.GetValueOrDefault(r.Target)) is not null).Select(r => r.Target).Distinct().ToArray() : Array.Empty<string>();
        return new { enabled = active, focusing = heartbeat?.Focusing == true, updatedUtcMs = now.ToUnixTimeMilliseconds(),
            blockedDomains = blocked, trackedDomains = active ? websiteRules.Select(r => r.Target).Distinct().ToArray() : Array.Empty<string>(),
            usedSeconds = usage,
            quotas = active ? websiteRules.Where(r => r.DailyMinutes > 0)
                .Select(r => new { domain = r.Target, remainingSeconds = Math.Max(0, r.DailyMinutes * 60 - usage.GetValueOrDefault(r.Target)) }).ToArray()
                : Array.Empty<object>() };
    }

    private static string AssemblyLocation() =>
        Path.Combine(Path.GetDirectoryName(
            Environment.ProcessPath ?? AppContext.BaseDirectory) ?? ".", "");

    private static string BuildHostManifest(string dir)
    {
        var exe = Path.Combine(dir, "com.equora.nativehost.exe");
        var manifest = new
        {
            name = "com.equora.nativehost",
            description = "衡序 Equora 专注限制 Native Messaging Host",
            path = exe,
            type = "stdio",
            @allowed_origins = new[]
            {
                "chrome-extension://EXTENSION_ID_PLACEHOLDER/",
            },
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
