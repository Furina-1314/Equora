namespace Equora.App.NativeInterop;

public enum ProjectStatusDto
{
    Active = 0,
    Archived = 1,
}

public sealed record ProjectDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Color { get; init; } = "";
    public string Goal { get; init; } = "";
    public ProjectStatusDto Status { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public long Revision { get; init; }
    public bool IsDeleted { get; init; }
    public string LastDeviceId { get; init; } = "";
}

public sealed record TagDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Color { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public long Revision { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed record ChecklistItemDto
{
    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string Content { get; init; }
    public bool IsChecked { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public long Revision { get; init; }
}

public enum TaskSort
{
    CreatedAsc = 0,
    CreatedDesc = 1,
    DueAsc = 2,
    DueDesc = 3,
    PriorityDesc = 4,
    UpdatedDesc = 5,
}

public enum SmartListKind
{
    Inbox,
    Today,
    Upcoming7,
    Overdue,
    NoDate,
    Scheduled,
    Waiting,
    Completed,
    CompletedToday,
}

/// <summary>托管查询条件;经 ABI 转换后在原生层执行。</summary>
public sealed record TaskQuery
{
    public SmartListKind? SmartList { get; init; }
    public string? Search { get; init; }
    public string? ProjectId { get; init; }
    public string? TagId { get; init; }
    public IReadOnlyList<TaskStatus>? Statuses { get; init; }
    public bool IncludeDeleted { get; init; }
    public TaskSort Sort { get; init; } = TaskSort.CreatedAsc;
    public int? Limit { get; init; }
    public int Offset { get; init; }

    /// <summary>智能清单的日界由本机时区当前偏移决定(业务规则在原生层)。</summary>
    internal int LocalOffsetMinutes()
    {
        var now = DateTimeOffset.Now;
        return (int)TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes;
    }

    internal long NowMs() => DateTimeOffset.Now.ToUnixTimeMilliseconds();
}
