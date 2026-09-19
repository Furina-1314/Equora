#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/core/CalendarRepository.h>
#include <equora/core/FocusRepository.h>
#include <equora/core/TaskRepository.h>
#include <equora/domain/Task.h>
#include <equora/domain/Time.h>
#include <equora/storage/Database.h>

namespace equora::core {

// ---- 复盘 DTO(计算结果;存档保留快照) ----

struct DailyReviewData {
    std::string localDate; // YYYY-MM-DD
    int completedCount = 0;
    int deferredCount = 0;   // 当日截止但未完成
    int cancelledCount = 0;
    std::int64_t plannedMinutes = 0;
    std::int64_t actualMinutes = 0;  // 当日时间块实际/专注有效
    int bigThreeDone = 0;            // 今日要事完成数
    int distractionCount = 0;
    std::string notes;
    std::string focusTomorrow;
};

struct WeeklyReviewData {
    std::string weekStartDate;
    std::int64_t deepWorkMinutes = 0; // 有效专注总时长
    double focusRatio = 0;            // 完成/(完成+放弃)
    double estimateAccuracy = 0;      // 估时接近度 0..1(样本充足时)
    int bestFocusHour = -1;           // 高产时段(完成会话中位起点小时,本地)
    std::string notes;
    std::string nextWeekGoals;
};

/// 复盘:从任务/时间块/专注会话聚合计算并落库存档(按日/周唯一,可覆盖更新)。
class ReviewRepository {
public:
    ReviewRepository(const storage::Database& db, std::string deviceId);

    // dayStartUtc 为该本地日的 UTC 起点(调用方按用户时区计算)。
    [[nodiscard]] DailyReviewData computeDaily(domain::UtcMillis dayStartUtc,
                                               const TaskRepository& tasks,
                                               const CalendarRepository& calendar,
                                               const FocusRepository& focus,
                                               int tzOffsetMinutes) const;
    [[nodiscard]] WeeklyReviewData computeWeekly(domain::UtcMillis weekStartUtc,
                                                 const TaskRepository& tasks,
                                                 const CalendarRepository& calendar,
                                                 const FocusRepository& focus,
                                                 int tzOffsetMinutes) const;

    void saveDaily(const DailyReviewData& data) const;
    void saveWeekly(const WeeklyReviewData& data) const;
    [[nodiscard]] std::optional<DailyReviewData> loadDaily(const std::string& localDate) const;
    [[nodiscard]] std::optional<WeeklyReviewData> loadWeekly(
        const std::string& weekStartDate) const;
    [[nodiscard]] std::vector<DailyReviewData> recentDaily(int limit) const;

private:
    const storage::Database& db_;
    std::string deviceId_;
};

// ---- 自动化 ----

struct AutomationRule {
    std::string id;
    std::string name;
    std::string trigger;    // JSON 文本
    std::string conditions; // JSON 文本
    std::string actions;    // JSON 文本
    bool enabled = true;
    domain::UtcMillis createdAt = 0;
    domain::UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
};

struct AutomationLogEntry {
    std::string id;
    std::string ruleId;
    std::string entityId;
    domain::UtcMillis triggeredAt = 0;
    bool success = false;
    std::string error;
};

/// 自动化:规则 CRUD + 评估(纯查询,动作由调用方执行 —— 防递归:
/// 评估产生的动作不再触发评估)。
class AutomationRepository {
public:
    AutomationRepository(const storage::Database& db, std::string deviceId);

    [[nodiscard]] AutomationRule create(AutomationRule draft) const;
    [[nodiscard]] std::optional<AutomationRule> find(const std::string& id) const;
    [[nodiscard]] AutomationRule update(AutomationRule rule) const;
    void setEnabled(const std::string& id, bool enabled) const;
    void remove(const std::string& id) const;
    [[nodiscard]] std::vector<AutomationRule> listEnabled() const;

    /// 评估某实体事件命中的规则(trigger 如 "task_created");返回命中规则 id 列表。
    [[nodiscard]] std::vector<std::string> evaluate(const std::string& trigger,
                                                    const domain::Task& task) const;

    void log(const std::string& ruleId, const std::string& entityId, bool success,
             const std::string& error) const;
    [[nodiscard]] std::vector<AutomationLogEntry> recentLogs(int limit = 50) const;

private:
    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
