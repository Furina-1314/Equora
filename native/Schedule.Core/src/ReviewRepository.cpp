#include <equora/core/ReviewRepository.h>

#include <algorithm>
#include <map>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>
#include <nlohmann/json.hpp>
#include <equora/scheduling/Recurrence.h>
#include <equora/storage/Database.h>

namespace equora::core {

using storage::Transaction;

using domain::ErrorCode;
using domain::EquoraError;
using domain::Task;
using domain::TaskStatus;
using domain::Uuid;
using domain::utc::now;

namespace {

[[nodiscard]] std::string localDateText(domain::UtcMillis t, int tz) {
    return equora::scheduling::localDateString(t, tz);
}

} // namespace

ReviewRepository::ReviewRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

DailyReviewData ReviewRepository::computeDaily(domain::UtcMillis dayStartUtc,
                                               const TaskRepository& tasks,
                                               const CalendarRepository& calendar,
                                               const FocusRepository& focus,
                                               int tzOffsetMinutes) const {
    DailyReviewData data;
    data.localDate = localDateText(dayStartUtc, tzOffsetMinutes);
    const domain::UtcMillis dayEnd = dayStartUtc + 86'400'000;

    // 任务侧:状态变更以 updated_at 当日落在本日的为准(简化:当日完成的/取消的)。
    for (const auto& t : tasks.query({})) {
        if (t.updatedAt < dayStartUtc || t.updatedAt >= dayEnd) continue;
        if (t.status == TaskStatus::Done) ++data.completedCount;
        if (t.status == TaskStatus::Cancelled) ++data.cancelledCount;
    }
    // 延期 = 截止在本日之前且仍未完成。
    for (const auto& t : tasks.query({})) {
        if (t.dueAt.has_value() && *t.dueAt < dayStartUtc &&
            t.status != TaskStatus::Done && t.status != TaskStatus::Cancelled) {
            ++data.deferredCount;
        }
    }

    // 时间块计划时长 + 专注有效时长。
    for (const auto& b : calendar.blocksInRange(dayStartUtc, dayEnd)) {
        data.plannedMinutes += b.durationMinutes();
        data.actualMinutes += b.actualMinutes;
    }
    for (const auto& s : focus.history(dayStartUtc, dayEnd)) {
        data.actualMinutes += s.effectiveMs(dayEnd) / 60'000;
        data.distractionCount += static_cast<int>(focus.interruptionsOf(s.id).size());
    }

    return data;
}

WeeklyReviewData ReviewRepository::computeWeekly(domain::UtcMillis weekStartUtc,
                                                 const TaskRepository& tasks,
                                                 const CalendarRepository& calendar,
                                                 const FocusRepository& focus,
                                                 int tzOffsetMinutes) const {
    WeeklyReviewData data;
    data.weekStartDate = localDateText(weekStartUtc, tzOffsetMinutes);
    const domain::UtcMillis weekEnd = weekStartUtc + 7LL * 86'400'000;

    int completed = 0;
    int abandoned = 0;
    std::map<int, int> hourHistogram; // 完成会话的本地起点小时
    std::vector<std::int64_t> plannedVsActual;

    for (const auto& s : focus.history(weekStartUtc, weekEnd)) {
        if (s.state == domain::SessionState::Completed) {
            ++completed;
            data.deepWorkMinutes += s.effectiveMs(weekEnd) / 60'000;
            const int hour = static_cast<int>(
                ((s.actualStart + static_cast<std::int64_t>(tzOffsetMinutes) * 60'000)
                 % 86'400'000) / 3'600'000);
            ++hourHistogram[hour];
            if (s.taskId.has_value()) {
                plannedVsActual.push_back(s.effectiveMs(weekEnd) / 60'000);
            }
        }
        else if (s.state == domain::SessionState::Abandoned) {
            ++abandoned;
        }
    }

    data.focusRatio = completed + abandoned == 0
        ? 0.0
        : static_cast<double>(completed) / (completed + abandoned);

    // 估时准确度:完成任务的 |实际-估计|/估计 平均(样本>=2 才有意义)。
    if (plannedVsActual.size() >= 2) {
        double sum = 0;
        int n = 0;
        for (const auto& t : tasks.query({})) {
            if (t.status == TaskStatus::Done && t.estimateMinutes.has_value() &&
                t.updatedAt >= weekStartUtc && t.updatedAt < weekEnd && t.actualMinutes > 0) {
                const double est = static_cast<double>(*t.estimateMinutes);
                const double act = static_cast<double>(t.actualMinutes);
                sum += std::max(0.0, 1.0 - std::abs(act - est) / est);
                ++n;
            }
        }
        data.estimateAccuracy = n == 0 ? 0.0 : sum / n;
    }

    if (!hourHistogram.empty()) {
        data.bestFocusHour = std::max_element(hourHistogram.begin(), hourHistogram.end(),
                                              [](const auto& a, const auto& b) {
                                                  return a.second < b.second;
                                              })->first;
    }
    return data;
}

void ReviewRepository::saveDaily(const DailyReviewData& data) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO daily_reviews (id, local_date, completed_count, deferred_count, "
            "cancelled_count, planned_minutes, actual_minutes, big_three_done, "
            "distraction_count, notes, focus_tomorrow, created_at, updated_at, revision, "
            "deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, NULL, ?) "
            "ON CONFLICT(local_date) DO UPDATE SET "
            "completed_count = excluded.completed_count, "
            "deferred_count = excluded.deferred_count, "
            "cancelled_count = excluded.cancelled_count, "
            "planned_minutes = excluded.planned_minutes, "
            "actual_minutes = excluded.actual_minutes, "
            "big_three_done = excluded.big_three_done, "
            "distraction_count = excluded.distraction_count, "
            "notes = excluded.notes, focus_tomorrow = excluded.focus_tomorrow, "
            "updated_at = excluded.updated_at, revision = revision + 1");
        st.bind(1, Uuid::random().toString())
            .bind(2, data.localDate)
            .bind(3, data.completedCount)
            .bind(4, data.deferredCount)
            .bind(5, data.cancelledCount)
            .bind(6, data.plannedMinutes)
            .bind(7, data.actualMinutes)
            .bind(8, data.bigThreeDone)
            .bind(9, data.distractionCount)
            .bind(10, data.notes)
            .bind(11, data.focusTomorrow)
            .bind(12, now())
            .bind(13, now())
            .bind(14, deviceId_);
        st.step();
    }
    tx.commit();
}

void ReviewRepository::saveWeekly(const WeeklyReviewData& data) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO weekly_reviews (id, week_start_date, deep_work_minutes, "
            "focus_ratio, estimate_accuracy, best_focus_hour, notes, next_week_goals, "
            "created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, NULL, ?) "
            "ON CONFLICT(week_start_date) DO UPDATE SET "
            "deep_work_minutes = excluded.deep_work_minutes, "
            "focus_ratio = excluded.focus_ratio, "
            "estimate_accuracy = excluded.estimate_accuracy, "
            "best_focus_hour = excluded.best_focus_hour, "
            "notes = excluded.notes, next_week_goals = excluded.next_week_goals, "
            "updated_at = excluded.updated_at, revision = revision + 1");
        st.bind(1, Uuid::random().toString())
            .bind(2, data.weekStartDate)
            .bind(3, data.deepWorkMinutes)
            .bind(4, data.focusRatio)
            .bind(5, data.estimateAccuracy)
            .bind(6, data.bestFocusHour)
            .bind(7, data.notes)
            .bind(8, data.nextWeekGoals)
            .bind(9, now())
            .bind(10, now())
            .bind(11, deviceId_);
        st.step();
    }
    tx.commit();
}

