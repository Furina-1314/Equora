#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

// 非任务型日程:会议、提醒、全天事项。可挂重复规则。
struct CalendarEvent {
    std::string id;
    std::string title;
    std::string location;
    std::string note;
    std::string calendarId;
    UtcMillis startAt = 0;
    UtcMillis endAt = 0;
    bool isAllDay = false; // 全天:调用方按本地日界计算 [start,end)
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] static CalendarEvent draft(std::string title);
};

} // namespace equora::domain
