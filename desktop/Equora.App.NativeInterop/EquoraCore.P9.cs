using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

/// <summary>P9:专注会话(状态机 / 中断 / 分心捕获 / 预设 / 崩溃恢复)。</summary>
public sealed partial class EquoraCore
{
    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();
    private static DateTimeOffset FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);

    // ---- 会话状态机 ----

    public FocusSessionDto StartFocus(FocusModeDto mode, int plannedMinutes, string goal = "",
        string? taskId = null, string? blockId = null, DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_start(_core, (int)mode, plannedMinutes, goal, taskId,
            blockId, Ms(now ?? DateTimeOffset.Now), out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_start");
        return ReadFocus(handle);
    }

    public FocusSessionDto? FindFocus(string id)
    {
        var rc = NativeMethods.eq_focus_find(_core, id, out var handle, out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "focus_find");
        return ReadFocus(handle);
    }

    /// <summary>当前开放会话;无则 null。</summary>
    public FocusSessionDto? OpenFocus()
    {
        var rc = NativeMethods.eq_focus_open(_core, out var handle, out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "focus_open");
        return ReadFocus(handle);
    }

    public FocusSessionDto PauseFocus(string id, DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_pause(_core, id, Ms(now ?? DateTimeOffset.Now),
            out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_pause");
        return ReadFocus(handle);
    }

    public FocusSessionDto ResumeFocus(string id, DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_resume(_core, id, Ms(now ?? DateTimeOffset.Now),
            out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_resume");
        return ReadFocus(handle);
    }

    public FocusSessionDto CompleteFocus(string id, string note = "", int completionLevel = -1,
        DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_complete(_core, id, Ms(now ?? DateTimeOffset.Now),
            note, completionLevel, out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_complete");
        return ReadFocus(handle);
    }

    public FocusSessionDto AbandonFocus(string id, DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_abandon(_core, id, Ms(now ?? DateTimeOffset.Now),
            out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_abandon");
        return ReadFocus(handle);
    }

    public IReadOnlyList<FocusSessionDto> FocusHistory(DateTimeOffset from, DateTimeOffset to,
        int limit = 0)
    {
        var rc = NativeMethods.eq_focus_history(_core, Ms(from), Ms(to), limit, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_history");
        try
        {
            var result = new List<FocusSessionDto>();
            var count = NativeMethods.eq_focus_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_focus_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                result.Add(FromFocusView(
                    Marshal.PtrToStructure<NativeMethods.EqFocusSessionView>(ptr)));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_focus_list_destroy(list);
        }
    }

    /// <summary>崩溃恢复:收尾遗留会话,返回条数。</summary>
    public int RecoverFocusSessions(DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_recover_interrupted(_core,
            Ms(now ?? DateTimeOffset.Now), out var recovered, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_recover");
        return recovered;
    }

    private static FocusSessionDto ReadFocus(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_focus_view(handle);
            if (ptr == IntPtr.Zero) throw new EquoraException(1, "focus view unavailable");
            return FromFocusView(Marshal.PtrToStructure<NativeMethods.EqFocusSessionView>(ptr));
        }
        finally
        {
            NativeMethods.eq_focus_handle_destroy(handle);
        }
    }

    private static FocusSessionDto FromFocusView(NativeMethods.EqFocusSessionView v) => new()
    {
        Id = v.id.Str() ?? "",
        TaskId = v.task_id.Str(),
        BlockId = v.block_id.Str(),
        Mode = (FocusModeDto)v.mode,
        PlannedStart = FromMs(v.planned_start),
        PlannedEnd = v.has_planned_end != 0 ? FromMs(v.planned_end) : null,
        ActualStart = FromMs(v.actual_start),
        ActualEnd = v.has_actual_end != 0 ? FromMs(v.actual_end) : null,
        Paused = TimeSpan.FromMilliseconds(v.paused_ms),
        State = (SessionStateDto)v.state,
        Goal = v.goal.Str() ?? "",
        CompletionNote = v.completion_note.Str() ?? "",
        CompletionLevel = v.completion_level,
        Revision = v.revision,
    };

    // ---- 中断 ----

    public InterruptionDto AddInterruption(string sessionId, DateTimeOffset occurredAt,
        TimeSpan duration, string reason = "", string source = "manual", string handling = "")
    {
        var rc = NativeMethods.eq_focus_add_interruption(_core, sessionId, Ms(occurredAt),
            (long)duration.TotalMilliseconds, reason, source, handling, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_add_interruption");
        try
        {
            var ptr = NativeMethods.eq_interruption_view(handle);
            var v = Marshal.PtrToStructure<NativeMethods.EqInterruptionView>(ptr);
            return new InterruptionDto
            {
                Id = v.id.Str() ?? "",
                SessionId = v.session_id.Str() ?? "",
                OccurredAt = FromMs(v.occurred_at),
                Duration = TimeSpan.FromMilliseconds(v.duration_ms),
                Reason = v.reason.Str() ?? "",
                Source = v.source.Str() ?? "manual",
                Handling = v.handling.Str() ?? "",
            };
        }
        finally
        {
            NativeMethods.eq_interruption_handle_destroy(handle);
        }
    }

    public IReadOnlyList<InterruptionDto> InterruptionsOf(string sessionId)
    {
        var rc = NativeMethods.eq_focus_interruptions(_core, sessionId, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_interruptions");
        try
        {
            var result = new List<InterruptionDto>();
            var count = NativeMethods.eq_interruption_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_interruption_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqInterruptionView>(ptr);
                result.Add(new InterruptionDto
                {
                    Id = v.id.Str() ?? "",
                    SessionId = v.session_id.Str() ?? "",
                    OccurredAt = FromMs(v.occurred_at),
                    Duration = TimeSpan.FromMilliseconds(v.duration_ms),
                    Reason = v.reason.Str() ?? "",
                    Source = v.source.Str() ?? "manual",
                    Handling = v.handling.Str() ?? "",
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_interruption_list_destroy(list);
        }
    }

    // ---- 分心捕获(即时落库) ----

    public DistractionDto CaptureDistraction(string content, string? sessionId = null,
        DateTimeOffset? now = null)
    {
        var rc = NativeMethods.eq_focus_capture(_core, content, sessionId,
            Ms(now ?? DateTimeOffset.Now), out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_capture");
        return ReadDistraction(handle);
    }

    public IReadOnlyList<DistractionDto> PendingDistractions(int limit = 100)
    {
        var rc = NativeMethods.eq_focus_pending_distractions(_core, limit, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_pending_distractions");
        try
        {
            var result = new List<DistractionDto>();
            var count = NativeMethods.eq_distraction_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_distraction_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqDistractionView>(ptr);
                result.Add(new DistractionDto
                {
                    Id = v.id.Str() ?? "",
                    SessionId = v.session_id.Str(),
                    Content = v.content.Str() ?? "",
                    CapturedAt = FromMs(v.captured_at),
                    Resolution = v.resolution,
                    ResolvedRef = v.resolved_ref.Str() ?? "",
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_distraction_list_destroy(list);
        }
    }

    public DistractionDto ResolveDistraction(string id, int resolution, string resolvedRef = "")
    {
        var rc = NativeMethods.eq_focus_resolve_distraction(_core, id, resolution,
            resolvedRef.Length == 0 ? null : resolvedRef, out var handle, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_resolve_distraction");
        return ReadDistraction(handle);
    }

    private static DistractionDto ReadDistraction(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_distraction_view(handle);
            var v = Marshal.PtrToStructure<NativeMethods.EqDistractionView>(ptr);
            return new DistractionDto
            {
                Id = v.id.Str() ?? "",
                SessionId = v.session_id.Str(),
                Content = v.content.Str() ?? "",
                CapturedAt = FromMs(v.captured_at),
                Resolution = v.resolution,
                ResolvedRef = v.resolved_ref.Str() ?? "",
            };
        }
        finally
        {
            NativeMethods.eq_distraction_handle_destroy(handle);
        }
    }

    // ---- 专注预设 ----

    public FocusProfileDto CreateFocusProfile(string name, FocusModeDto mode,
        int plannedMinutes, int breakMinutes = 5, string? allowedApps = null,
        string? blockedApps = null, string? allowedSites = null, string? blockedSites = null,
        int notifyPolicy = 0, bool isDefault = false)
    {
        using var scope = new ProfileScope(name, mode, plannedMinutes, breakMinutes,
            allowedApps, blockedApps, allowedSites, blockedSites, notifyPolicy, isDefault,
            revision: 0, id: null);
        var rc = NativeMethods.eq_focus_profile_create(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_profile_create");
        return ReadProfile(handle);
    }

    public FocusProfileDto? FindFocusProfile(string id)
    {
        var rc = NativeMethods.eq_focus_profile_find(_core, id, out var handle, out var error);
        if (rc == 3) return null;
        EquoraException.ThrowIfFailed(rc, error, "focus_profile_find");
        return ReadProfile(handle);
    }

    public FocusProfileDto UpdateFocusProfile(FocusProfileDto p)
    {
        using var scope = new ProfileScope(p.Name, p.Mode, p.PlannedMinutes, p.BreakMinutes,
            p.AllowedApps, p.BlockedApps, p.AllowedSites, p.BlockedSites, p.NotifyPolicy,
            p.IsDefault, p.Revision, p.Id);
        var rc = NativeMethods.eq_focus_profile_update(_core, in scope.Value, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_profile_update");
        return ReadProfile(handle);
    }

    public void DeleteFocusProfile(string id)
    {
        var rc = NativeMethods.eq_focus_profile_delete(_core, id, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_profile_delete");
    }

    public IReadOnlyList<FocusProfileDto> ListFocusProfiles()
    {
        var rc = NativeMethods.eq_focus_profile_list(_core, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "focus_profile_list");
        try
        {
            var result = new List<FocusProfileDto>();
            var count = NativeMethods.eq_focus_profile_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_focus_profile_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqFocusProfileView>(ptr);
                result.Add(new FocusProfileDto
                {
                    Id = v.id.Str() ?? "",
                    Name = v.name.Str() ?? "",
                    Mode = (FocusModeDto)v.mode,
                    PlannedMinutes = v.planned_minutes,
                    BreakMinutes = v.break_minutes,
                    AllowedApps = v.allowed_apps.Str() ?? "[]",
                    BlockedApps = v.blocked_apps.Str() ?? "[]",
                    AllowedSites = v.allowed_sites.Str() ?? "[]",
                    BlockedSites = v.blocked_sites.Str() ?? "[]",
                    NotifyPolicy = v.notify_policy,
                    IsDefault = v.is_default != 0,
                    Revision = v.revision,
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_focus_profile_list_destroy(list);
        }
    }

    private static FocusProfileDto ReadProfile(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_focus_profile_view(handle);
            var v = Marshal.PtrToStructure<NativeMethods.EqFocusProfileView>(ptr);
            return new FocusProfileDto
            {
                Id = v.id.Str() ?? "",
                Name = v.name.Str() ?? "",
                Mode = (FocusModeDto)v.mode,
                PlannedMinutes = v.planned_minutes,
                BreakMinutes = v.break_minutes,
                AllowedApps = v.allowed_apps.Str() ?? "[]",
                BlockedApps = v.blocked_apps.Str() ?? "[]",
                AllowedSites = v.allowed_sites.Str() ?? "[]",
                BlockedSites = v.blocked_sites.Str() ?? "[]",
                NotifyPolicy = v.notify_policy,
                IsDefault = v.is_default != 0,
                Revision = v.revision,
            };
        }
        finally
        {
            NativeMethods.eq_focus_profile_handle_destroy(handle);
        }
    }

    private sealed class ProfileScope : IDisposable
    {
        private readonly List<Utf8NativeString> _owned = new();
        public NativeMethods.EqFocusProfileInput Value;

        public ProfileScope(string name, FocusModeDto mode, int planned, int breakMin,
            string? allowedApps, string? blockedApps, string? allowedSites,
            string? blockedSites, int notifyPolicy, bool isDefault, long revision, string? id)
        {
            Value = new NativeMethods.EqFocusProfileInput
            {
                Id = Pin(id),
                Name = Pin(name),
                mode = (int)mode,
                planned_minutes = planned,
                break_minutes = breakMin,
                AllowedApps = Pin(allowedApps),
                BlockedApps = Pin(blockedApps),
                AllowedSites = Pin(allowedSites),
                BlockedSites = Pin(blockedSites),
                notify_policy = notifyPolicy,
                is_default = isDefault ? 1 : 0,
                revision = revision,
            };
        }

        private nint Pin(string? s)
        {
            if (s is null) return 0;
            var holder = new Utf8NativeString(s);
            _owned.Add(holder);
            return holder.Pointer;
        }

        public void Dispose()
        {
            foreach (var h in _owned) h.Dispose();
            _owned.Clear();
        }
    }
}
