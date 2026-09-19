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
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public TaskStatus Status { get; set; }
    public Priority Priority { get; set; }
    public int Importance { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public int? EstimateMinutes { get; set; }
    public int ActualMinutes { get; set; }
    public string? ProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }
    public bool IsDeleted { get; set; }
    public string LastDeviceId { get; set; } = "";
}

/// <summary>创建任务的输入(原生层补全 id/时间戳/revision)。</summary>
public sealed record TaskDraft
{
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public TaskStatus Status { get; set; } = TaskStatus.Inbox;
    public Priority Priority { get; set; } = Priority.Normal;
    public int Importance { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public int? EstimateMinutes { get; set; }
    public string? ProjectId { get; set; }
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
