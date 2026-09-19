#include <gtest/gtest.h>

#include <equora/domain/RecurrenceRule.h>
#include <equora/scheduling/Conflict.h>
#include <equora/scheduling/FreeSlot.h>
#include <equora/scheduling/Recurrence.h>

namespace {

using equora::domain::MonthMode;
using equora::domain::RecurFreq;
using equora::domain::RecurrenceRule;
using equora::domain::Weekday;
using equora::scheduling::Instance;
using equora::scheduling::localDateString;
using equora::scheduling::nthWeekdayOfMonth;

// 2026-09-19T02:00:00Z(东八区周六 10:00)。
constexpr equora::domain::UtcMillis kSeed = 1'789'826'400'000;
constexpr equora::domain::UtcMillis kSeedEnd = kSeed + 3'600'000;
constexpr int kTz8 = 480;

TEST(RecurrenceTest, DailyEveryDayInWindow) {
    RecurrenceRule r;
    r.freq = RecurFreq::Daily;
    r.hostType = "event";
    r.hostId = "e1";

    const auto instances = equora::scheduling::expandRecurrence(
        r, kSeed, kSeedEnd, kSeed, kSeed + 6LL * 86'400'000, kTz8);
    ASSERT_EQ(instances.size(), 6U); // 种子 + 5 天
    EXPECT_EQ(instances[0].startUtc, kSeed);
    EXPECT_EQ(instances[3].startUtc, kSeed + 3 * 86'400'000);
}

TEST(RecurrenceTest, DailyIntervalAndUntilAndCount) {
    RecurrenceRule r;
    r.freq = RecurFreq::Daily;
    r.interval = 2;
    r.untilUtc = kSeed + 6LL * 86'400'000; // 含第 4 天,不含第 6 天
    auto out = equora::scheduling::expandRecurrence(r, kSeed, kSeedEnd, kSeed,
                                                    kSeed + 20 * 86'400'000, kTz8);
    EXPECT_EQ(out.size(), 3U); // 0/2/4 天

    RecurrenceRule withCount;
    withCount.freq = RecurFreq::Daily;
    withCount.maxCount = 3;
    out = equora::scheduling::expandRecurrence(withCount, kSeed, kSeedEnd, kSeed,
                                               kSeed + 20 * 86'400'000, kTz8);
    EXPECT_EQ(out.size(), 3U);
}

TEST(RecurrenceTest, WeeklyByDayAndExcludedDate) {
    RecurrenceRule r;
    r.freq = RecurFreq::Weekly;
    r.byWeekday = {Weekday::Tue, Weekday::Thu};
    r.hostType = "event";
    r.hostId = "e2";

    // 种子是周六(不属 BYDAY):实例全部来自 BYDAY 集合,本地时间取种子钟点。
    const auto out = equora::scheduling::expandRecurrence(
        r, kSeed, kSeedEnd, kSeed, kSeed + 21LL * 86'400'000, kTz8);
    ASSERT_FALSE(out.empty());
    for (const auto& inst : out) {
        // 验证每个实例是当地周二或周四的 10:00。
        const auto d = equora::domain::utc::civilFromMillis(
            equora::domain::utc::localDayIndex(inst.startUtc, kTz8) * 0 + inst.startUtc);
        (void)d;
        const std::int64_t localDay = equora::domain::utc::localDayIndex(inst.startUtc, kTz8);
        const int weekday = static_cast<int>((localDay + 3) % 7);
        EXPECT_TRUE(weekday == 1 || weekday == 3);
    }
    EXPECT_GE(out.size(), 4U);

    // 排除其中一个本地日期。
    RecurrenceRule withEx = r;
    withEx.excludedDates = {localDateString(out[0].startUtc, kTz8)};
    const auto filtered = equora::scheduling::expandRecurrence(
        withEx, kSeed, kSeedEnd, kSeed, kSeed + 21LL * 86'400'000, kTz8);
    EXPECT_EQ(filtered.size(), out.size() - 1);
    for (const auto& inst : filtered) {
        EXPECT_NE(inst.startUtc, out[0].startUtc);
    }
}

TEST(RecurrenceTest, MonthlyNthWeekdayAndLast) {
    RecurrenceRule r;
    r.freq = RecurFreq::Monthly;
    r.monthMode = MonthMode::NthWeekday;
    r.monthNth = 2;
    r.monthWeekday = Weekday::Wed;

    const auto out = equora::scheduling::expandRecurrence(
        r, kSeed, kSeedEnd, kSeed, kSeed + 90LL * 86'400'000, kTz8);
    ASSERT_GE(out.size(), 2U);
    for (const auto& inst : out) {
        const std::int64_t localDay = equora::domain::utc::localDayIndex(inst.startUtc, kTz8);
        EXPECT_EQ((localDay + 3) % 7, 2); // 周三
        const auto date = equora::domain::utc::civilFromDays(localDay);
        EXPECT_GE(date.day, 8U);
        EXPECT_LE(date.day, 14U); // 第二个周三
    }

    RecurrenceRule last;
    last.freq = RecurFreq::Monthly;
    last.monthMode = MonthMode::LastWeekday;
    last.monthWeekday = Weekday::Fri;
    const auto lastOut = equora::scheduling::expandRecurrence(
        last, kSeed, kSeedEnd, kSeed, kSeed + 62LL * 86'400'000, kTz8);
    ASSERT_GE(lastOut.size(), 1U);
    for (const auto& inst : lastOut) {
        const std::int64_t localDay = equora::domain::utc::localDayIndex(inst.startUtc, kTz8);
        const auto date = equora::domain::utc::civilFromDays(localDay);
        EXPECT_GT(date.day, 21U); // 最后一个周五必在 22 日之后
    }
}

TEST(RecurrenceTest, NthWeekdayHelpers) {
    // 2026-09 的第一个周一是 09-07;最后一个是 09-28。
    const auto first = nthWeekdayOfMonth(2026, 9, Weekday::Mon, 1);
    ASSERT_TRUE(first.has_value());
    auto date = equora::domain::utc::civilFromDays(*first);
    EXPECT_EQ(date.day, 7U);

    const auto last = nthWeekdayOfMonth(2026, 9, Weekday::Mon, -1);
    ASSERT_TRUE(last.has_value());
    date = equora::domain::utc::civilFromDays(*last);
    EXPECT_EQ(date.day, 28U);

    // 第 5 个周三不存在(2026-09 只有 4 个)。
    EXPECT_FALSE(nthWeekdayOfMonth(2026, 10, Weekday::Wed, 5).has_value());
}

TEST(RruleTest, RoundtripAndRejects) {
    RecurrenceRule r;
    r.freq = RecurFreq::Weekly;
    r.interval = 2;
    r.byWeekday = {Weekday::Tue, Weekday::Thu};
    r.maxCount = 10;

    const std::string text = r.toRruleString();
    EXPECT_EQ(text, "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH;COUNT=10");

    std::string why;
    auto parsed = RecurrenceRule::parseRrule(text, &why);
    ASSERT_TRUE(parsed.has_value()) << why;
    EXPECT_EQ(parsed->freq, RecurFreq::Weekly);
    EXPECT_EQ(parsed->interval, 2);
    ASSERT_EQ(parsed->byWeekday.size(), 2U);
    EXPECT_EQ(parsed->maxCount, std::optional<std::int64_t>(10));

    // 月度 -1FR 解析。
    auto monthly = RecurrenceRule::parseRrule("FREQ=MONTHLY;BYDAY=-1FR", &why);
    ASSERT_TRUE(monthly.has_value()) << why;
    EXPECT_EQ(monthly->monthMode, MonthMode::LastWeekday);
    EXPECT_EQ(monthly->monthWeekday, Weekday::Fri);

    EXPECT_FALSE(RecurrenceRule::parseRrule("FREQ=HOURLY", &why).has_value());
    EXPECT_FALSE(RecurrenceRule::parseRrule("INTERVAL=2", &why).has_value()); // 缺 FREQ
    EXPECT_FALSE(RecurrenceRule::parseRrule("FREQ=DAILY;BYMONTH=3", &why).has_value());
}

TEST(ConflictTest, DetectsOverlapPairs) {
    std::vector<equora::scheduling::Span> spans = {
        {"a", "block", "A", "", 0, 10 * 60'000},
        {"b", "block", "B", "", 5 * 60'000, 15 * 60'000},   // 与 a 重叠 5 分钟
        {"c", "event", "C", "", 15 * 60'000, 20 * 60'000}, // 紧邻 b,不重叠(闭开)
    };
    const auto conflicts = equora::scheduling::detectConflicts(spans);
    ASSERT_EQ(conflicts.size(), 1U);
    EXPECT_EQ(conflicts[0].overlapMinutes, 5);
}

TEST(FreeSlotTest, FindsGapsInWorkHours) {
    using equora::scheduling::Span;
    using equora::scheduling::WorkHours;

    const auto day0 = 1'789'689'600'000; // 2026-09-18T00:00:00Z(周五)
    WorkHours hours;                     // 09:00-18:00,周一到周五

    std::vector<Span> busy = {
        {"b1", "block", "", "", day0 + 10 * 3'600'000, day0 + 11 * 3'600'000},
        {"b2", "block", "", "", day0 + 13 * 3'600'000, day0 + 15 * 3'600'000},
    };
    const auto slots = equora::scheduling::findFreeSlots(
        busy, day0, day0 + 86'400'000, hours, /*minMinutes=*/60);
    ASSERT_EQ(slots.size(), 3U); // 09-10、11-13、15-18
    EXPECT_EQ(slots[0].minutes(), 60);
    EXPECT_EQ(slots[1].minutes(), 120);
    EXPECT_EQ(slots[2].minutes(), 180);

    // 最小 90 分钟时只剩两段。
    const auto fewer = equora::scheduling::findFreeSlots(
        busy, day0, day0 + 86'400'000, hours, 90);
    ASSERT_EQ(fewer.size(), 2U);

    // 周六不工作:整窗在周六则无结果。
    const auto saturday = 1'789'862'400'000; // 2026-09-20T00:00:00Z(周六)
    EXPECT_TRUE(equora::scheduling::findFreeSlots(
                    busy, saturday, saturday + 86'400'000, hours, 30)
                    .empty());
}

} // namespace
