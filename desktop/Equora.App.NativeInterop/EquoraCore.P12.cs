using System.Runtime.InteropServices;

namespace Equora.App.NativeInterop;

/// <summary>P12:智能规划 / 复盘 / 自动化。</summary>
public sealed partial class EquoraCore
{
    public const int DefaultWorkdayMask = 0b0011111; // 周一..周五

    public IReadOnlyList<ProposedBlockDto> PlanWeek(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute = 540, int workEndMinute = 1080, int workdayMask = DefaultWorkdayMask,
        int maxBlockMinutes = 120)
    {
        var tz = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes;
        var rc = NativeMethods.eq_plan_week(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), workStartMinute, workEndMinute, workdayMask,
            maxBlockMinutes, tz, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "plan_week");
        try
        {
            var result = new List<ProposedBlockDto>();
            var count = NativeMethods.eq_proposed_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_proposed_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqProposedView>(ptr);
                result.Add(new ProposedBlockDto
                {
                    TaskId = v.task_id.Str() ?? "",
                    Start = DateTimeOffset.FromUnixTimeMilliseconds(v.start),
                    End = DateTimeOffset.FromUnixTimeMilliseconds(v.end),
                    Reason = v.reason.Str() ?? "",
                });
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_proposed_list_destroy(list);
        }
    }

    public IReadOnlyList<DayLoadDto> DayLoads(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute = 540, int workEndMinute = 1080,
        int workdayMask = DefaultWorkdayMask)
    {
        var tz = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes;
        var rc = NativeMethods.eq_day_loads(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), workStartMinute, workEndMinute, workdayMask, tz,
            out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "day_loads");
        try
        {
            var result = new List<DayLoadDto>();
            var count = NativeMethods.eq_load_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_load_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqDayLoadView>(ptr);
                unsafe
                {
                    result.Add(new DayLoadDto
                    {
                        LocalDate = NativeMethods.FixedString(v.local_date, 11),
                        PlannedMinutes = v.planned_minutes,
                        CapacityMinutes = v.capacity_minutes,
                    });
                }
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_load_list_destroy(list);
        }
    }

    public IReadOnlyList<SplitSuggestionDto> SuggestSplits(DateTimeOffset from,
        DateTimeOffset to, int maxBlockMinutes = 120)
    {
        var rc = NativeMethods.eq_suggest_splits(_core, from.ToUnixTimeMilliseconds(),
            to.ToUnixTimeMilliseconds(), maxBlockMinutes, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "suggest_splits");
        try
        {
            var result = new List<SplitSuggestionDto>();
            var count = NativeMethods.eq_split_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_split_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqSplitView>(ptr);
                unsafe
                {
                    result.Add(new SplitSuggestionDto
                    {
                        TaskId = NativeMethods.FixedString(v.task_id, 64),
                        Title = NativeMethods.FixedString(v.title, 128),
                        TotalMinutes = v.total_minutes,
                        Blocks = v.blocks,
                        BlockMinutes = v.block_minutes,
                    });
                }
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_split_list_destroy(list);
        }
    }

    /// <summary>估时校正建议;样本不足返回 null。</summary>
    public int? CorrectEstimate(string taskId)
    {
        var rc = NativeMethods.eq_correct_estimate(_core, taskId, out var suggested,
            out _, out var has, out var error);
        EquoraException.ThrowIfFailed(rc, error, "correct_estimate");
        return has != 0 ? suggested : null;
    }

    public DailyReviewDto ComputeDailyReview(DateTimeOffset dayStartLocal)
    {
        var dayStartUtc = new DateTimeOffset(dayStartLocal.Date,
            dayStartLocal.Offset).ToUniversalTime();
        var tz = (int)dayStartLocal.Offset.TotalMinutes;
        var rc = NativeMethods.eq_review_daily_compute(_core,
            dayStartUtc.ToUnixTimeMilliseconds(), tz, out var output, out var error);
        EquoraException.ThrowIfFailed(rc, error, "review_daily_compute");
        return new DailyReviewDto
        {
            Completed = output.completed,
            Deferred = output.deferred,
            Cancelled = output.cancelled,
            PlannedMinutes = output.planned_minutes,
            ActualMinutes = output.actual_minutes,
            DistractionCount = output.distraction_count,
        };
    }

    public void SaveDailyReview(DateTimeOffset dayStartLocal, string notes = "",
        string focusTomorrow = "")
    {
        var dayStartUtc = new DateTimeOffset(dayStartLocal.Date,
            dayStartLocal.Offset).ToUniversalTime();
        var rc = NativeMethods.eq_review_daily_save(_core,
            dayStartUtc.ToUnixTimeMilliseconds(), (int)dayStartLocal.Offset.TotalMinutes,
            notes.Length == 0 ? null : notes, focusTomorrow.Length == 0 ? null : focusTomorrow,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "review_daily_save");
    }

    public WeeklyReviewDto ComputeWeeklyReview(DateTimeOffset weekStartLocal)
    {
        var weekStartUtc = new DateTimeOffset(weekStartLocal.Date,
            weekStartLocal.Offset).ToUniversalTime();
        var tz = (int)weekStartLocal.Offset.TotalMinutes;
        var rc = NativeMethods.eq_review_weekly_compute(_core,
            weekStartUtc.ToUnixTimeMilliseconds(), tz, out var output, out var error);
        EquoraException.ThrowIfFailed(rc, error, "review_weekly_compute");
        return new WeeklyReviewDto
        {
            DeepWorkMinutes = output.deep_work_minutes,
            FocusRatio = output.focus_ratio,
            EstimateAccuracy = output.estimate_accuracy,
            BestFocusHour = output.best_focus_hour,
        };
    }

    public void SaveWeeklyReview(DateTimeOffset weekStartLocal, string notes = "",
        string nextWeekGoals = "")
    {
        var weekStartUtc = new DateTimeOffset(weekStartLocal.Date,
            weekStartLocal.Offset).ToUniversalTime();
        var rc = NativeMethods.eq_review_weekly_save(_core,
            weekStartUtc.ToUnixTimeMilliseconds(), (int)weekStartLocal.Offset.TotalMinutes,
            notes.Length == 0 ? null : notes,
            nextWeekGoals.Length == 0 ? null : nextWeekGoals, out var error);
        EquoraException.ThrowIfFailed(rc, error, "review_weekly_save");
    }

    // ---- 自动化 ----

    public AutomationRuleDto CreateAutomationRule(string name, string trigger,
        string actions, string conditions = "{}")
    {
        using var id = new Utf8NativeString("");
        using var n = new Utf8NativeString(name);
        using var t = new Utf8NativeString(trigger);
        using var c = new Utf8NativeString(conditions);
        using var a = new Utf8NativeString(actions);
        var input = new NativeMethods.EqAutomationInput
        {
            Id = id.Pointer, Name = n.Pointer, Trigger = t.Pointer,
            Conditions = c.Pointer, Actions = a.Pointer, enabled = 1, revision = 0,
        };
        var rc = NativeMethods.eq_automation_create(_core, in input, out var handle,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "automation_create");
        return ReadAutomation(handle);
    }

    public IReadOnlyList<AutomationRuleDto> ListAutomationRules()
    {
        var rc = NativeMethods.eq_automation_list(_core, out var list, out var error);
        EquoraException.ThrowIfFailed(rc, error, "automation_list");
        try
        {
            var result = new List<AutomationRuleDto>();
            var count = NativeMethods.eq_automation_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var ptr = NativeMethods.eq_automation_list_get(list, i);
                if (ptr == IntPtr.Zero) continue;
                var v = Marshal.PtrToStructure<NativeMethods.EqAutomationView>(ptr);
                result.Add(FromAutomationView(v));
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_automation_list_destroy(list);
        }
    }

    public void SetAutomationEnabled(string id, bool enabled)
    {
        var rc = NativeMethods.eq_automation_set_enabled(_core, id, enabled ? 1 : 0,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "automation_set_enabled");
    }

    public void DeleteAutomationRule(string id)
    {
        var rc = NativeMethods.eq_automation_delete(_core, id, out var error);
        EquoraException.ThrowIfFailed(rc, error, "automation_delete");
    }

    /// <summary>评估命中规则 id(触发由调用方传入;动作由调用方执行 —— 防递归)。</summary>
    public IReadOnlyList<string> EvaluateAutomation(string trigger, string taskId)
    {
        var rc = NativeMethods.eq_automation_evaluate(_core, trigger, taskId, out var list,
            out var error);
        EquoraException.ThrowIfFailed(rc, error, "automation_evaluate");
        try
        {
            var result = new List<string>();
            var count = NativeMethods.eq_id_list_count(list);
            for (int i = 0; i < count; i++)
            {
                var p = NativeMethods.eq_id_list_get(list, i);
                if (p != IntPtr.Zero) result.Add(Marshal.PtrToStringUTF8(p) ?? "");
            }
            return result;
        }
        finally
        {
            NativeMethods.eq_id_list_destroy(list);
        }
    }

    private static AutomationRuleDto ReadAutomation(IntPtr handle)
    {
        try
        {
            var ptr = NativeMethods.eq_automation_view(handle);
            var v = Marshal.PtrToStructure<NativeMethods.EqAutomationView>(ptr);
            return FromAutomationView(v);
        }
        finally
        {
            NativeMethods.eq_automation_handle_destroy(handle);
        }
    }

    private static AutomationRuleDto FromAutomationView(NativeMethods.EqAutomationView v) => new()
    {
        Id = v.id.Str() ?? "",
        Name = v.name.Str() ?? "",
        Trigger = v.trigger.Str() ?? "",
        Conditions = v.conditions.Str() ?? "{}",
        Actions = v.actions.Str() ?? "",
        Enabled = v.enabled != 0,
        Revision = v.revision,
    };
}
