#include <gtest/gtest.h>

#include <equora/domain/Task.h>
#include <equora/scheduling/Estimate.h>
#include <equora/scheduling/Planner.h>

namespace {

using equora::domain::Priority;
using equora::domain::Task;
using equora::domain::TaskStatus;
using equora::scheduling::DayLoad;
using equora::scheduling::PlannerInput;
using equora::scheduling::Span;
using equora::scheduling::WorkHours;

constexpr equora::domain::UtcMillis kWeek =
    1'789'689'600'000LL - 4LL * 86'400'000; // 2026-09-14(周一)T00:00Z

Task makeTask(std::string title, std::optional<equora::domain::UtcMillis> due,
              std::optional<std::int32_t> estimate, Priority p = Priority::Normal) {
    Task t = Task::draft(std::move(title));
    t.dueAt = due;
    t.estimateMinutes = estimate;
    t.priority = p;
    return t;
}

PlannerInput baseInput() {
    PlannerInput in;
    in.windowFrom = kWeek;
    in.windowTo = kWeek + 7LL * 86'400'000;
    in.hours = WorkHours{};
    return in;
}

TEST(PlannerTest, FillsTasksIntoFreeWorkSlotsDeterministically) {
    PlannerInput in = baseInput();
    in.tasks = {
        makeTask("大任务", kWeek + 2LL * 86'400'000, 120),
        makeTask("小任务", kWeek + 86'400'000, 30),
    };

    const auto plan = equora::scheduling::planWeek(in);
    // 小任务截止更近 → 先排;两个任务都应得到建议。
    ASSERT_EQ(plan.size(), 2U);
    EXPECT_EQ(plan[0].taskId, in.tasks[1].id);
    // 建议落周一 09:00 起(工作时段第一个空闲)。
    EXPECT_EQ(plan[0].start, kWeek + 9 * 3'600'000);
    EXPECT_EQ((plan[0].end - plan[0].start) / 60'000, 30);
    // 大任务从 09:30 续排 120 分钟。
    EXPECT_EQ(plan[1].start, kWeek + 9 * 3'600'000 + 30 * 60'000);
    EXPECT_EQ((plan[1].end - plan[1].start) / 60'000, 120);

    // 同输入重跑结果一致(确定性)。
    const auto again = equora::scheduling::planWeek(baseInput());
    PlannerInput re = baseInput();
    re.tasks = in.tasks;
    const auto rerun = equora::scheduling::planWeek(re);
    ASSERT_EQ(rerun.size(), plan.size());
    for (std::size_t i = 0; i < plan.size(); ++i) {
        EXPECT_EQ(rerun[i].start, plan[i].start);
    }
}

TEST(PlannerTest, RespectsBusySpansAndMaxBlock) {
    PlannerInput in = baseInput();
    in.tasks = { makeTask("超长", std::nullopt, 300) };
    // 周一 09:00-12:00 已占。
    Span busy{};
    busy.start = kWeek + 9 * 3'600'000;
    busy.end = kWeek + 12 * 3'600'000;
    in.busy = { busy };

    const auto plan = equora::scheduling::planWeek(in);
    ASSERT_FALSE(plan.empty());
    // 第一块从 13:00 开始(避开忙碌),长度 = maxBlock 120。
    EXPECT_EQ(plan[0].start, kWeek + 12 * 3'600'000); // 忙碌 09-12 后从 12:00 续
    EXPECT_EQ((plan[0].end - plan[0].start) / 60'000, in.maxBlockMinutes);
    // 300 分钟 → 至少 3 块。
    EXPECT_GE(plan.size(), 3U);
}

TEST(PlannerTest, DayLoadsFlagOverload) {
    PlannerInput in = baseInput();
    Span a{};
    a.start = kWeek + 9 * 3'600'000;
    a.end = kWeek + 18 * 3'600'000; // 9h ≥ 容量 9h
    Span b{};
    b.start = kWeek + 19 * 3'600'000;
    b.end = kWeek + 20 * 3'600'000; // +1h 超载

    const auto loads = equora::scheduling::dayLoads({ a, b }, in);
    ASSERT_EQ(loads.size(), 1U);
    EXPECT_EQ(loads[0].localDate, "2026-09-14");
    EXPECT_TRUE(loads[0].overloaded());
    EXPECT_EQ(loads[0].plannedMinutes, 600);
}

TEST(PlannerTest, SplitSuggestionForLongTasks) {
    PlannerInput in = baseInput();
    in.tasks = {
        makeTask("巨任务", std::nullopt, 300),
        makeTask("小", std::nullopt, 30),
    };
    const auto splits = equora::scheduling::suggestSplits(in);
    ASSERT_EQ(splits.size(), 1U);
    EXPECT_EQ(splits[0].suggestedBlocks, 3);
    EXPECT_EQ(splits[0].blockMinutes, 100);
}

TEST(EstimateTest, MedianOfEffectiveDurations) {
    // 60/90/120 分钟 → 中位数 90。
    auto c = equora::scheduling::correctEstimate("t1", 30, { 60, 90, 120 });
    EXPECT_TRUE(c.hasSuggestion());
    EXPECT_EQ(c.suggestedMinutes, 90);
    EXPECT_EQ(c.sampleCount, 3);
    EXPECT_EQ(c.originalMinutes, 30); // 原始估计保留

    // 偶数样本取均值;异常样本被过滤;样本不足不产生建议。
    c = equora::scheduling::correctEstimate("t2", 30, { 60, 120 });
    EXPECT_EQ(c.suggestedMinutes, 90);
    c = equora::scheduling::correctEstimate("t3", 30, { 0, 60, 100'000, 90 });
    EXPECT_EQ(c.sampleCount, 2);
    c = equora::scheduling::correctEstimate("t4", 30, { 60 });
    EXPECT_FALSE(c.hasSuggestion());
}

} // namespace
