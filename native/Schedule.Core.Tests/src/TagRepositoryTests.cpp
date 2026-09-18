#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/domain/Error.h>

namespace {

using equora::domain::EquoraError;
using equora::domain::ErrorCode;
using equora::domain::Tag;
using equora::domain::Task;
using equora::testing::CoreDbTest;

TEST_F(CoreDbTest, TagCreateAndUniqueName) {
    const Tag t = tags_->create(Tag::draft("重要"));
    EXPECT_EQ(t.revision, 1);
    EXPECT_FALSE(t.id.empty());

    (void)tags_->create(Tag::draft("IMPORTANT")); // 不同名,合法
    try {
        (void)tags_->create(Tag::draft("重要")); // 完全同名 → Conflict
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }
}

TEST_F(CoreDbTest, TagRenameConflictRejected) {
    (void)tags_->create(Tag::draft("工作"));
    Tag t = tags_->create(Tag::draft("生活"));
    t.name = "工作"; // 撞名
    try {
        (void)tags_->update(t);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }
}

TEST_F(CoreDbTest, TagAssignUnassignAndForTask) {
    const Task t1 = tasks_->create(Task::draft("任务一"));
    const Task t2 = tasks_->create(Task::draft("任务二"));
    const Tag a = tags_->create(Tag::draft("alpha"));
    const Tag b = tags_->create(Tag::draft("beta"));

    tags_->assignToTask(t1.id, a.id);
    tags_->assignToTask(t1.id, b.id);
    tags_->assignToTask(t1.id, a.id); // 幂等
    tags_->assignToTask(t2.id, a.id);

    EXPECT_EQ(tags_->forTask(t1.id).size(), 2U);
    EXPECT_EQ(tags_->forTask(t2.id).size(), 1U);
    EXPECT_EQ(tags_->forTask(t1.id)[0].name, "alpha"); // 名称序

    tags_->unassignFromTask(t1.id, b.id);
    tags_->unassignFromTask(t1.id, b.id); // 幂等
    EXPECT_EQ(tags_->forTask(t1.id).size(), 1U);
}

TEST_F(CoreDbTest, TagAssignValidatesExistence) {
    const Task t = tasks_->create(Task::draft("正常任务"));
    const Tag tag = tags_->create(Tag::draft("x"));

    try {
        tags_->assignToTask("no-such-task", tag.id);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::NotFound);
    }
    try {
        tags_->assignToTask(t.id, "no-such-tag");
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::NotFound);
    }
}

TEST_F(CoreDbTest, DeletedTagHiddenFromTaskButNameStillOccupied) {
    const Task t = tasks_->create(Task::draft("任务"));
    const Tag tag = tags_->create(Tag::draft("稍后读"));
    tags_->assignToTask(t.id, tag.id);
    (void)tags_->setDeleted(tag.id, true);

    EXPECT_TRUE(tags_->forTask(t.id).empty()); // 查询侧过滤
    try {
        (void)tags_->create(Tag::draft("稍后读")); // 墓碑占用名称
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }

    (void)tags_->setDeleted(tag.id, false); // 恢复后关系重新可见
    EXPECT_EQ(tags_->forTask(t.id).size(), 1U);
}

} // namespace