std::optional<DailyReviewData> ReviewRepository::loadDaily(
    const std::string& localDate) const {
    auto st = db_.prepare(
        "SELECT local_date, completed_count, deferred_count, cancelled_count, "
        "planned_minutes, actual_minutes, big_three_done, distraction_count, notes, "
        "focus_tomorrow FROM daily_reviews WHERE local_date = ?");
    st.bind(1, localDate);
    if (!st.step()) return std::nullopt;
    DailyReviewData d;
    d.localDate = st.columnText(0);
    d.completedCount = static_cast<int>(st.columnInt(1));
    d.deferredCount = static_cast<int>(st.columnInt(2));
    d.cancelledCount = static_cast<int>(st.columnInt(3));
    d.plannedMinutes = st.columnInt(4);
    d.actualMinutes = st.columnInt(5);
    d.bigThreeDone = static_cast<int>(st.columnInt(6));
    d.distractionCount = static_cast<int>(st.columnInt(7));
    d.notes = st.columnText(8);
    d.focusTomorrow = st.columnText(9);
    return d;
}

std::optional<WeeklyReviewData> ReviewRepository::loadWeekly(
    const std::string& weekStartDate) const {
    auto st = db_.prepare(
        "SELECT week_start_date, deep_work_minutes, focus_ratio, estimate_accuracy, "
        "best_focus_hour, notes, next_week_goals FROM weekly_reviews "
        "WHERE week_start_date = ?");
    st.bind(1, weekStartDate);
    if (!st.step()) return std::nullopt;
    WeeklyReviewData w;
    w.weekStartDate = st.columnText(0);
    w.deepWorkMinutes = st.columnInt(1);
    w.focusRatio = st.columnDouble(2);
    w.estimateAccuracy = st.columnDouble(3);
    w.bestFocusHour = static_cast<int>(st.columnInt(4));
    w.notes = st.columnText(5);
    w.nextWeekGoals = st.columnText(6);
    return w;
}

