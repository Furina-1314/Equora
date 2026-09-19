using Equora.App.NativeInterop;

namespace Equora.App.Services;

/// <summary>P12:规划/复盘/自动化(转发原生核心)。</summary>
public sealed partial class AppDataService
{
    public IReadOnlyList<ProposedBlockDto> PlanWeek(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute = 540, int workEndMinute = 1080,
        int workdayMask = EquoraCore.DefaultWorkdayMask, int maxBlockMinutes = 120) =>
        _core.PlanWeek(from, to, workStartMinute, workEndMinute, workdayMask, maxBlockMinutes);

    public IReadOnlyList<DayLoadDto> DayLoads(DateTimeOffset from, DateTimeOffset to,
        int workStartMinute = 540, int workEndMinute = 1080,
        int workdayMask = EquoraCore.DefaultWorkdayMask) =>
        _core.DayLoads(from, to, workStartMinute, workEndMinute, workdayMask);

    public IReadOnlyList<SplitSuggestionDto> SuggestSplits(DateTimeOffset from,
        DateTimeOffset to, int maxBlockMinutes = 120) =>
        _core.SuggestSplits(from, to, maxBlockMinutes);

    public int? CorrectEstimate(string taskId) => _core.CorrectEstimate(taskId);

    public DailyReviewDto ComputeDailyReview(DateTimeOffset dayStartLocal) =>
        _core.ComputeDailyReview(dayStartLocal);
    public void SaveDailyReview(DateTimeOffset dayStartLocal, string notes = "",
        string focusTomorrow = "") =>
        _core.SaveDailyReview(dayStartLocal, notes, focusTomorrow);
    public WeeklyReviewDto ComputeWeeklyReview(DateTimeOffset weekStartLocal) =>
        _core.ComputeWeeklyReview(weekStartLocal);
    public void SaveWeeklyReview(DateTimeOffset weekStartLocal, string notes = "",
        string nextWeekGoals = "") =>
        _core.SaveWeeklyReview(weekStartLocal, notes, nextWeekGoals);

    public AutomationRuleDto CreateAutomationRule(string name, string trigger,
        string actions, string conditions = "{}") =>
        _core.CreateAutomationRule(name, trigger, actions, conditions);
    public IReadOnlyList<AutomationRuleDto> ListAutomationRules() =>
        _core.ListAutomationRules();
    public void SetAutomationEnabled(string id, bool enabled) =>
        _core.SetAutomationEnabled(id, enabled);
    public void DeleteAutomationRule(string id) => _core.DeleteAutomationRule(id);
    public IReadOnlyList<string> EvaluateAutomation(string trigger, string taskId) =>
        _core.EvaluateAutomation(trigger, taskId);
}
