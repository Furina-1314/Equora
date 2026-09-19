using System.Text;
using System.Text.Json;
using Equora.App.NativeInterop;

namespace Equora.NativeHost;

/// <summary>
/// Equora Native Messaging host(Chrome/Edge 规范)。
/// 扩展 ←stdin/stdout→ 本进程 →(equora_capi.dll,同库只读会话 + 读 appsettings)。
/// 协议:v1 {type, protocolVersion, sessionId, ts, nonce, checksum} + {type:"query"} →
/// 回 {type:"state", state:FocusGateState}。校验和不匹配回 {type:"error"}。
/// </summary>
public static class Program
{
    // 默认数据库与主程序一致(LocalAppData\Equora\equora.db)。
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Equora", "equora.db");

    public static int Main(string[] args)
    {
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

            var root = doc.RootElement;
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
                    WriteJson(stdout, new { type = "state", state = BuildState() });
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

    /// <summary>查询开放专注会话 + 对应预设的站点名单 → FocusGateState。</summary>
    private static object BuildState()
    {
        if (!File.Exists(DbPath))
        {
            return FocusGate.FromSession(null, null, null, null);
        }
        try
        {
            using var core = EquoraCore.Open(DbPath, "native-host");
            var session = core.OpenFocus();
            string? taskTitle = null;
            string? allowed = null;
            string? blocked = null;
            if (session is { TaskId: { } taskId })
            {
                taskTitle = core.GetTask(taskId)?.Title;
            }
            if (session is not null)
            {
                // 找默认预设或第一个预设(扩展执行阶段统一走预设;P11 取默认)。
                var profile = core.ListFocusProfiles().FirstOrDefault(p => p.IsDefault)
                              ?? core.ListFocusProfiles().FirstOrDefault();
                allowed = profile?.AllowedSites;
                blocked = profile?.BlockedSites;
            }
            return FocusGate.FromSession(session, taskTitle, allowed, blocked);
        }
        catch (EquoraException)
        {
            return FocusGate.FromSession(null, null, null, null);
        }
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
