#include <equora/domain/Time.h>

#include <chrono>
#include <cstdio>
#include <utility>

namespace equora::domain::utc {

UtcMillis now() noexcept {
    const auto tp = std::chrono::system_clock::now().time_since_epoch();
    return std::chrono::duration_cast<std::chrono::milliseconds>(tp).count();
}

bool isLeapYear(int year) noexcept {
    return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
}

unsigned daysInMonth(int year, unsigned month) noexcept {
    static constexpr unsigned kTable[13] = {
        0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    if (month < 1 || month > 12) return 0;
    if (month == 2 && isLeapYear(year)) return 29;
    return kTable[month];
}

// Howard Hinnant 的 days_from_civil / civil_from_days 算法,
// 覆盖公历全范围且不依赖平台本地时区实现。
std::int64_t daysFromCivil(int y, unsigned m, unsigned d) noexcept {
    y -= m <= 2;
    const std::int64_t era = (y >= 0 ? y : y - 399) / 400;
    const unsigned yoe = static_cast<unsigned>(y - era * 400);            // [0, 399]
    const unsigned doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;  // [0, 365]
    const unsigned doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;           // [0, 146096]
    return era * 146097 + static_cast<std::int64_t>(doe) - 719468;
}

CivilDate civilFromDays(std::int64_t z) noexcept {
    z += 719468;
    const std::int64_t era = (z >= 0 ? z : z - 146096) / 146097;
    const unsigned doe = static_cast<unsigned>(z - era * 146097);             // [0, 146096]
    const unsigned yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365; // [0, 399]
    const std::int64_t y = static_cast<std::int64_t>(yoe) + era * 400;
    const unsigned doy = doe - (365 * yoe + yoe / 4 - yoe / 100);             // [0, 365]
    const unsigned mp = (5 * doy + 2) / 153;                                  // [0, 11]
    const unsigned d = doy - (153 * mp + 2) / 5 + 1;                          // [1, 31]
    const unsigned m = mp + (mp < 10 ? 3 : -9);                               // [1, 12]
    CivilDate out;
    out.year = static_cast<int>(y + (m <= 2));
    out.month = m;
    out.day = d;
    return out;
}

namespace {

// 安全除法:向零取整(C++ 语义)对负数同样成立,配合余数恒非负使用。
[[nodiscard]] constexpr std::pair<std::int64_t, std::int64_t> divMod(std::int64_t a, std::int64_t b) noexcept {
    const std::int64_t q = a / b;
    const std::int64_t r = a % b;
    return (r != 0 && ((r < 0) != (b < 0))) ? std::make_pair(q - 1, r + b)
                                            : std::make_pair(q, r);
}

} // namespace

CivilDateTime civilFromMillis(UtcMillis t) noexcept {
    auto [days, msOfDay] = divMod(t, 86'400'000);
    const CivilDate d = civilFromDays(days);
    auto [hour, msRest1] = divMod(msOfDay, 3'600'000);
    auto [minute, msRest2] = divMod(msRest1, 60'000);
    auto [second, ms] = divMod(msRest2, 1'000);

    CivilDateTime out;
    out.year = d.year;
    out.month = d.month;
    out.day = d.day;
    out.hour = static_cast<unsigned>(hour);
    out.minute = static_cast<unsigned>(minute);
    out.second = static_cast<unsigned>(second);
    out.millisecond = static_cast<unsigned>(ms);
    return out;
}

UtcMillis millisFromCivil(const CivilDateTime& dt) noexcept {
    const std::int64_t days = daysFromCivil(dt.year, dt.month, dt.day);
    return days * 86'400'000 + dt.hour * 3'600'000 + dt.minute * 60'000 +
           dt.second * 1'000 + dt.millisecond;
}

std::string toIso8601(UtcMillis t) {
    const CivilDateTime dt = civilFromMillis(t);
    std::string out;
    out.resize(24);
    std::snprintf(out.data(), out.size() + 1, "%04d-%02u-%02uT%02u:%02u:%02u.%03uZ",
                  dt.year, dt.month, dt.day, dt.hour, dt.minute, dt.second, dt.millisecond);
    return out;
}

namespace {

[[nodiscard]] bool consumeDigits(std::string_view& s, int count, int& out) {
    if (static_cast<int>(s.size()) < count) return false;
    int v = 0;
    for (int i = 0; i < count; ++i) {
        if (s[i] < '0' || s[i] > '9') return false;
        v = v * 10 + (s[i] - '0');
    }
    s.remove_prefix(static_cast<std::size_t>(count));
    out = v;
    return true;
}

[[nodiscard]] bool consume(std::string_view& s, char expected) {
    if (s.empty() || s.front() != expected) return false;
    s.remove_prefix(1);
    return true;
}

} // namespace

std::optional<UtcMillis> parseIso8601(std::string_view text) {
    std::string_view s = text;
    int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;

    if (!consumeDigits(s, 4, year)) return std::nullopt;
    if (!consume(s, '-')) return std::nullopt;
    if (!consumeDigits(s, 2, month)) return std::nullopt;
    if (!consume(s, '-')) return std::nullopt;
    if (!consumeDigits(s, 2, day)) return std::nullopt;
    if (!consume(s, 'T') && !consume(s, 't')) return std::nullopt;
    if (!consumeDigits(s, 2, hour)) return std::nullopt;
    if (!consume(s, ':')) return std::nullopt;
    if (!consumeDigits(s, 2, minute)) return std::nullopt;
    if (!consume(s, ':')) return std::nullopt;
    if (!consumeDigits(s, 2, second)) return std::nullopt;

    int millisecond = 0;
    if (!s.empty() && (s.front() == '.' || s.front() == ',')) {
        s.remove_prefix(1);
        if (s.empty() || s.front() < '0' || s.front() > '9') return std::nullopt;
        int digits = 0;
        int scale = 100;
        while (!s.empty() && s.front() >= '0' && s.front() <= '9') {
            if (digits < 3) {
                millisecond += (s.front() - '0') * scale;
                scale /= 10;
            }
            ++digits;
            s.remove_prefix(1);
        }
    }

    if (s.empty() || (s.front() != 'Z' && s.front() != 'z')) return std::nullopt;
    s.remove_prefix(1);
    if (!s.empty()) return std::nullopt;

    if (year < 1 || month < 1 || month > 12) return std::nullopt;
    if (day < 1 || day > static_cast<int>(daysInMonth(year, static_cast<unsigned>(month))))
        return std::nullopt;
    if (hour > 23 || minute > 59 || second > 59) return std::nullopt;

    CivilDateTime dt;
    dt.year = year;
    dt.month = static_cast<unsigned>(month);
    dt.day = static_cast<unsigned>(day);
    dt.hour = static_cast<unsigned>(hour);
    dt.minute = static_cast<unsigned>(minute);
    dt.second = static_cast<unsigned>(second);
    dt.millisecond = static_cast<unsigned>(millisecond);
    return millisFromCivil(dt);
}

std::int64_t localDayIndex(UtcMillis t, int offsetMinutes) noexcept {
    const std::int64_t shifted = t + static_cast<std::int64_t>(offsetMinutes) * 60'000;
    auto [day, msOfDay] = divMod(shifted, 86'400'000);
    (void)msOfDay;
    return day;
}

UtcMillis localDayStartUtc(std::int64_t localDay, int offsetMinutes) noexcept {
    return localDay * 86'400'000 - static_cast<std::int64_t>(offsetMinutes) * 60'000;
}

} // namespace equora::domain::utc
