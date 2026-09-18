#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

// 标签实体。名称在活动标签内唯一(大小写敏感;由仓库层校验)。
struct Tag {
    std::string id;
    std::string name;
    std::string color; // "#RRGGBB",可空
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] static Tag draft(std::string name);
};

} // namespace equora::domain
