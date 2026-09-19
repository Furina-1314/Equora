#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Time.h>

namespace equora::domain {

enum class Weekday : std::int32_t { Mon = 0, Tue, Wed, Thu, Fri, Sat, Sun };

enum class RecurFreq : std::int32_t { Daily = 0, Weekly, Monthly, Yearly };

// 月度模式:date=每月同日;nthWeekday=每月第 n 个周 X;lastWeekday=每月最后一个周 X。
enum class MonthMode : std::int32_t { Date = 0, NthWeekday, LastWeekday };

// RRULE 子集(FREQ/INTERVAL/BYDAY/UNTIL/COUNT + 月度模式扩展),
// 另有应用层语义 completeRecurDays(完成后 N 天再次出现,非 RRULE 标准)。
struct RecurrenceRule {
    std::string id;
    std::string hostType; // "task" | "event"
    std::string hostId;
    RecurFreq freq = RecurFreq::Daily;
    std::int32_t interval = 1;              // 每 N 个周期
    std::vector<Weekday> byWeekday;         // 仅 Weekly
    MonthMode monthMode = MonthMode::Date;  // 仅 Monthly
    std::int32_t monthNth = 1;              // NthWeekday:1..5
    Weekday monthWeekday = Weekday::Mon;
    std::optional<UtcMillis> untilUtc;      // 含;实例开始 < until 才出现
    std::optional<std::int64_t> maxCount;   // 总实例数上限(含种子)
    std::int32_t completeRecurDays = 0;     // >0:完成后 N 天再排(任务型)
    std::vector<std::string> excludedDates; // 本地日期 "YYYY-MM-DD"
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] std::string toRruleString() const;
    // 解析 FREQ/INTERVAL/BYDAY/UNTIL/COUNT;不支持的部分返回 nullopt 并给出原因。
    [[nodiscard]] static std::optional<RecurrenceRule> parseRrule(std::string_view rrule,
                                                                  std::string* whyNot);
};

} // namespace equora::domain
