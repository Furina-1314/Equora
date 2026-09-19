using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>同步传输抽象:生产为 HTTP,测试为进程内实现(忠实服务端协议)。</summary>
public interface ISyncTransport
{
    /// <summary>发起一次 sync 请求;网络/协议失败抛异常。</summary>
    Task<SyncWireResponse> SyncAsync(string deviceId, long cursor,
        IReadOnlyList<SyncWireOperation> operations, CancellationToken ct = default);
}

/// <summary>与服务器协议一致的线格式 DTO。</summary>
public sealed record SyncWireOperation
{
    public required string OperationId { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public long BaseRevision { get; init; }
    public required string Operation { get; init; } // create/update/delete
    public string Payload { get; init; } = "{}";
}

public sealed record SyncWireOutcome
{
    public required string OperationId { get; init; }
    public required string Result { get; init; } // accepted/duplicate/conflict/invalid
    public long NewRevision { get; init; }
    public long ServerRevision { get; init; }
    public bool ServerDeleted { get; init; }
    public string ServerPayload { get; init; } = "{}";
}

public sealed record SyncWireChange
{
    public long Seq { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public long Revision { get; init; }
    public required string Kind { get; init; }
    public string Payload { get; init; } = "{}";
    public string DeviceId { get; init; } = "";
}

public sealed record SyncWireResponse
{
    public IReadOnlyList<SyncWireOutcome> Outcomes { get; init; } = Array.Empty<SyncWireOutcome>();
    public IReadOnlyList<SyncWireChange> Changes { get; init; } = Array.Empty<SyncWireChange>();
    public long NextCursor { get; init; }
}

/// <summary>HTTP 传输(Bearer + JSON,与 Drogon 服务器协议 v1 一致)。</summary>
public sealed class HttpSyncTransport : ISyncTransport
{
    private readonly HttpClient _http;
    private readonly string _token;

    public HttpSyncTransport(HttpClient http, string serverUrl, string token)
    {
        _http = http;
        _http.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
        _token = token;
    }

    public async Task<SyncWireResponse> SyncAsync(string deviceId, long cursor,
        IReadOnlyList<SyncWireOperation> operations, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            protocolVersion = 1,
            deviceId,
            cursor,
            operations = operations.Select(o => new
            {
                operationId = o.OperationId,
                entityType = o.EntityType,
                entityId = o.EntityId,
                baseRevision = o.BaseRevision,
                operation = o.Operation,
                payload = JsonDocument.Parse(o.Payload, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                }).RootElement,
            }),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/sync")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseResponse(json);
    }

    internal static SyncWireResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var outcomes = root.GetProperty("outcomes").EnumerateArray().Select(o =>
            new SyncWireOutcome
            {
                OperationId = o.GetProperty("operationId").GetString() ?? "",
                Result = o.GetProperty("result").GetString() ?? "",
                NewRevision = o.TryGetProperty("newRevision", out var nr) ? nr.GetInt64() : 0,
                ServerRevision = o.TryGetProperty("serverRevision", out var sr)
                    ? sr.GetInt64() : 0,
                ServerDeleted = o.TryGetProperty("serverDeleted", out var sd) && sd.GetBoolean(),
                ServerPayload = o.TryGetProperty("serverPayload", out var sp)
                    ? sp.GetRawText() : "{}",
            }).ToList();

        var changes = root.GetProperty("changes").EnumerateArray().Select(c =>
            new SyncWireChange
            {
                Seq = c.GetProperty("seq").GetInt64(),
                EntityType = c.GetProperty("entityType").GetString() ?? "",
                EntityId = c.GetProperty("entityId").GetString() ?? "",
                Revision = c.GetProperty("revision").GetInt64(),
                Kind = c.GetProperty("kind").GetString() ?? "update",
                Payload = c.TryGetProperty("payload", out var p) ? p.GetRawText() : "{}",
                DeviceId = c.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "",
            }).ToList();

        return new SyncWireResponse
        {
            Outcomes = outcomes,
            Changes = changes,
            NextCursor = root.GetProperty("nextCursor").GetInt64(),
        };
    }
}
