#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

// 任务在日历中的计划时间段。task_id 可空 = 独立占位块;
// 拖回待办区只删除块,任务仍在(软删除任务不级联删块,硬清除时级联)。
struct TimeBlock {
    std::string id;
    std::optional<std::string> taskId;
    std::string calendarId; // 空 = 默认日历
    UtcMillis startAt = 0;
    UtcMillis endAt = 0;    // 闭开区间;endAt > startAt
    std::int32_t prepareMinutes = 0;  // 前置准备/通勤
    std::int32_t bufferMinutes = 0;   // 后置缓冲
    std::int32_t actualMinutes = 0;
    std::string note;
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] std::int64_t durationMinutes() const noexcept {
        return (endAt - startAt) / 60'000;
    }
};

} // namespace equora::domain
