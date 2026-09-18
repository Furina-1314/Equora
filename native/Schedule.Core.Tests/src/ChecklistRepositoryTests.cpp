#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/domain/Error.h>

namespace {

using equora::domain::ChecklistItem;
using equora::domain::EquoraError;
using equora::domain::ErrorCode;
using equora::domain::Task;
using equora::testing::CoreDbTest;

TEST_F(CoreDbTest, ChecklistAddAppendsOrder) {
    const Task t = tasks_->create(Task::draft("发布清单"));
    const auto a = checklist_->add(t.id, "写发布说明");
    const auto b = checklist_->add(t.id, "打标签");
    const auto c = checklist_->add(t.id, "通知用户");

    EXPECT_EQ(a.sortOrder, 1);
    EXPECT_EQ(b.sortOrder, 2);
    EXPECT_EQ(c.sortOrder, 3);

    const auto items = checklist_->listForTask(t.id);
    ASSERT_EQ(items.size(), 3U);
    EXPECT_EQ(items[2].content, "通知用户");
}

TEST_F(CoreDbTest, ChecklistAddValidatesTask) {
    try {
        (void)checklist_->add("missing-task", "条目");
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::NotFound);
    }
}

TEST_F(CoreDbTest, ChecklistToggleAndUpdate) {
    const Task t = tasks_->create(Task::draft("复习"));
    auto item = checklist_->add(t.id, "第三章习题");
    item.isChecked = true;
    const auto updated = checklist_->update(item);
    EXPECT_TRUE(updated.isChecked);
    EXPECT_EQ(updated.revision, 2);
    EXPECT_TRUE(checklist_->findById(item.id)->isChecked);
}

TEST_F(CoreDbTest, ChecklistRemoveIsSoft) {
    const Task t = tasks_->create(Task::draft("复习"));
    const auto item = checklist_->add(t.id, "划掉我");
    checklist_->remove(item.id);
    checklist_->remove(item.id); // 幂等

    EXPECT_TRUE(checklist_->listForTask(t.id).empty());
    EXPECT_FALSE(checklist_->findById(item.id).has_value());
}

TEST_F(CoreDbTest, ChecklistReorder) {
    const Task t = tasks_->create(Task::draft("流程"));
    const auto a = checklist_->add(t.id, "一");
    const auto b = checklist_->add(t.id, "二");
    const auto c = checklist_->add(t.id, "三");

    checklist_->reorder(t.id, {c.id, a.id, b.id});
    auto items = checklist_->listForTask(t.id);
    ASSERT_EQ(items.size(), 3U);
    EXPECT_EQ(items[0].content, "三");
    EXPECT_EQ(items[1].content, "一");
    EXPECT_EQ(items[2].content, "二");

    // 集合不一致 → 拒绝。
    try {
        checklist_->reorder(t.id, {a.id, b.id});
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::InvalidArgument);
    }
}

} // namespace
