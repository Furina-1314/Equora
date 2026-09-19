using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>P9:专注数据面(状态机、中断、分心捕获、预设、崩溃恢复)。</summary>
public interface IFocusService
{
    FocusSessionDto StartFocus(FocusModeDto mode, int plannedMinutes, string goal = "",
        string? taskId = null, string? blockId = null, DateTimeOffset? now = null);
    FocusSessionDto? FindFocus(string id);
    FocusSessionDto? OpenFocus();
    FocusSessionDto PauseFocus(string id, DateTimeOffset? now = null);
    FocusSessionDto ResumeFocus(string id, DateTimeOffset? now = null);
    FocusSessionDto CompleteFocus(string id, string note = "", int completionLevel = -1,
        DateTimeOffset? now = null);
    FocusSessionDto AbandonFocus(string id, DateTimeOffset? now = null);
    IReadOnlyList<FocusSessionDto> FocusHistory(DateTimeOffset from, DateTimeOffset to,
        int limit = 0);
    int RecoverFocusSessions(DateTimeOffset? now = null);

    InterruptionDto AddInterruption(string sessionId, DateTimeOffset occurredAt,
        TimeSpan duration, string reason = "", string source = "manual", string handling = "");
    IReadOnlyList<InterruptionDto> InterruptionsOf(string sessionId);

    DistractionDto CaptureDistraction(string content, string? sessionId = null,
        DateTimeOffset? now = null);
    IReadOnlyList<DistractionDto> PendingDistractions(int limit = 100);
    DistractionDto ResolveDistraction(string id, int resolution, string resolvedRef = "");

    FocusProfileDto CreateFocusProfile(string name, FocusModeDto mode, int plannedMinutes,
        int breakMinutes = 5, string? allowedApps = null, string? blockedApps = null,
        string? allowedSites = null, string? blockedSites = null, int notifyPolicy = 0,
        bool isDefault = false);
    FocusProfileDto? FindFocusProfile(string id);
    FocusProfileDto UpdateFocusProfile(FocusProfileDto p);
    void DeleteFocusProfile(string id);
    IReadOnlyList<FocusProfileDto> ListFocusProfiles();
}
