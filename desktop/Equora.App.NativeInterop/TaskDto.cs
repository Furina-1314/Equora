namespace Equora.App.NativeInterop;

/// <summary>原生核心返回的错误,Code 与 C ABI ErrorCode 一致。</summary>
public sealed class EquoraException : Exception
{
    public int Code { get; }

    internal EquoraException(int code, string message)
        : base($"[{code}] {message}")
    {
        Code = code;
    }

    internal static void ThrowIfFailed(int rc, in NativeMethods.EqError error, string context)
    {
        if (rc == 0) return;
        throw new EquoraException(error.Code, $"{context}: {error.MessageText}");
    }
}

/// <summary>任务的可空字段与展示值(托管副本,脱离原生句柄生命周期)。</summary>
public sealed record TaskDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Note { get; init; } = "";
    public TaskStatus Status { get; init; }
    public Priority Priority { get; init; }
    public int Importance { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public int? EstimateMinutes { get; init; }
    public int ActualMinutes { get; init; }
    public string? ProjectId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public long Revision { get; init; }
    public bool IsDeleted { get; init; }
    public string LastDeviceId { get; init; } = "";
}

/// <summary>创建任务的输入(原生层补全 id/时间戳/revision)。</summary>
public sealed record TaskDraft
{
    public required string Title { get; init; }
    public string Note { get; init; } = "";
    public TaskStatus Status { get; init; } = TaskStatus.Inbox;
    public Priority Priority { get; init; } = Priority.Normal;
    public int Importance { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public int? EstimateMinutes { get; init; }
    public string? ProjectId { get; init; }
}

public enum TaskStatus
{
    Inbox = 0,
    Planned = 1,
    InProgress = 2,
    Waiting = 3,
    Done = 4,
    Cancelled = 5,
}

public enum Priority
{
    None = 0,
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}
