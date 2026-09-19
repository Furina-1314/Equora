#pragma once

#include <cstdint>
#include <vector>

#include <equora/scheduling/Conflict.h>

namespace equora::scheduling {

struct FreeSlot {
    domain::UtcMillis start = 0;
    domain::UtcMillis end = 0;
    [[nodiscard]] std::int64_t minutes() const noexcept { return (end - start) / 60'000; }
};

struct WorkHours {
    std::int32_t startMinute = 9 * 60;  // 当地 09:00
    std::int32_t endMinute = 18 * 60;   // 当地 18:00
    std::vector<std::int32_t> workdays = {0, 1, 2, 3, 4}; // Mon..Fri(Weekday 值)
};

// 在 [from, to) 内按工作时段查找长度 >= minMinutes 的连续空闲段。
// span 覆盖会从每日工作窗中扣除;结果按开始时间升序,至多 limit 条(0=不限)。
[[nodiscard]] std::vector<FreeSlot> findFreeSlots(const std::vector<Span>& busy,
                                                  domain::UtcMillis from,
                                                  domain::UtcMillis to,
                                                  const WorkHours& hours,
                                                  std::int64_t minMinutes,
                                                  std::size_t limit = 0,
                                                  int tzOffsetMinutes = 0);

} // namespace equora::scheduling