std::vector<DailyReviewData> ReviewRepository::recentDaily(int limit) const {
    auto st = db_.prepare(
        "SELECT local_date, completed_count, deferred_count, cancelled_count, "
        "planned_minutes, actual_minutes, big_three_done, distraction_count, notes, "
        "focus_tomorrow FROM daily_reviews ORDER BY local_date DESC LIMIT ?");
    st.bind(1, limit);
    std::vector<DailyReviewData> out;
    while (st.step()) {
        DailyReviewData d;
        d.localDate = st.columnText(0);
        d.completedCount = static_cast<int>(st.columnInt(1));
        d.deferredCount = static_cast<int>(st.columnInt(2));
        d.cancelledCount = static_cast<int>(st.columnInt(3));
        d.plannedMinutes = st.columnInt(4);
        d.actualMinutes = st.columnInt(5);
        d.bigThreeDone = static_cast<int>(st.columnInt(6));
        d.distractionCount = static_cast<int>(st.columnInt(7));
        d.notes = st.columnText(8);
        d.focusTomorrow = st.columnText(9);
        out.push_back(std::move(d));
    }
    return out;
}

// ---- 自动化 ----

AutomationRepository::AutomationRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

AutomationRule AutomationRepository::create(AutomationRule draft) const {
    if (draft.name.empty() || draft.trigger.empty() || draft.actions.empty()) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "automation rule requires name, trigger and actions");
    }
    draft.id = Uuid::random().toString();
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO automation_rules (id, name, trigger, conditions, actions, "
            "enabled, created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.name)
            .bind(3, draft.trigger)
            .bind(4, draft.conditions.empty() ? "{}" : draft.conditions)
            .bind(5, draft.actions)
            .bind(6, draft.enabled ? 1 : 0)
            .bind(7, draft.createdAt)
            .bind(8, draft.updatedAt)
            .bind(9, draft.revision)
            .bind(10, deviceId_);
        st.step();
    }
    tx.commit();
    return draft;
}

std::optional<AutomationRule> AutomationRepository::find(const std::string& id) const {
    auto st = db_.prepare(
        "SELECT id, name, trigger, conditions, actions, enabled, created_at, updated_at, "
        "revision FROM automation_rules WHERE id = ? AND deleted_at IS NULL");
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    AutomationRule r;
    r.id = st.columnText(0);
    r.name = st.columnText(1);
    r.trigger = st.columnText(2);
    r.conditions = st.columnText(3);
    r.actions = st.columnText(4);
    r.enabled = st.columnInt(5) != 0;
    r.createdAt = st.columnInt(6);
    r.updatedAt = st.columnInt(7);
    r.revision = st.columnInt(8);
    return r;
}

AutomationRule AutomationRepository::update(AutomationRule rule) const {
    auto existing = find(rule.id);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "automation rule not found");
    }
    if (rule.revision != existing->revision) {
        throw EquoraError(ErrorCode::Conflict, "stale revision for automation rule");
    }
    rule.updatedAt = now();
    rule.revision += 1;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE automation_rules SET name = ?, trigger = ?, conditions = ?, "
            "actions = ?, enabled = ?, updated_at = ?, revision = ?, last_device_id = ? "
            "WHERE id = ? AND revision = ?");
        st.bind(1, rule.name)
            .bind(2, rule.trigger)
            .bind(3, rule.conditions)
            .bind(4, rule.actions)
            .bind(5, rule.enabled ? 1 : 0)
            .bind(6, rule.updatedAt)
            .bind(7, rule.revision)
            .bind(8, deviceId_)
            .bind(9, rule.id)
            .bind(10, rule.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of automation rule");
        }
    }
    tx.commit();
    return rule;
}

