#include <equora/domain/Time.h>

#include <gtest/gtest.h>

#include <random>
#include <utility>
#include <vector>

namespace {

using equora::domain::CivilDateTime;
using equora::domain::UtcMillis;
namespace utc = equora::domain::utc;

TEST(TimeTest, EpochFormatsAsIso) {
    EXPECT_EQ(utc::toIso8601(0), "1970-01-01T00:00:00.000Z");
}

TEST(TimeTest, KnownValuesRoundtrip) {
    const char* samples[] = {
        "1970-01-01T00:00:00.000Z",
        "1969-12-31T23:59:59.999Z",  // 纪元前 1 毫秒
        "2024-02-29T12:00:00.500Z",  // 闰日
        "2000-02-29T00:00:00.000Z",  // 400 年闰
        "1900-03-01T00:00:00.000Z",  // 100 年非闰次日
        "2026-09-18T08:30:05.123Z",
        "9999-12-31T23:59:59.999Z",
    };
    for (const char* s : samples) {
        const auto parsed = utc::parseIso8601(s);
        ASSERT_TRUE(parsed.has_value()) << s;
        EXPECT_EQ(utc::toIso8601(*parsed), s);
    }
}

TEST(TimeTest, CivilMillisAreSymmetric) {
    std::mt19937_64 rng(20260918);
    std::uniform_int_distribution<std::int64_t> dist(-62'167'219'200'000LL, // 0001-01-01
                                                     253'402'300'799'999LL); // 9999-12-31T23:59:59.999
    for (int i = 0; i < 50'000; ++i) {
        const UtcMillis t = dist(rng);
        const CivilDateTime dt = utc::civilFromMillis(t);
        EXPECT_EQ(utc::millisFromCivil(dt), t) << utc::toIso8601(t);
    }
}

TEST(TimeTest, ParseAcceptsFractionVariants) {
    EXPECT_EQ(utc::parseIso8601("2026-09-18T08:30:05Z"),
              utc::parseIso8601("2026-09-18T08:30:05.000Z"));
    EXPECT_EQ(utc::parseIso8601("2026-09-18T08:30:05.5Z"),
              utc::parseIso8601("2026-09-18T08:30:05.500Z"));
    EXPECT_EQ(utc::parseIso8601("2026-09-18t08:30:05z"),
              utc::parseIso8601("2026-09-18T08:30:05.000Z"));
    // 第 4 位及以后的小数被截断到毫秒精度。
    EXPECT_EQ(utc::parseIso8601("2026-09-18T08:30:05.1234567Z"),
              utc::parseIso8601("2026-09-18T08:30:05.123Z"));
}

TEST(TimeTest, ParseRejectsInvalid) {
    const char* invalid[] = {
        "",
        "2026-09-18",                      // 缺时间
        "2026-09-18 08:30:05Z",            // 空格分隔
        "2026-9-18T08:30:05Z",             // 未补零
        "2026-13-01T00:00:00Z",            // 月越界
        "2026-00-01T00:00:00Z",
        "2026-09-31T00:00:00Z",            // 日越界(9 月 30 天)
        "2024-02-30T00:00:00Z",            // 闰年 2 月
        "2023-02-29T00:00:00Z",            // 非闰年 2 月
        "2026-09-18T24:00:00Z",            // 时越界
        "2026-09-18T23:60:00Z",
        "2026-09-18T23:59:60Z",
        "2026-09-18T08:30:05",             // 缺 Z
        "2026-09-18T08:30:05+01:00",       // 偏移量暂不支持
        "2026-09-18T08:30:05.Z",           // 空小数
        "0000-01-01T00:00:00Z",            // 年 0
        "2026-09-18T08:30:05Z ",           // 尾部空白
    };
    for (const char* s : invalid) {
        EXPECT_FALSE(utc::parseIso8601(s).has_value()) << s;
    }
}

TEST(TimeTest, LeapYearRules) {
    EXPECT_TRUE(utc::isLeapYear(2024));
    EXPECT_TRUE(utc::isLeapYear(2000));
    EXPECT_FALSE(utc::isLeapYear(1900));
    EXPECT_FALSE(utc::isLeapYear(2023));

    EXPECT_EQ(utc::daysInMonth(2024, 2), 29U);
    EXPECT_EQ(utc::daysInMonth(2023, 2), 28U);
    EXPECT_EQ(utc::daysInMonth(2026, 9), 30U);
    EXPECT_EQ(utc::daysInMonth(2026, 12), 31U);
    EXPECT_EQ(utc::daysInMonth(2026, 0), 0U); // 越界月
}

TEST(TimeTest, CivilDayConversions) {
    EXPECT_EQ(utc::daysFromCivil(1970, 1, 1), 0);
    EXPECT_EQ(utc::daysFromCivil(1970, 1, 2), 1);
    EXPECT_EQ(utc::daysFromCivil(1969, 12, 31), -1);
    EXPECT_EQ(utc::civilFromDays(0).year, 1970);
    EXPECT_EQ(utc::civilFromDays(0).month, 1U);
    EXPECT_EQ(utc::civilFromDays(0).day, 1U);

    // 对称性抽查:含闰年边界。
    for (std::int64_t d = -800; d <= 800; ++d) {
        const auto date = utc::civilFromDays(d);
        EXPECT_EQ(utc::daysFromCivil(date.year, date.month, date.day), d);
    }
}

TEST(TimeTest, NowIsPlausible) {
    // 2026 年前后 ±10 年窗口内的合理性检查(防止实现错用单位)。
    const UtcMillis t = utc::now();
    EXPECT_GT(t, 1'600'000'000'000); // 2020-09 后
    EXPECT_LT(t, 2'100'000'000'000); // 2036-08 前
}

TEST(TimeTest, LocalDayIndexAndStartRoundtrip) {
    // 东八区:UTC 2026-09-18T04:00 属于当地 9 月 18 日。
    EXPECT_EQ(utc::localDayIndex(1'789'790'400'000, 480), utc::localDayIndex(1'789'790'400'000, 0));
    // 当地午夜边界:UTC 前一天 16:00 是当地当天 00:00。
    const auto day = utc::localDayIndex(1'789'790'400'000, 480);
    EXPECT_EQ(utc::localDayStartUtc(day, 480), 1'789'790'400'000 - 12 * 3'600'000);
    EXPECT_EQ(utc::toIso8601(utc::localDayStartUtc(day, 480)), "2026-09-18T16:00:00.000Z");

    // 负偏移(西五区 UTC-300):2026-09-18T02:00Z 是当地 9 月 17 日 21:00。
    EXPECT_EQ(utc::localDayIndex(1'789'783'200'000, -300),
              utc::localDayIndex(1'789'790'400'000, 480) - 1);

    // 对称性:日界 <= t < 日界+24h。
    for (const auto& [t, off] : std::vector<std::pair<UtcMillis, int>>{
             {1'789'790'400'000, 480}, {0, 0}, {0, 480}, {-1, -300}, {1'789'790'399'999, 480}}) {
        const auto idx = utc::localDayIndex(t, off);
        const auto start = utc::localDayStartUtc(idx, off);
        EXPECT_LE(start, t);
        EXPECT_LT(t, start + 86'400'000);
    }
}

} // namespace
