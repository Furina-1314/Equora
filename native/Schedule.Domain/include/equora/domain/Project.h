#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

enum class ProjectStatus : std::int32_t {
    Active   = 0,
    Archived = 1,
};

// 项目实体。归档用 status 表达;墓碑语义与其他实体一致。
struct Project {
    std::string id;
    std::string name;
    std::string color; // "#RRGGBB",可空
    std::string goal;  // 目标说明,可空
    ProjectStatus status = ProjectStatus::Active;
    std::optional<UtcMillis> archivedAt;
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] static Project draft(std::string name);
};

} // namespace equora::domain
