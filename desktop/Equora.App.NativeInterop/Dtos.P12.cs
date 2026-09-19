using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

public sealed record ProposedBlockDto
{
    public required string TaskId { get; init; }
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string Reason { get; init; } = "";
}

public sealed record DayLoadDto
{
    public required string LocalDate { get; init; }
    public long PlannedMinutes { get; init; }
    public long CapacityMinutes { get; init; }
    public bool Overloaded => PlannedMinutes > CapacityMinutes;
}

public sealed record SplitSuggestionDto
{
    public required string TaskId { get; init; }
    public required string Title { get; init; }
    public int TotalMinutes { get; init; }
    public int Blocks { get; init; }
    public int BlockMinutes { get; init; }
}

public sealed record DailyReviewDto
{
    public int Completed { get; init; }
    public int Deferred { get; init; }
    public int Cancelled { get; init; }
    public long PlannedMinutes { get; init; }
    public long ActualMinutes { get; init; }
    public int DistractionCount { get; init; }
}

public sealed record WeeklyReviewDto
{
    public long DeepWorkMinutes { get; init; }
    public double FocusRatio { get; init; }
    public double EstimateAccuracy { get; init; }
    public int BestFocusHour { get; init; }
}

public sealed record AutomationRuleDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Trigger { get; init; }
    public string Conditions { get; init; } = "{}";
    public required string Actions { get; init; }
    public bool Enabled { get; init; }
    public long Revision { get; init; }
}
