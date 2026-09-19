#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/core/ReviewRepository.h>
#include <equora/scheduling/Recurrence.h>

namespace {

using equora::core::AutomationRule;
using equora::core::ReviewRepository;
using equora::domain::Task;
using equora::domain::TaskStatus;
using equora::testing::CoreDbTest;

// 用真实"今天"的 UTC 日界:任务 updated_at(=now)才能落入统计窗口。
const equora::domain::UtcMillis kDay =
    (equora::domain::utc::now() / 86'400'000) * 86'400'000;
const equora::domain::UtcMillis kWeek = kDay - 4LL * 86'400'000; // 周一

class ReviewTest : public CoreDbTest {
protected:
    void SetUp() override {
        CoreDbTest::SetUp();
        reviews_ = std::make_unique<ReviewRepository>(db_, "device-test");
        automation_ = std::make_unique<equora::core::AutomationRepository>(db_, "device-test");
        calendar_ = std::make_unique<equora::core::CalendarRepository>(db_, "device-test");
        focus_ = std::make_unique<equora::core::FocusRepository>(db_, "device-test");
    }
    std::unique_ptr<ReviewRepository> reviews_;
    std::unique_ptr<equora::core::AutomationRepository> automation_;
    std::unique_ptr<equora::core::CalendarRepository> calendar_;
    std::unique_ptr<equora::core::FocusRepository> focus_;
};

TEST_F(ReviewTest, DailyComputesFromTasksBlocksAndSessions) {
    // 完成 1 + 延期 1。
    Task done = Task::draft("完成了");
    done.status = TaskStatus::Done;
    (void)tasks_->create(done);
    Task overdue = Task::draft("过期未完");
    overdue.dueAt = kDay - 86'400'000;
    (void)tasks_->create(overdue);

    // 当日块(计划 60 分钟)。
    equora::domain::TimeBlock b;
    b.startAt = kDay + 9 * 3'600'000;
    b.endAt = b.startAt + 60 * 60'000;
    (void)calendar_->createBlock(b);

    // 当日专注会话(有效 45 分钟,2 次中断)。
    auto s = focus_->start(equora::domain::FocusMode::Pomodoro, 25, "",
                           std::nullopt, std::nullopt, kDay + 10 * 3'600'000);
    (void)focus_->addInterruption(s.id, kDay + 10 * 3'600'000 + 60'000, 30'000, "a", "manual", "");
    (void)focus_->addInterruption(s.id, kDay + 10 * 3'600'000 + 120'000, 30'000, "b", "manual", "");
    (void)focus_->complete(s.id, kDay + 10 * 3'600'000 + 45 * 60'000, "", -1);

    const auto data = reviews_->computeDaily(kDay, *tasks_, *calendar_, *focus_, 0);
    EXPECT_EQ(data.completedCount, 1);
    EXPECT_EQ(data.deferredCount, 1);
    EXPECT_EQ(data.plannedMinutes, 60);
    EXPECT_EQ(data.actualMinutes, 45); // 会话有效 45(时间块 actual 为 0)
    EXPECT_EQ(data.distractionCount, 2);

    // 存档与重载;覆盖更新不重复。
    reviews_->saveDaily(data);
    reviews_->saveDaily(data);
    const auto loaded = reviews_->loadDaily(data.localDate);
    ASSERT_TRUE(loaded.has_value());
    EXPECT_EQ(loaded->completedCount, 1);
    EXPECT_EQ(reviews_->recentDaily(10).size(), 1U);
}

TEST_F(ReviewTest, WeeklyComputesFocusRatioAndBestHour) {
    // 2 完成 + 1 放弃 → ratio = 2/3。
    for (int i = 0; i < 2; ++i) {
        auto s = focus_->start(equora::domain::FocusMode::Deep, 60, "", std::nullopt,
                               std::nullopt, kWeek + 9 * 3'600'000 + i * 3'600'000);
        (void)focus_->complete(s.id, kWeek + 11 * 3'600'000 + i * 3'600'000, "", -1);
    }
    auto ab = focus_->start(equora::domain::FocusMode::Pomodoro, 25, "", std::nullopt,
                            std::nullopt, kWeek + 86'400'000 + 15 * 3'600'000);
    (void)focus_->abandon(ab.id, kWeek + 86'400'000 + 16 * 3'600'000);

    const auto w = reviews_->computeWeekly(kWeek, *tasks_, *calendar_, *focus_, 0);
    EXPECT_NEAR(w.focusRatio, 2.0 / 3.0, 1e-9);
    EXPECT_EQ(w.bestFocusHour, 9); // 两个 9 点完成的会话
    const std::string expectedWeek =
        equora::scheduling::localDateString(kWeek, 0);
    EXPECT_EQ(w.weekStartDate, expectedWeek);

    reviews_->saveWeekly(w);
    const auto loaded = reviews_->loadWeekly(expectedWeek);
    ASSERT_TRUE(loaded.has_value());
    EXPECT_DOUBLE_EQ(loaded->focusRatio, w.focusRatio);
}

TEST_F(ReviewTest, AutomationRuleCrudEvaluateAndLog) {
    AutomationRule rule;
    rule.name = "大任务建议拆分";
    rule.trigger = R"({"type":"task_created"})";
    rule.conditions = R"({"estimate_gte":120})";
    rule.actions = R"([{"type":"suggest_split"}])";
    const auto created = automation_->create(rule);
    EXPECT_EQ(created.revision, 1);

    // 条件不满足(估时 30)。
    Task small = Task::draft("小");
    small.estimateMinutes = 30;
    const auto t1 = tasks_->create(small);
    EXPECT_TRUE(automation_->evaluate("task_created", t1).empty());

    // 条件满足(估时 180)。
    Task big = Task::draft("大");
    big.estimateMinutes = 180;
    const auto t2 = tasks_->create(big);
    const auto hits = automation_->evaluate("task_created", t2);
    ASSERT_EQ(hits.size(), 1U);
    EXPECT_EQ(hits[0], created.id);

    // 触发类型不匹配。
    EXPECT_TRUE(automation_->evaluate("completed", t2).empty());

    // 禁用后不再命中;日志可查。
    automation_->setEnabled(created.id, false);
    EXPECT_TRUE(automation_->evaluate("task_created", t2).empty());

    automation_->log(created.id, t2.id, true, "");
    automation_->log(created.id, t2.id, false, "演示错误");
    const auto logs = automation_->recentLogs();
    ASSERT_EQ(logs.size(), 2U);
    EXPECT_EQ(logs[0].error, "演示错误");
}

} // namespace
