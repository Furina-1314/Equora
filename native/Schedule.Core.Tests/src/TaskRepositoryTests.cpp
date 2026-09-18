#include <equora/core/TaskRepository.h>

#include <gtest/gtest.h>

#include <algorithm>
#include <memory>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>
#include <equora/storage/Migrations.h>

namespace {

using equora::domain::ErrorCode;
using equora::domain::EquoraError;
using equora::domain::Task;
using equora::domain::TaskStatus;
using equora::core::TaskRepository;
using equora::storage::Database;
using equora::storage::applyMigrations;

class TaskRepositoryTest : public ::testing::Test {
protected:
    void SetUp() override {
        db_ = Database::open(":memory:");
        applyMigrations(db_);
        repo_ = std::make_unique<TaskRepository>(db_, "device-A");
    }

    Database db_;
    std::unique_ptr<TaskRepository> repo_;
};

TEST_F(TaskRepositoryTest, CreateFillsSyncFields) {
    const Task created = repo_->create(Task::draft("写周报"));
    ASSERT_FALSE(created.id.empty());
    EXPECT_TRUE(equora::domain::Uuid::parse(created.id).has_value());
    EXPECT_EQ(created.revision, 1);
    EXPECT_GT(created.createdAt, 0);
    EXPECT_EQ(created.updatedAt, created.createdAt);
    EXPECT_FALSE(created.deletedAt.has_value());
    EXPECT_EQ(created.lastDeviceId, "device-A");
    EXPECT_EQ(created.status, TaskStatus::Inbox);
}

TEST_F(TaskRepositoryTest, CreateRejectsBlankTitle) {
    EXPECT_THROW(repo_->create(Task::draft("   ")), EquoraError);
    EXPECT_THROW(repo_->create(Task::draft("\t\n")), EquoraError);
    try {
        (void)repo_->create(Task::draft(""));
        FAIL() << "expected InvalidArgument";
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::InvalidArgument);
    }
}

TEST_F(TaskRepositoryTest, CreateRejectsInvalidExplicitId) {
    Task t = Task::draft("x");
    t.id = "not-a-uuid";
    EXPECT_THROW(repo_->create(t), EquoraError);
}

TEST_F(TaskRepositoryTest, FindByIdRoundtrip) {
    const Task created = repo_->create(Task::draft("复习电路"));
    const auto found = repo_->findById(created.id);
    ASSERT_TRUE(found.has_value());
    EXPECT_EQ(found->id, created.id);
    EXPECT_EQ(found->title, "复习电路");
    EXPECT_EQ(found->revision, created.revision);

    EXPECT_FALSE(repo_->findById(equora::domain::Uuid::nil().toString()).has_value());
}

TEST_F(TaskRepositoryTest, UpdateBumpsRevisionAndFields) {
    Task t = repo_->create(Task::draft("设计稿"));
    t.title = "完成设计稿初版";
    t.note = "含三屏高保真";
    t.priority = equora::domain::Priority::High;
    t.estimateMinutes = 90;

    const Task updated = repo_->update(t);
    EXPECT_EQ(updated.revision, t.revision + 1);
    EXPECT_GE(updated.updatedAt, t.updatedAt);

    const auto read = repo_->findById(t.id);
    ASSERT_TRUE(read.has_value());
    EXPECT_EQ(read->title, "完成设计稿初版");
    EXPECT_EQ(read->note, "含三屏高保真");
    EXPECT_EQ(read->priority, equora::domain::Priority::High);
    EXPECT_EQ(read->estimateMinutes, 90);
}

TEST_F(TaskRepositoryTest, UpdateWithStaleRevisionConflicts) {
    Task t = repo_->create(Task::draft("旧副本"));
    Task stale = t;

    t.title = "新副本";
    (void)repo_->update(t); // revision 1 -> 2

    try {
        (void)repo_->update(stale); // 仍是 revision 1
        FAIL() << "expected Conflict";
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }
}

TEST_F(TaskRepositoryTest, UpdateMissingTaskFails) {
    Task t = Task::draft("幽灵");
    t.id = equora::domain::Uuid::random().toString();
    EXPECT_THROW((void)repo_->update(t), EquoraError);
}

TEST_F(TaskRepositoryTest, SoftDeleteAndRestore) {
    const Task t = repo_->create(Task::draft("稍后处理"));

    const Task deleted = repo_->setDeleted(t.id, true);
    EXPECT_TRUE(deleted.deletedAt.has_value());
    EXPECT_EQ(deleted.revision, t.revision + 1);

    EXPECT_FALSE(repo_->findById(t.id).has_value());                 // 默认排除
    ASSERT_TRUE(repo_->findById(t.id, /*includeDeleted=*/true).has_value());
    EXPECT_EQ(repo_->listAll().size(), 0U);
    EXPECT_EQ(repo_->listAll(/*includeDeleted=*/true).size(), 1U);    // 墓碑仍在

    const Task restored = repo_->setDeleted(t.id, false);
    EXPECT_FALSE(restored.deletedAt.has_value());
    EXPECT_EQ(restored.revision, deleted.revision + 1);
    EXPECT_TRUE(repo_->findById(t.id).has_value());
}

TEST_F(TaskRepositoryTest, SetDeletedIsIdempotent) {
    const Task t = repo_->create(Task::draft("幂等删除"));
    const Task first = repo_->setDeleted(t.id, true);
    const Task second = repo_->setDeleted(t.id, true); // 不再推进 revision
    EXPECT_EQ(second.revision, first.revision);
}

TEST_F(TaskRepositoryTest, ListAllOrdersByCreation) {
    for (int i = 0; i < 5; ++i) {
        (void)repo_->create(Task::draft("任务 " + std::to_string(i)));
    }
    const auto all = repo_->listAll();
    ASSERT_EQ(all.size(), 5U);
    bool ascending = std::is_sorted(all.begin(), all.end(),
                                    [](const Task& a, const Task& b) {
                                        return a.createdAt < b.createdAt;
                                    });
    EXPECT_TRUE(ascending);
}

TEST_F(TaskRepositoryTest, MultiValueFieldsRoundtrip) {
    Task t = Task::draft("带可空字段");
    t.dueAt = 1'800'000'000'000;
    t.estimateMinutes = 45;
    t.importance = 4;
    t.projectId = "proj-uuid";
    t = repo_->create(t); // create 按值返回补全后的实体(id 在其中)

    const auto read = repo_->findById(t.id);
    ASSERT_TRUE(read.has_value());
    EXPECT_EQ(read->dueAt, t.dueAt);
    EXPECT_EQ(read->estimateMinutes, 45);
    EXPECT_EQ(read->importance, 4);
    EXPECT_EQ(read->projectId, "proj-uuid");

    // 清空可空字段。
    Task cleared = *read;
    cleared.dueAt = std::nullopt;
    cleared.projectId = std::nullopt;
    cleared.estimateMinutes = std::nullopt;
    (void)repo_->update(cleared);
    const auto reread = repo_->findById(t.id);
    ASSERT_TRUE(reread.has_value());
    EXPECT_FALSE(reread->dueAt.has_value());
    EXPECT_FALSE(reread->projectId.has_value());
    EXPECT_FALSE(reread->estimateMinutes.has_value());
}

} // namespace
