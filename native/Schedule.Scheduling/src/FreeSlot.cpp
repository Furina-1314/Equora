#include <equora/scheduling/FreeSlot.h>

#include <algorithm>

#include <equora/domain/Time.h>

namespace equora::scheduling {

using domain::utc::localDayIndex;
using domain::utc::localDayStartUtc;

namespace {
constexpr std::int64_t kDayMs = 86'400'000;

int weekdayOfLocalDay(std::int64_t localDay) noexcept {
    return static_cast<int>((localDay + 3) % 7);
}
} // namespace

std::vector<FreeSlot> findFreeSlots(const std::vector<Span>& busy, domain::UtcMillis from,
                                    domain::UtcMillis to, const WorkHours& hours,
                                    std::int64_t minMinutes, std::size_t limit) {
    std::vector<FreeSlot> out;
    if (to <= from || minMinutes <= 0 || hours.endMinute <= hours.startMinute) return out;

    // 覆盖检查辅助:区间与 [a,b) 的交叠。
    auto clipped = [&](const Span& s, std::int64_t a, std::int64_t b) {
        return std::make_pair(std::max<std::int64_t>(s.start, a),
                              std::min<std::int64_t>(s.end, b));
    };

    for (std::int64_t day = localDayIndex(from, 0);; ++day) {
        const std::int64_t dayStart = localDayStartUtc(day, 0);
        if (dayStart >= to) break;

        if (std::find(hours.workdays.begin(), hours.workdays.end(),
                      weekdayOfLocalDay(day)) == hours.workdays.end()) {
            continue;
        }

        std::int64_t windowStart = dayStart + hours.startMinute * 60'000;
        std::int64_t windowEnd = dayStart + hours.endMinute * 60'000;
        if (windowStart < from) windowStart = from;
        if (windowEnd > to) windowEnd = to;
        if (windowEnd <= windowStart) continue;

        // 收集当日交叠的忙碌区间并排序。
        std::vector<std::pair<std::int64_t, std::int64_t>> overlaps;
        for (const auto& s : busy) {
            if (s.end <= windowStart || s.start >= windowEnd) continue;
            overlaps.push_back(clipped(s, windowStart, windowEnd));
        }
        std::sort(overlaps.begin(), overlaps.end());

        std::int64_t cursor = windowStart;
        for (const auto& [bs, be] : overlaps) {
            if (bs > cursor && (bs - cursor) / 60'000 >= minMinutes) {
                out.push_back(FreeSlot{cursor, bs});
                if (limit > 0 && out.size() >= limit) return out;
            }
            cursor = std::max(cursor, be);
        }
        if (windowEnd > cursor && (windowEnd - cursor) / 60'000 >= minMinutes) {
            out.push_back(FreeSlot{cursor, windowEnd});
            if (limit > 0 && out.size() >= limit) return out;
        }
    }
    return out;
}

} // namespace equora::scheduling
