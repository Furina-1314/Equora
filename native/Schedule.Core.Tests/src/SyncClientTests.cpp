#include <gtest/gtest.h>

#include <equora/domain/Error.h>
#include <equora/storage/Database.h>
#include <equora/storage/Migrations.h>
#include <equora/syncclient/SyncClient.h>

namespace {

using equora::sync::backoffDelayMs;
using equora::sync::mergeJsonFields;
using equora::sync::OutboxStatus;
using equora::sync::SyncClientRepository;

constexpr equora::domain::UtcMillis T0 = 1'789'689'600'000;

class SyncClientTest : public ::testing::Test {
protected:
    void SetUp() override {
        db_ = equora::storage::Database::open(":memory:");
        equora::storage::applyMigrations(db_);
        repo_ = std::make_unique<SyncClientRepository>(db_);
    }
    equora::storage::Database db_;
    std::unique_ptr<SyncClientRepository> repo_;
};

TEST_F(SyncClientTest, EnqueueDueAndMarkResult) {
    const auto e1 = repo_->enqueue("op-1", "task", "t-1", 0, 0, R"({"v":1})", T0);
    const auto e2 = repo_->enqueue("op-2", "task", "t-2", 3, 1, R"({"v":2})", T0 + 1);
    EXPECT_GT(e1.rowId, 0);
    EXPECT_EQ(repo_->pendingCount(), 2U);

    auto due = repo_->duePending(T0, 10);
    ASSERT_EQ(due.size(), 2U);
    EXPECT_EQ(due[0].operationId, "op-1"); // FIFO
    EXPECT_EQ(due[1].baseRevision, 3);

    // 退避:未到时间的条目不出队。
    repo_->markSending(e1.rowId);
    repo_->markResult(e1.rowId, /*accepted=*/false, T0 + 60'000, 5);
    due = repo_->duePending(T0, 10); // 立刻再取:e1 应仍在退避(60s 后)
    ASSERT_EQ(due.size(), 1U);
    EXPECT_EQ(due[0].operationId, "op-2");

    due = repo_->duePending(T0 + 60'001, 10);
    EXPECT_EQ(due.size(), 2U); // e1 退避到期
    EXPECT_EQ(due[0].attempts, 1);

    // 接受后删除。
    repo_->markSending(e1.rowId);
    repo_->markResult(e1.rowId, true, T0, 5);
    EXPECT_EQ(repo_->pendingCount(), 1U);
}

TEST_F(SyncClientTest, MaxAttemptsMarksFailed) {
    const auto e = repo_->enqueue("op-x", "task", "t", 0, 1, "{}", T0);
    for (int i = 0; i < 3; ++i) {
        repo_->markSending(e.rowId);
        repo_->markResult(e.rowId, false, T0 + 1000 * (i + 1), 3);
    }
    auto due = repo_->duePending(T0 + 10'000'000, 10);
    ASSERT_EQ(due.size(), 1U);
    EXPECT_EQ(due[0].status, OutboxStatus::Failed); // 达上限 → Failed(不再自动重试)
    EXPECT_EQ(due[0].attempts, 3);
}

TEST_F(SyncClientTest, ResetStuckRecoversCrashedSending) {
    const auto e = repo_->enqueue("op-1", "task", "t", 0, 1, "{}", T0);
    repo_->markSending(e.rowId);
    // 模拟崩溃重启。
    repo_->resetStuck(T0 + 1000);
    EXPECT_EQ(repo_->pendingCount(), 1U); // 回到 Pending
}

TEST_F(SyncClientTest, StateRoundtrip) {
    auto s = repo_->loadState();
    EXPECT_EQ(s.cursor, 0);
    EXPECT_TRUE(s.serverUrl.empty());

    repo_->saveServerUrl("https://sync.example.com");
    repo_->saveDeviceId("device-uuid-1");
    repo_->saveCursor(18291, T0);

    s = repo_->loadState();
    EXPECT_EQ(s.cursor, 18291);
    EXPECT_EQ(s.serverUrl, "https://sync.example.com");
    EXPECT_EQ(s.deviceId, "device-uuid-1");
    EXPECT_EQ(s.lastSuccessAtMs, T0);
}

TEST_F(SyncClientTest, ConflictLifecycle) {
    repo_->addConflict("op-1", "task", "t-1", R"({"title":"我的"})",
                       R"({"title":"服务器的"})", 5, false, T0);
    auto open = repo_->openConflicts();
    ASSERT_EQ(open.size(), 1U);
    EXPECT_EQ(open[0].serverRevision, 5);
    EXPECT_FALSE(open[0].serverDeleted);

    repo_->resolveConflict(open[0].rowId, 2); // 采纳服务端
    EXPECT_TRUE(repo_->openConflicts().empty());

    // 重复解决被拒。
    try {
        repo_->resolveConflict(open[0].rowId, 1);
        FAIL();
    } catch (const equora::domain::EquoraError& ex) {
        EXPECT_EQ(ex.code(), equora::domain::ErrorCode::Conflict);
    }
}

TEST(BackoffTest, ExponentialWithJitterAndCap) {
    // 无失败 → 0。
    EXPECT_EQ(backoffDelayMs(0), 0);
    // 第 1 次:1s~2s(抖动)。
    EXPECT_EQ(backoffDelayMs(1, 1000, 300'000, 0.0), 1000);
    EXPECT_EQ(backoffDelayMs(1, 1000, 300'000, 0.5), 1500);
    // 指数:2^4=16s(×抖动)。
    EXPECT_EQ(backoffDelayMs(5, 1000, 300'000, 0.0), 16'000);
    // 封顶 5 分钟。
    EXPECT_EQ(backoffDelayMs(20, 1000, 300'000, 0.9), 300'000);
    // 抖动越界被钳制。
    EXPECT_EQ(backoffDelayMs(1, 1000, 300'000, -1), 1000);
    EXPECT_EQ(backoffDelayMs(1, 1000, 300'000, 2.0), 2000);
}

TEST(MergeTest, FieldLevelMergePrefersOurs) {
    const auto merged = mergeJsonFields(
        R"({"title":"我的标题","note":"新备注","priority":3})",
        R"({"title":"服务器标题","note":"旧备注","done":true})",
        /*preferOurs=*/true);
    ASSERT_TRUE(merged.has_value());
    // 胜方字段全保留,败方独有字段并入。
    EXPECT_NE(merged->find("我的标题"), std::string::npos);
    EXPECT_NE(merged->find("新备注"), std::string::npos);
    EXPECT_NE(merged->find("\"done\":true"), std::string::npos);
    EXPECT_EQ(merged->find("服务器标题"), std::string::npos);
}

TEST(MergeTest, MergePrefersTheirsAndRejectsNonObject) {
    const auto merged = mergeJsonFields(
        R"({"title":"我的"})", R"({"title":"服务器的"})", /*preferOurs=*/false);
    ASSERT_TRUE(merged.has_value());
    EXPECT_NE(merged->find("服务器的"), std::string::npos);

    EXPECT_FALSE(mergeJsonFields("[1,2]", "{}", true).has_value());
    EXPECT_FALSE(mergeJsonFields("not json", "{}", true).has_value());
}

} // namespace
