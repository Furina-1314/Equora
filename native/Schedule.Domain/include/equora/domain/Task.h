#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

enum class TaskStatus : std::int32_t {
    Inbox      = 0,
    Planned    = 1, // 已安排时间块
    InProgress = 2, // 专注执行中
    Waiting    = 3, // 等待他人/外部事件
    Done       = 4,
    Cancelled  = 5,
};

enum class Priority : std::int32_t {
    None   = 0,
    Low    = 1,
    Normal = 2,
    High   = 3,
    Urgent = 4,
};

// 任务实体。字段构成从第一天起面向同步:
// id 为 UUID;revision 单调递增;deleted_at 为软删除墓碑。
struct Task {
    std::string id;             // UUID,创建时生成
    std::string title;
    std::string note;
    TaskStatus status = TaskStatus::Inbox;
    Priority priority = Priority::Normal;
    std::int32_t importance = 0;              // 0=未设置,1(低)..5(关键);由用户/建议决定
    std::optional<UtcMillis> dueAt;           // 截止时间(UTC);日期语义在 P6 引入
    std::optional<std::int32_t> estimateMinutes;
    std::int32_t actualMinutes = 0;
    std::optional<std::string> projectId;     // P4 起使用
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;       // 软删除墓碑
    std::string lastDeviceId;

    // 创建新任务草稿:仅填业务字段,id/时间戳/revision 由仓库补全。
    [[nodiscard]] static Task draft(std::string title);
};

} // namespace equora::domain
