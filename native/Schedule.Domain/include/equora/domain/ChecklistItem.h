#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

// 任务内部检查项:轻量勾选列表,软删除与其他实体一致。
struct ChecklistItem {
    std::string id;
    std::string taskId;
    std::string content;
    bool isChecked = false;
    std::int32_t sortOrder = 0; // 同一任务内从 1 递增
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;
};

} // namespace equora::domain