void AutomationRepository::setEnabled(const std::string& id, bool enabled) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE automation_rules SET enabled = ?, updated_at = ?, "
            "revision = revision + 1 WHERE id = ? AND deleted_at IS NULL");
        st.bind(1, enabled ? 1 : 0).bind(2, now()).bind(3, id);
        st.step();
    }
    tx.commit();
}

void AutomationRepository::remove(const std::string& id) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE automation_rules SET deleted_at = ?, updated_at = ?, "
            "revision = revision + 1 WHERE id = ? AND deleted_at IS NULL");
        st.bind(1, now()).bind(2, now()).bind(3, id);
        st.step();
    }
    tx.commit();
}

std::vector<AutomationRule> AutomationRepository::listEnabled() const {
    auto st = db_.prepare(
        "SELECT id, name, trigger, conditions, actions, enabled, created_at, updated_at, "
        "revision FROM automation_rules WHERE enabled = 1 AND deleted_at IS NULL "
        "ORDER BY created_at");
    std::vector<AutomationRule> out;
    while (st.step()) {
        AutomationRule r;
        r.id = st.columnText(0);
        r.name = st.columnText(1);
        r.trigger = st.columnText(2);
        r.conditions = st.columnText(3);
        r.actions = st.columnText(4);
        r.enabled = st.columnInt(5) != 0;
        r.createdAt = st.columnInt(6);
        r.updatedAt = st.columnInt(7);
        r.revision = st.columnInt(8);
        out.push_back(std::move(r));
    }
    return out;
}

std::vector<std::string> AutomationRepository::evaluate(const std::string& trigger,
                                                        const Task& task) const {
    std::vector<std::string> hits;
    for (const auto& rule : listEnabled()) {
        try {
            auto t = nlohmann::json::parse(rule.trigger);
            if (t.value("type", "") != trigger) continue;

            if (!rule.conditions.empty() && rule.conditions != "{}") {
                auto c = nlohmann::json::parse(rule.conditions);
                if (c.contains("tag") && !c["tag"].get<std::string>().empty()) {
                    // 标签条件由调用方补查(仓库无法直接查任务标签);
                    // 命中条件不足时保守跳过。
                    continue;
                }
                if (c.contains("priority_gte")) {
                    if (static_cast<std::int64_t>(task.priority) <
                        c["priority_gte"].get<std::int64_t>()) {
                        continue;
                    }
                }
                if (c.contains("estimate_gte") && task.estimateMinutes.has_value()) {
                    if (*task.estimateMinutes < c["estimate_gte"].get<std::int64_t>()) {
                        continue;
                    }
                }
            }
            hits.push_back(rule.id);
        } catch (const std::exception&) {
            // 规则 JSON 损坏:跳过该规则(不影响其他),错误可见于日志。
            continue;
        }
    }
    return hits;
}

void AutomationRepository::log(const std::string& ruleId, const std::string& entityId,
                               bool success, const std::string& error) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO automation_logs (id, rule_id, entity_id, triggered_at, success, "
            "error) VALUES (?, ?, ?, ?, ?, ?)");
        st.bind(1, Uuid::random().toString())
            .bind(2, ruleId)
            .bind(3, entityId)
            .bind(4, now())
            .bind(5, success ? 1 : 0)
            .bind(6, error);
        st.step();
    }
    tx.commit();
}

std::vector<AutomationLogEntry> AutomationRepository::recentLogs(int limit) const {
    auto st = db_.prepare(
        "SELECT id, rule_id, entity_id, triggered_at, success, error "
        "FROM automation_logs ORDER BY triggered_at DESC, rowid DESC LIMIT ?");
    st.bind(1, limit);
    std::vector<AutomationLogEntry> out;
    while (st.step()) {
        AutomationLogEntry e;
        e.id = st.columnText(0);
        e.ruleId = st.columnText(1);
        e.entityId = st.columnText(2);
        e.triggeredAt = st.columnInt(3);
        e.success = st.columnInt(4) != 0;
        e.error = st.columnText(5);
        out.push_back(std::move(e));
    }
    return out;
}

} // namespace equora::core
