#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Task.h>
#include <equora/domain/Time.h>

namespace equora::core {

enum class TaskSort : std::int32_t {
    CreatedAsc  = 0,
    CreatedDesc = 1,
    DueAsc      = 2,
    DueDesc     = 3,
    PriorityDesc = 4,
    UpdatedDesc = 5,
};

// 任务查询过滤:智能清单与搜索的统一底座。
// dueAfter/dueBefore 构成闭开区间 [after, before);updated 同义。
struct TaskFilter {
    enum DueState : std::int32_t { DueAny = 0, DueSet = 1, DueUnset = 2 };

    std::int32_t dueState = DueAny;
    std::optional<domain::UtcMillis> dueAfter;
    std::optional<domain::UtcMillis> dueBefore;
    std::optional<domain::UtcMillis> updatedAfter;
    std::optional<domain::UtcMillis> updatedBefore;
    std::optional<std::string> projectId;
    std::optional<std::string> tagId;
    std::string searchText; // 标题/备注子串,大小写不敏感
    std::vector<domain::TaskStatus> statuses;
    std::vector<domain::TaskStatus> excludedStatuses;
    bool includeDeleted = false;
    TaskSort sort = TaskSort::CreatedAsc;
    std::int32_t limit = 0;  // 0 = 不限
    std::int32_t offset = 0;
};

// 智能清单预设:语义定义在核心层,保证各视图一致;
// 「今天」等日界由调用方传当前 UTC 偏移分钟数(东八区 = +480)。
struct SmartLists {
    [[nodiscard]] static TaskFilter inbox();
    [[nodiscard]] static TaskFilter today(domain::UtcMillis nowMs, int offsetMinutes);
    [[nodiscard]] static TaskFilter upcoming(domain::UtcMillis nowMs, int offsetMinutes,
                                             int days);
    [[nodiscard]] static TaskFilter overdue(domain::UtcMillis nowMs);
    [[nodiscard]] static TaskFilter noDate();
    [[nodiscard]] static TaskFilter scheduled();
    [[nodiscard]] static TaskFilter waiting();
    [[nodiscard]] static TaskFilter completed();
    [[nodiscard]] static TaskFilter completedToday(domain::UtcMillis nowMs, int offsetMinutes);
};

} // namespace equora::core
