#include <equora/scheduling/Recurrence.h>

#include <algorithm>
#include <cstdio>

namespace equora::scheduling {

using domain::RecurFreq;
using domain::Weekday;
using domain::utc::civilFromDays;
using domain::utc::daysFromCivil;
using domain::utc::localDayIndex;
using domain::utc::localDayStartUtc;

namespace {

constexpr std::int64_t kDayMs = 86'400'000;

[[nodiscard]] int weekdayOfLocalDay(std::int64_t localDay) noexcept {
    // 1970-01-01 是周四;Weekday 枚举 Mon=0。
    return static_cast<int>((localDay + 3) % 7);
}

[[nodiscard]] std::optional<std::int64_t> dayOfNthWeekday(int year, unsigned month,
                                                          Weekday weekday, int nth) {
    const std::int64_t first = daysFromCivil(year, month, 1);
    const int firstWd = weekdayOfLocalDay(first);
    const int target = static_cast<int>(weekday);
    const int offset = (target - firstWd + 7) % 7;
    const std::int64_t day = first + offset + (static_cast<std::int64_t>(nth) - 1) * 7;
    const auto date = civilFromDays(day);
    if (date.month != month || date.year != year) return std::nullopt; // 越界(如第 5 个周三不存在)
    return day;
}

[[nodiscard]] std::string two(unsigned v) {
    char buf[8];
    std::snprintf(buf, sizeof(buf), "%02u", v);
    return buf;
}

} // namespace

std::string localDateString(domain::UtcMillis t, int tzOffsetMinutes) {
    const auto d = civilFromDays(localDayIndex(t, tzOffsetMinutes));
    char buf[16];
    std::snprintf(buf, sizeof(buf), "%04d-%s-%s", d.year, two(d.month).c_str(),
                  two(d.day).c_str());
    return buf;
}

std::optional<std::int64_t> nthWeekdayOfMonth(int year, unsigned month, Weekday weekday,
                                              int nth) {
    if (nth == -1) { // 最后一个
        const unsigned lastDay = domain::utc::daysInMonth(year, month);
        const std::int64_t last = daysFromCivil(year, month, lastDay);
        const int lastWd = weekdayOfLocalDay(last);
        const int back = (lastWd - static_cast<int>(weekday) + 7) % 7;
        return last - back;
    }
    if (nth < 1 || nth > 5) return std::nullopt;
    return dayOfNthWeekday(year, month, weekday, nth);
}

std::vector<Instance> expandRecurrence(const domain::RecurrenceRule& rule,
                                       domain::UtcMillis seedStart,
                                       domain::UtcMillis seedEnd,
                                       domain::UtcMillis windowFrom,
                                       domain::UtcMillis windowTo,
                                       int tzOffsetMinutes) {
    std::vector<Instance> out;
    if (windowTo <= windowFrom || seedEnd <= seedStart) return out;

    const std::int64_t duration = seedEnd - seedStart;
    const std::int64_t interval = std::max<std::int64_t>(rule.interval, 1);
    // 以「周期索引」枚举:种子为 0,按 freq 逐周期推进,生成该周期内的实例。
    // DAILY/ YEARLY: 每周期一个实例;WEEKLY: 每周期输出 byWeekday 集合。
    auto pushIfInWindow = [&](std::int64_t occurrenceDay, std::int64_t dayOffsetMs,
                              std::int64_t counter) -> bool {
        // 返回 false 表示超过 maxCount,应停止枚举。
        if (rule.maxCount.has_value() && counter > *rule.maxCount) return false;

        // 实例起点 = 种子时刻所在本地日 occurrenceDay 的同钟点。
        const std::int64_t seedDay = localDayIndex(seedStart, tzOffsetMinutes);
        const std::int64_t timeOfDay = seedStart - localDayStartUtc(seedDay, tzOffsetMinutes);
        const std::int64_t start =
            localDayStartUtc(occurrenceDay, tzOffsetMinutes) + timeOfDay + dayOffsetMs;
        const std::int64_t end = start + duration;

        if (rule.untilUtc.has_value() && start >= *rule.untilUtc) return false;

        if (start >= windowFrom && start < windowTo) {
            const std::string localDate = localDateString(start, tzOffsetMinutes);
            const bool excluded = std::find(rule.excludedDates.begin(),
                                            rule.excludedDates.end(),
                                            localDate) != rule.excludedDates.end();
            if (!excluded) {
                out.push_back(Instance{rule.id, rule.hostType, rule.hostId, start, end, false});
            }
        }
        return start < windowTo; // 超窗后停止
    };

    if (rule.freq == RecurFreq::Daily) {
        for (std::int64_t k = 0;; ++k) {
            const std::int64_t day = localDayIndex(seedStart, tzOffsetMinutes) + k * interval;
            if (!pushIfInWindow(day, 0, k + 1)) break;
            if (out.size() > 4096) break;
        }
    } else if (rule.freq == RecurFreq::Weekly) {
        std::vector<int> weekdays;
        if (rule.byWeekday.empty()) {
            weekdays.push_back(weekdayOfLocalDay(localDayIndex(seedStart, tzOffsetMinutes)));
        } else {
            for (const auto w : rule.byWeekday) weekdays.push_back(static_cast<int>(w));
        }
        std::sort(weekdays.begin(), weekdays.end());

        std::int64_t counter = 0;
        for (std::int64_t week = 0;; ++week) {
            // 种子周的周一(本地)。
            const std::int64_t seedDay = localDayIndex(seedStart, tzOffsetMinutes);
            const std::int64_t monday = seedDay - weekdayOfLocalDay(seedDay) + week * interval * 7;
            for (const int wd : weekdays) {
                if (!pushIfInWindow(monday + wd, 0, ++counter)) {
                    if (rule.maxCount.has_value() && counter > *rule.maxCount) return out;
                }
            }
            if (out.size() > 4096) break;
            // 停止条件:本周一已越过窗口尾 + 一周。
            const auto last = out.empty() ? nullptr : &out.back();
            if (localDayStartUtc(monday, tzOffsetMinutes) >
                windowTo + 7 * kDayMs) {
                break;
            }
            (void)last;
            if (rule.maxCount.has_value() && counter >= *rule.maxCount) break;
        }
    } else if (rule.freq == RecurFreq::Monthly) {
        for (std::int64_t k = 0;; ++k) {
            const auto seedDate = civilFromDays(localDayIndex(seedStart, tzOffsetMinutes));
            int year = seedDate.year;
            unsigned month = seedDate.month;
            std::int64_t index = k * interval;
            year += static_cast<int>(index / 12);
            month = static_cast<unsigned>(month + index % 12);
            if (month > 12) { month -= 12; ++year; }

            std::optional<std::int64_t> day;
            if (rule.monthMode == domain::MonthMode::Date) {
                const unsigned dim = domain::utc::daysInMonth(year, month);
                const unsigned dom = std::min(seedDate.day, dim);
                day = daysFromCivil(year, month, dom);
            } else if (rule.monthMode == domain::MonthMode::NthWeekday) {
                day = nthWeekdayOfMonth(year, month, rule.monthWeekday, rule.monthNth);
            } else {
                day = nthWeekdayOfMonth(year, month, rule.monthWeekday, -1);
            }
            if (day.has_value()) {
                if (!pushIfInWindow(*day, 0, k + 1)) break;
            }
            if (out.size() > 4096) break;
        }
    } else { // Yearly
        for (std::int64_t k = 0;; ++k) {
            const auto seedDate = civilFromDays(localDayIndex(seedStart, tzOffsetMinutes));
            const int year = seedDate.year + static_cast<int>(k * interval);
            const unsigned dim = domain::utc::daysInMonth(year, seedDate.month);
            const unsigned dom = std::min(seedDate.day, dim);
            if (!pushIfInWindow(daysFromCivil(year, seedDate.month, dom), 0, k + 1)) break;
            if (out.size() > 4096) break;
        }
    }

    return out;
}

} // namespace equora::scheduling
