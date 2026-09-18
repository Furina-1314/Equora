#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace equora::domain {

// 自 Unix 纪元(1970-01-01T00:00:00Z)起的 UTC 毫秒数。
// 所有持久化时间统一为该表示;本地时区仅在展示层转换。
using UtcMillis = std::int64_t;

// 公历日期(不含时间与时区)。
struct CivilDate {
    int year = 1970;
    unsigned month = 1;  // 1..12
    unsigned day = 1;    // 1..31(按月校验)
};

// 公历日期时间(UTC 语义,不含时区偏移)。
struct CivilDateTime {
    int year = 1970;
    unsigned month = 1;
    unsigned day = 1;
    unsigned hour = 0;        // 0..23
    unsigned minute = 0;      // 0..59
    unsigned second = 0;      // 0..59
    unsigned millisecond = 0; // 0..999

    [[nodiscard]] bool operator==(const CivilDateTime&) const = default;
};

namespace utc {

[[nodiscard]] UtcMillis now() noexcept;

// 格式化为 YYYY-MM-DDTHH:MM:SS.mmmZ(毫秒恒定 3 位)。
[[nodiscard]] std::string toIso8601(UtcMillis t);

// 解析 YYYY-MM-DDTHH:MM:SS[.f{1..9}](Z|z)。年份 0001..9999。
// 拒绝越界月/日/时/分/秒与非规范分隔符。
[[nodiscard]] std::optional<UtcMillis> parseIso8601(std::string_view text);

[[nodiscard]] CivilDateTime civilFromMillis(UtcMillis t) noexcept;
[[nodiscard]] UtcMillis millisFromCivil(const CivilDateTime& dt) noexcept;

// 天数换算(Hinnant 算法),供日历/重复规则模块复用。
[[nodiscard]] std::int64_t daysFromCivil(int year, unsigned month, unsigned day) noexcept;
[[nodiscard]] CivilDate civilFromDays(std::int64_t days) noexcept;
[[nodiscard]] bool isLeapYear(int year) noexcept;
[[nodiscard]] unsigned daysInMonth(int year, unsigned month) noexcept;

// ---- 本地日期与 UTC 换算 ----
// offsetMinutes 为用户时区相对 UTC 的偏移(东八区 = +480)。
// 「今天」等智能清单的日界由调用方传入当前偏移,业务规则保留在核心层。

// 时刻所属的本地日序号(自 1970-01-01 起,floor 除法)。
[[nodiscard]] std::int64_t localDayIndex(UtcMillis t, int offsetMinutes) noexcept;

// 本地日序号当天 00:00 对应的 UTC 时刻(区间左端,含)。
[[nodiscard]] UtcMillis localDayStartUtc(std::int64_t localDay, int offsetMinutes) noexcept;

} // namespace utc
} // namespace equora::domain
