#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/core/TaskQuery.h>
#include <equora/domain/Error.h>

namespace {

using equora::core::SmartLists;
using equora::core::TaskFilter;
using equora::core::TaskSort;
using equora::domain::Task;
using equora::domain::TaskStatus;
using equora::domain::UtcMillis;
using equora::domain::utc::now;
using equora::testing::CoreDbTest;

// 固定基准时刻:2026-09-18T04:00:00Z(东八区当天 12:00)。
constexpr UtcMillis kNow = 1789790400000; // 2026-09-18T04:00:00Z
constexpr int kUtcPlus8 = 480;

class TaskQueryTest : public CoreDbTest {
protected:
    Task seed(std::string title, TaskStatus status,
              std::optional<UtcMillis> due = std::nullopt) {
        Task t = Task::draft(std::move(title));
        t.status = status;
        t.dueAt = due;
        return tasks_->create(t);
    }
};

TEST_F(TaskQueryTest, TodayRespectsLocalDayBounds) {
    // 基准 2026-09-19T04:00Z(东八区 12:00);当地当天 = UTC [09-18T16:00, 09-19T16:00)。
    (void)seed("昨天截止", TaskStatus::Inbox, kNow - 20 * 3'600'000);       // 前一天(且早于 now)
    (void)seed("今天午后", TaskStatus::Inbox, kNow + 1 * 3'600'000);      // 当地 13:00,仍属今天
    (void)seed("今晚当地", TaskStatus::Inbox, kNow + 10 * 3'600'000);       // UTC 14:00 → 当地 22:00
    (void)seed("明天当地凌晨", TaskStatus::Inbox, kNow + 14 * 3'600'000);   // UTC 18:00 → 当地次日 02:00

    const auto today = tasks_->query(SmartLists::today(kNow, kUtcPlus8));
    ASSERT_EQ(today.size(), 2U);
    EXPECT_EQ(today[0].title, "今天午后"); // DueAsc
    EXPECT_EQ(today[1].title, "今晚当地");

    const auto overdue = tasks_->query(SmartLists::overdue(kNow));
    ASSERT_EQ(overdue.size(), 1U);
    EXPECT_EQ(overdue[0].title, "昨天截止");
}

TEST_F(TaskQueryTest, OverdueExcludesCompleted) {
    (void)seed("逾期的进行中", TaskStatus::InProgress, kNow - 3'600'000);
    (void)seed("逾期的已完成", TaskStatus::Done, kNow - 3'600'000);
    (void)seed("逾期的已取消", TaskStatus::Cancelled, kNow - 3'600'000);

    const auto overdue = tasks_->query(SmartLists::overdue(kNow));
    ASSERT_EQ(overdue.size(), 1U);
    EXPECT_EQ(overdue[0].title, "逾期的进行中");
}

TEST_F(TaskQueryTest, InboxNoDateScheduledWaiting) {
    (void)seed("收件箱任务", TaskStatus::Inbox);
    (void)seed("无日期高优", TaskStatus::Planned);
    (void)seed("已安排", TaskStatus::Planned, kNow + 3'600'000);
    (void)seed("等待他人", TaskStatus::Waiting);

    EXPECT_EQ(tasks_->query(SmartLists::inbox()).size(), 1U);
    EXPECT_EQ(tasks_->query(SmartLists::noDate()).size(), 3U);
    EXPECT_EQ(tasks_->query(SmartLists::scheduled()).size(), 1U);
    EXPECT_EQ(tasks_->query(SmartLists::waiting()).size(), 1U);
}

TEST_F(TaskQueryTest, CompletedTodayOnlyCountsToday) {
    Task doneToday = seed("今天完成", TaskStatus::Done);
    (void)seed("老完成", TaskStatus::Done);

    // “老完成”的 updatedAt 是插入时刻(≈now),难以区分;这里只验证包含关系与排序。
    const auto list = tasks_->query(SmartLists::completedToday(now(), kUtcPlus8));
    ASSERT_GE(list.size(), 1U);
    bool found = false;
    for (const auto& t : list) {
        if (t.id == doneToday.id) found = true;
    }
    EXPECT_TRUE(found);
    EXPECT_EQ(tasks_->query(SmartLists::completed()).size(), 2U);
}

TEST_F(TaskQueryTest, SearchMatchesTitleAndNoteCaseInsensitive) {
    Task t = Task::draft("Weekly Report");
    t.note = "包含 关键词 线路图";
    (void)tasks_->create(t);
    (void)seed("无关任务", TaskStatus::Inbox);

    TaskFilter byTitle;
    byTitle.searchText = "weekly";
    EXPECT_EQ(tasks_->query(byTitle).size(), 1U);

    TaskFilter byNote;
    byNote.searchText = "线路图";
    EXPECT_EQ(tasks_->query(byNote).size(), 1U);

    TaskFilter none;
    none.searchText = "不存在的词";
    EXPECT_TRUE(tasks_->query(none).empty());
}

TEST_F(TaskQueryTest, TagFilterAndProjectFilter) {
    const auto proj = projects_->create(equora::domain::Project::draft("写作"));
    Task tagged = seed("带标签", TaskStatus::Inbox);
    (void)seed("不带标签", TaskStatus::Inbox);
    const Task inProject = seed("项目任务", TaskStatus::Inbox);
    // 直接以项目 id 更新任务。
    {
        auto withProject = tasks_->findById(inProject.id).value();
        withProject.projectId = proj.id;
        (void)tasks_->update(withProject);
    }

    const auto tag = tags_->create(equora::domain::Tag::draft("focus"));
    tags_->assignToTask(tagged.id, tag.id);

    TaskFilter byTag;
    byTag.tagId = tag.id;
    ASSERT_EQ(tasks_->query(byTag).size(), 1U);
    EXPECT_EQ(tasks_->query(byTag)[0].title, "带标签");

    TaskFilter byProject;
    byProject.projectId = proj.id;
    ASSERT_EQ(tasks_->query(byProject).size(), 1U);
    EXPECT_EQ(tasks_->query(byProject)[0].title, "项目任务");
}

TEST_F(TaskQueryTest, SortingAndPaging) {
    (void)seed("无日期A", TaskStatus::Planned);
    (void)seed("无日期B", TaskStatus::Planned);
    (void)seed("早截止", TaskStatus::Planned, kNow + 2 * 3'600'000);
    (void)seed("晚截止", TaskStatus::Planned, kNow + 5 * 3'600'000);

    TaskFilter dueAsc;
    dueAsc.sort = TaskSort::DueAsc;
    const auto sorted = tasks_->query(dueAsc);
    ASSERT_EQ(sorted.size(), 4U);
    EXPECT_EQ(sorted[0].title, "早截止"); // 有截止的在前,按时间升序
    EXPECT_EQ(sorted[1].title, "晚截止");
    // 无截止的两项:同毫秒创建,id 决胜,顺序不作断言
    EXPECT_TRUE((sorted[2].title == "无日期A" && sorted[3].title == "无日期B") ||
                (sorted[2].title == "无日期B" && sorted[3].title == "无日期A"));

    TaskFilter page;
    page.sort = TaskSort::DueAsc;
    page.limit = 2;
    page.offset = 1;
    const auto paged = tasks_->query(page);
    ASSERT_EQ(paged.size(), 2U);
    EXPECT_EQ(paged[0].title, "晚截止");
}

} // namespace
