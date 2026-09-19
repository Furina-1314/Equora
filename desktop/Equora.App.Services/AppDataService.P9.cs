using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>IFocusService 实现:转发原生核心。</summary>
public sealed partial class AppDataService
{
    public FocusSessionDto StartFocus(FocusModeDto mode, int plannedMinutes, string goal = "",
        string? taskId = null, string? blockId = null, DateTimeOffset? now = null) =>
        _core.StartFocus(mode, plannedMinutes, goal, taskId, blockId, now);
    public FocusSessionDto? FindFocus(string id) => _core.FindFocus(id);
    public FocusSessionDto? OpenFocus() => _core.OpenFocus();
    public FocusSessionDto PauseFocus(string id, DateTimeOffset? now = null) =>
        _core.PauseFocus(id, now);
    public FocusSessionDto ResumeFocus(string id, DateTimeOffset? now = null) =>
        _core.ResumeFocus(id, now);
    public FocusSessionDto CompleteFocus(string id, string note = "",
        int completionLevel = -1, DateTimeOffset? now = null) =>
        _core.CompleteFocus(id, note, completionLevel, now);
    public FocusSessionDto AbandonFocus(string id, DateTimeOffset? now = null) =>
        _core.AbandonFocus(id, now);
    public IReadOnlyList<FocusSessionDto> FocusHistory(DateTimeOffset from,
        DateTimeOffset to, int limit = 0) => _core.FocusHistory(from, to, limit);
    public int RecoverFocusSessions(DateTimeOffset? now = null) =>
        _core.RecoverFocusSessions(now);

    public InterruptionDto AddInterruption(string sessionId, DateTimeOffset occurredAt,
        TimeSpan duration, string reason = "", string source = "manual",
        string handling = "") =>
        _core.AddInterruption(sessionId, occurredAt, duration, reason, source, handling);
    public IReadOnlyList<InterruptionDto> InterruptionsOf(string sessionId) =>
        _core.InterruptionsOf(sessionId);

    public DistractionDto CaptureDistraction(string content, string? sessionId = null,
        DateTimeOffset? now = null) =>
        _core.CaptureDistraction(content, sessionId, now);
    public IReadOnlyList<DistractionDto> PendingDistractions(int limit = 100) =>
        _core.PendingDistractions(limit);
    public DistractionDto ResolveDistraction(string id, int resolution,
        string resolvedRef = "") =>
        _core.ResolveDistraction(id, resolution, resolvedRef);

    public FocusProfileDto CreateFocusProfile(string name, FocusModeDto mode,
        int plannedMinutes, int breakMinutes = 5, string? allowedApps = null,
        string? blockedApps = null, string? allowedSites = null, string? blockedSites = null,
        int notifyPolicy = 0, bool isDefault = false) =>
        _core.CreateFocusProfile(name, mode, plannedMinutes, breakMinutes, allowedApps,
            blockedApps, allowedSites, blockedSites, notifyPolicy, isDefault);
    public FocusProfileDto? FindFocusProfile(string id) => _core.FindFocusProfile(id);
    public FocusProfileDto UpdateFocusProfile(FocusProfileDto p) =>
        _core.UpdateFocusProfile(p);
    public void DeleteFocusProfile(string id) => _core.DeleteFocusProfile(id);
    public IReadOnlyList<FocusProfileDto> ListFocusProfiles() => _core.ListFocusProfiles();
}
