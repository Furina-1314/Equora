#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

#include <equora/domain/RecurrenceRule.h>
#include <equora/domain/Time.h>

namespace equora::scheduling {

// 展开产物:虚拟实例(非物化记录)。
struct Instance {
    std::string ruleId;
    std::string hostType;
    std::string hostId;
    domain::UtcMillis startUtc = 0;
    domain::UtcMillis endUtc = 0;
    bool isException = false; // 被排除日期上被过滤前先标记
};

// 把规则在 [windowFrom, windowTo) 内展开。
// seedStart/seedEnd 是宿主自身的时间(第一实例);duration 固定为种子时长。
// tzOffsetMinutes 用于例外日期(本地)与月度模式计算。上限保护:最多 4096 个实例。
[[nodiscard]] std::vector<Instance> expandRecurrence(const domain::RecurrenceRule& rule,
                                                     domain::UtcMillis seedStart,
                                                     domain::UtcMillis seedEnd,
                                                     domain::UtcMillis windowFrom,
                                                     domain::UtcMillis windowTo,
                                                     int tzOffsetMinutes);

// 工具:某年月第 n 个周 X 的本地日序号;n=-1 表示最后一个。越界返回 nullopt。
[[nodiscard]] std::optional<std::int64_t> nthWeekdayOfMonth(int year, unsigned month,
                                                            domain::Weekday weekday, int nth);
// 本地日期文本(按 tz 偏移)"YYYY-MM-DD"。
[[nodiscard]] std::string localDateString(domain::UtcMillis t, int tzOffsetMinutes);

} // namespace equora::scheduling
