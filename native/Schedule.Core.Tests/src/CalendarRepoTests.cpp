#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/core/CalendarRepository.h>
#include <equora/core/TransferIcs.h>
#include <equora/domain/CalendarEvent.h>
#include <equora/domain/Error.h>
#include <equora/scheduling/Conflict.h>

namespace {

using equora::core::CalendarRepository;
using equora::domain::CalendarEvent;
using equora::domain::RecurFreq;
using equora::domain::RecurrenceRule;
using equora::domain::TimeBlock;
using equora::domain::Weekday;
using equora::testing::CoreDbTest;

constexpr equora::domain::UtcMillis kDay = 1'789'689'600'000; // 2026-09-18T00:00Z(周五)

class CalendarRepoTest : public CoreDbTest {
protected:
    void SetUp() override {
        CoreDbTest::SetUp();
        cal_ = std::make_unique<CalendarRepository>(db_, "device-test");
    }
    std::unique_ptr<CalendarRepository> cal_;
};

TEST_F(CalendarRepoTest, BlockCrudAndRangeQuery) {
    TimeBlock b;
    b.startAt = kDay + 9 * 3'600'000;
    b.endAt = kDay + 10 * 3'600'000;
    b.note = "写方案";
    const auto created = cal_->createBlock(b);
    EXPECT_EQ(created.revision, 1);

    auto updated = cal_->findBlock(created.id).value();
    updated.endAt = updated.startAt + 90 * 60'000;
    const auto moved = cal_->update(updated);
    EXPECT_EQ(moved.revision, 2);

    cal_->deleteBlock(created.id);
    EXPECT_FALSE(cal_->findBlock(created.id).has_value());

    // 窗口查询:相交才返回。
    TimeBlock early;
    early.startAt = kDay + 2 * 3'600'000;
    early.endAt = kDay + 3 * 3'600'000;
    (void)cal_->createBlock(early);

    // 从块的 end 开始查询:闭开区间,不再相交。
    auto inRange = cal_->blocksInRange(early.endAt, early.endAt + 1);
    EXPECT_TRUE(inRange.empty());
    inRange = cal_->blocksInRange(kDay, kDay + 86'400'000);
    EXPECT_EQ(inRange.size(), 1U);
}

TEST_F(CalendarRepoTest, BlockRejectsInvertedRangeAndStaleRevision) {
    TimeBlock bad;
    bad.startAt = 100;
    bad.endAt = 100;
    EXPECT_THROW((void)cal_->createBlock(bad), equora::domain::EquoraError);

    TimeBlock b;
    b.startAt = 0;
    b.endAt = 60'000;
    const auto created = cal_->createBlock(b);
    auto stale = created;
    auto fresh = created;
    (void)cal_->update(fresh);
    try {
        (void)cal_->update(stale);
        FAIL();
    } catch (const equora::domain::EquoraError& e) {
        EXPECT_EQ(e.code(), equora::domain::ErrorCode::Conflict);
    }
}

TEST_F(CalendarRepoTest, EventSeriesMaterializeAndDetach) {
    CalendarEvent ev = CalendarEvent::draft("每周例会");
    ev.startAt = kDay + 2 * 3'600'000; // 周五 10:00Z
    ev.endAt = ev.startAt + 3'600'000;
    const auto created = cal_->createEvent(ev);

    RecurrenceRule rule;
    rule.hostType = "event";
    rule.hostId = created.id;
    rule.freq = RecurFreq::Weekly;
    rule.byWeekday = {Weekday::Fri};
    const auto savedRule = cal_->createRule(rule);

    const auto spans = cal_->materializeWindow(kDay, kDay + 28LL * 86'400'000, 0);
    // 宿主事件本身 + 非种子的 3 个周五实例(种子去重)。
    EXPECT_EQ(spans.size(), 4U);

    // 冲突检测能发现两个重叠的周五实例(构造一个真实块与第 2 周实例重叠)。
    const auto secondFriday = kDay + 7 * 86'400'000 + 2 * 3'600'000 + 30 * 60'000;
    TimeBlock clash;
    clash.startAt = secondFriday;
    clash.endAt = secondFriday + 30 * 60'000;
    (void)cal_->createBlock(clash);
    const auto withClash = cal_->materializeWindow(kDay, kDay + 28LL * 86'400'000, 0);
    EXPECT_FALSE(equora::scheduling::detectConflicts(withClash).empty());

    // 仅修改本次:物化第二周实例并加入例外。
    const auto materialized = cal_->detachOccurrence(
        savedRule, kDay + 7 * 86'400'000 + 2 * 3'600'000, 0);
    EXPECT_NE(materialized.id, created.id);
    const auto afterDetach = cal_->materializeWindow(kDay, kDay + 28LL * 86'400'000, 0);
    // 虚拟实例换成真实事件;窗口内还有冲突测试用的真实块。
    EXPECT_EQ(afterDetach.size(), 5U);

    // 修改本次及以后:旧规则截断 + 新规则,后续实例双份(detach 已推进规则版本,需重取)。
    const auto ruleAfterDetach = cal_->findRule(savedRule.id).value();
    const auto tail = cal_->splitSeries(ruleAfterDetach, kDay + 14LL * 86'400'000 + 2 * 3'600'000);
    EXPECT_FALSE(tail.id.empty());
    const auto afterSplit = cal_->materializeWindow(kDay + 14 * 86'400'000,
                                                    kDay + 28LL * 86'400'000, 0);
    // 第 3、4 周五:旧 tail 规则各 1 + 原系列已截断 → 至少 2 个实例。
    EXPECT_GE(afterSplit.size(), 2U);
}

TEST_F(CalendarRepoTest, IcsRoundtrip) {
    CalendarEvent ev = CalendarEvent::draft("发布评审;上线,清单");
    ev.startAt = kDay + 6 * 3'600'000;
    ev.endAt = ev.startAt + 30 * 60'000;
    ev.location = "会议室 A";
    ev.note = "带\n换行的说明";
    (void)cal_->createEvent(ev);

    const std::string ics = equora::core::exportIcs(*cal_, kDay, kDay + 86'400'000);
    EXPECT_NE(ics.find("BEGIN:VCALENDAR"), std::string::npos);
    EXPECT_NE(ics.find("SUMMARY:发布评审\\;上线\\,清单"), std::string::npos);
    EXPECT_NE(ics.find("DTSTART:20260918T060000Z"), std::string::npos);

    // 导入到新库:事件回来,标题/位置往返一致。
    auto db2 = equora::storage::Database::open(":memory:");
    equora::storage::applyMigrations(db2);
    CalendarRepository cal2(db2, "device-ics");
    const auto result = equora::core::importIcs(cal2, ics);
    EXPECT_EQ(result.imported, 1);
    EXPECT_EQ(result.skipped, 0);

    const auto events = cal2.eventsInRange(kDay, kDay + 86'400'000);
    ASSERT_EQ(events.size(), 1U);
    EXPECT_EQ(events[0].title, "发布评审;上线,清单");
    EXPECT_EQ(events[0].location, "会议室 A");
    EXPECT_EQ(events[0].note, "带\n换行的说明");

    // 幂等 + 非法事件计数。
    const auto again = equora::core::importIcs(cal2, ics);
    EXPECT_EQ(again.imported, 0);
    EXPECT_EQ(again.skipped, 1);

    const std::string bad = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:无时间\r\n"
                            "END:VEVENT\r\nEND:VCALENDAR\r\n";
    const auto badResult = equora::core::importIcs(cal2, bad);
    EXPECT_EQ(badResult.failed, 1);
}

TEST_F(CalendarRepoTest, IcsImportWithRruleCreatesRule) {
    const std::string ics =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:ics-rule-1\r\n"
        "DTSTART:20260918T140000Z\r\nDTEND:20260918T150000Z\r\nSUMMARY:站会\r\n"
        "RRULE:FREQ=DAILY;COUNT=5\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    const auto result = equora::core::importIcs(*cal_, ics);
    ASSERT_EQ(result.imported, 1);

    const auto rules = cal_->rulesForHost("event", "ics-rule-1");
    ASSERT_EQ(rules.size(), 1U);
    EXPECT_EQ(rules[0].freq, RecurFreq::Daily);
    EXPECT_EQ(rules[0].maxCount, std::optional<std::int64_t>(5));
}

TEST(MigrationV3Test, UpgradesV2Database) {
    std::filesystem::create_directories(EQUORA_TEST_TMPDIR);
    const std::string path = std::string(EQUORA_TEST_TMPDIR) + "/v3_upgrade.db";
    std::error_code ec;
    std::filesystem::remove(path, ec);

    {
        auto db = equora::storage::Database::open(path);
        const auto& all = equora::storage::builtInMigrations();
        equora::storage::applyMigrations(
            db, std::vector<equora::storage::Migration>(all.begin(), all.begin() + 2));
        ASSERT_EQ(equora::storage::currentSchemaVersion(db), 2);
        db.exec("INSERT INTO tasks (id, title, created_at, updated_at, revision) "
                "VALUES ('t', 'v2 数据', 1, 1, 1)");
    }
    {
        auto db = equora::storage::Database::open(path);
        equora::storage::applyMigrations(db);
        EXPECT_EQ(equora::storage::currentSchemaVersion(db), 3);

        equora::core::CalendarRepository cal(db, "device-new");
        (void)cal.createCalendar(equora::domain::Calendar::draft("默认"));
        TimeBlock b;
        b.taskId = "t";
        b.startAt = 10;
        b.endAt = 70'000;
        (void)cal.createBlock(b);

        auto st = db.prepare("SELECT COUNT(*) FROM tasks WHERE title = 'v2 数据'");
        st.step();
        EXPECT_EQ(st.columnInt(0), 1); // 旧数据仍在
    }
}

} // namespace
