#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

// 日历(本地来源;ICS/CalDAV 接入在后续阶段)。
struct Calendar {
    std::string id;
    std::string name;
    std::string color;   // "#RRGGBB"
    std::string source = "local";
    bool isVisible = true;
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] static Calendar draft(std::string name);
};

} // namespace equora::domain
