using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

public enum FocusModeDto
{
    Pomodoro = 0,
    Deep = 1,
    Flowtime = 2,
    Stopwatch = 3,
    Untimed = 4,
}

public enum SessionStateDto
{
    Running = 0,
    Paused = 1,
    Completed = 2,
    Abandoned = 3,
}

public sealed record FocusSessionDto
{
    public required string Id { get; init; }
    public string? TaskId { get; init; }
    public string? BlockId { get; init; }
    public FocusModeDto Mode { get; init; }
    public DateTimeOffset PlannedStart { get; init; }
    public DateTimeOffset? PlannedEnd { get; init; }
    public DateTimeOffset ActualStart { get; init; }
    public DateTimeOffset? ActualEnd { get; init; }
    public TimeSpan Paused { get; init; }
    public SessionStateDto State { get; init; }
    public string Goal { get; init; } = "";
    public string CompletionNote { get; init; } = "";
    public int CompletionLevel { get; init; } = -1;
    public long Revision { get; init; }

    /// <summary>有效专注时长(扣除暂停);未闭合时按 now 计。</summary>
    public TimeSpan Effective(DateTimeOffset? now = null) =>
        TimeSpan.FromMilliseconds((ActualEnd ?? now ?? DateTimeOffset.Now).ToUnixTimeMilliseconds() -
                                  ActualStart.ToUnixTimeMilliseconds()) - Paused;
}

public sealed record InterruptionDto
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public TimeSpan Duration { get; init; }
    public string Reason { get; init; } = "";
    public string Source { get; init; } = "manual";
    public string Handling { get; init; } = "";
}

public sealed record DistractionDto
{
    public required string Id { get; init; }
    public string? SessionId { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public int Resolution { get; init; }
    public string ResolvedRef { get; init; } = "";
}

public sealed record FocusProfileDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public FocusModeDto Mode { get; init; }
    public int PlannedMinutes { get; init; }
    public int BreakMinutes { get; init; }
    public string AllowedApps { get; init; } = "[]";
    public string BlockedApps { get; init; } = "[]";
    public string AllowedSites { get; init; } = "[]";
    public string BlockedSites { get; init; } = "[]";
    public int NotifyPolicy { get; init; }
    public bool IsDefault { get; init; }
    public long Revision { get; init; }
}
