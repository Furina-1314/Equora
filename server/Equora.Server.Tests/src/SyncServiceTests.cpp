#include <gtest/gtest.h>

#include <equora/sync/SyncService.h>
#include <equora/sync/MemorySyncStore.h>

#include <algorithm>

namespace {

using equora::sync::Change;
using equora::sync::MemorySyncStore;
using equora::sync::OpKind;
using equora::sync::OpResult;
using equora::sync::Operation;
using equora::sync::SyncService;

constexpr const char* kUser = "user-1";

Operation makeOp(const std::string& opId, const std::string& entityId,
                 std::int64_t baseRevision, OpKind kind, const std::string& payload = "{}") {
    Operation op;
    op.operationId = opId;
    op.entityType = "task";
    op.entityId = entityId;
    op.baseRevision = baseRevision;
    op.kind = kind;
    op.payload = payload;
    return op;
}

class SyncServiceTest : public ::testing::Test {
protected:
    MemorySyncStore store;
    SyncService service{store};
};

TEST_F(SyncServiceTest, CreateAssignsRevisionAndCursor) {
    const auto r = service.sync(kUser, "device-A", 0,
                                { makeOp("op-1", "task-1", 0, OpKind::Create,
                                         R"({"title":"A"})") });
    ASSERT_EQ(r.outcomes.size(), 1U);
    EXPECT_EQ(r.outcomes[0].result, OpResult::Accepted);
    EXPECT_EQ(r.outcomes[0].newRevision, 1);
    EXPECT_EQ(r.nextCursor, 1);
    ASSERT_EQ(r.changes.size(), 1U);
    EXPECT_EQ(r.changes[0].deviceId, "device-A");
}

TEST_F(SyncServiceTest, DuplicateOperationIdIsIdempotent) {
    const auto first = service.sync(kUser, "device-A", 0, { makeOp("op-1", "task-1", 0,
                                                                   OpKind::Create) });
    // 同一 operationId 重放(网络重试):不产生重复效果。
    const auto replay = service.sync(kUser, "device-A", 0, { makeOp("op-1", "task-1", 0,
                                                                    OpKind::Create) });
    ASSERT_EQ(replay.outcomes.size(), 1U);
    EXPECT_EQ(replay.outcomes[0].result, OpResult::Duplicate);
    EXPECT_EQ(replay.outcomes[0].newRevision, first.outcomes[0].newRevision);
    EXPECT_EQ(replay.nextCursor, first.nextCursor); // 无新变更
}

TEST_F(SyncServiceTest, ConcurrentModificationYieldsConflictWithServerVersion) {
    (void)service.sync(kUser, "device-A", 0, { makeOp("op-1", "task-1", 0,
                                                      OpKind::Create, R"({"v":1})") });
    // 设备 A 更新到 revision 2。
    (void)service.sync(kUser, "device-A", 1, { makeOp("op-2", "task-1", 1,
                                                      OpKind::Update, R"({"v":2})") });
    // 设备 B 持旧基线 revision 1 → 冲突,并带回服务端版本 v=2。
    const auto r = service.sync(kUser, "device-B", 2,
                                { makeOp("op-3", "task-1", 1, OpKind::Update,
                                         R"({"v":3})") });
    ASSERT_EQ(r.outcomes.size(), 1U);
    EXPECT_EQ(r.outcomes[0].result, OpResult::Conflict);
    EXPECT_EQ(r.outcomes[0].serverRevision, 2);
    EXPECT_EQ(r.outcomes[0].serverPayload, R"({"v":2})");
    EXPECT_FALSE(r.outcomes[0].serverDeleted);
}

TEST_F(SyncServiceTest, DeletionWinsOverModify) {
    (void)service.sync(kUser, "device-A", 0, { makeOp("op-1", "task-1", 0,
                                                      OpKind::Create, R"({"v":1})") });
    // 设备 A 删除(revision 1 → 2 墓碑)。
    (void)service.sync(kUser, "device-A", 0, { makeOp("op-2", "task-1", 1,
                                                      OpKind::Delete) });
    // 设备 B 仍基于 revision 1 修改 → 冲突且服务端已删除(墓碑优先)。
    const auto r = service.sync(kUser, "device-B", 2,
                                { makeOp("op-3", "task-1", 1, OpKind::Update,
                                         R"({"v":99})") });
    EXPECT_EQ(r.outcomes[0].result, OpResult::Conflict);
    EXPECT_TRUE(r.outcomes[0].serverDeleted);
    EXPECT_TRUE(SyncService::deletionWinsOverModify(r.outcomes[0].serverDeleted));

    // 变更流里的删除条目 payload 为空、kind=Delete(全量拉取验证)。
    const auto all = service.sync(kUser, "device-B", 0, {});
    const auto& del = std::find_if(all.changes.begin(), all.changes.end(),
                                   [](const Change& c) {
                                       return c.kind == OpKind::Delete;
                                   });
    ASSERT_NE(del, all.changes.end());
    EXPECT_TRUE(del->payload.empty());
}

TEST_F(SyncServiceTest, CursorReturnsOnlyIncrementalChanges) {
    (void)service.sync(kUser, "device-A", 0, { makeOp("op-1", "t1", 0, OpKind::Create) });
    const auto afterFirst = service.currentCursor(kUser);

    // 设备 B 从 0 拉:看到 t1。
    auto r0 = service.sync(kUser, "device-B", 0, {});
    ASSERT_EQ(r0.changes.size(), 1U);

    // 新变更 t2 后,B 从 afterFirst 拉:只看到 t2。
    (void)service.sync(kUser, "device-A", 0, { makeOp("op-2", "t2", 0, OpKind::Create) });
    const auto r1 = service.sync(kUser, "device-B", afterFirst, {});
    ASSERT_EQ(r1.changes.size(), 1U);
    EXPECT_EQ(r1.changes[0].entityId, "t2");
    EXPECT_EQ(r1.nextCursor, service.currentCursor(kUser));
}

TEST_F(SyncServiceTest, UsersAreIsolated) {
    (void)service.sync(kUser, "d", 0, { makeOp("op-1", "t1", 0, OpKind::Create) });
    const auto other = service.sync("user-2", "d", 0, {});
    EXPECT_TRUE(other.changes.empty());
    EXPECT_EQ(other.nextCursor, 0);
}

TEST_F(SyncServiceTest, InvalidEntityRejected) {
    Operation bad;
    bad.operationId = "";
    bad.entityType = "";
    bad.entityId = "";
    const auto r = service.sync(kUser, "d", 0, { bad });
    EXPECT_EQ(r.outcomes[0].result, OpResult::InvalidEntity);
}

// ---- M7 验收场景:两台逻辑设备离线并发后合流 ----

TEST_F(SyncServiceTest, TwoDevicesOfflineThenSyncNoDataLoss) {
    // 设备 A、B 都离线创建(基于空基线,不同实体)。
    (void)service.sync(kUser, "device-A", 0, { makeOp("a-1", "ta", 0, OpKind::Create,
                                                      R"({"who":"A"})") });
    (void)service.sync(kUser, "device-B", 0, { makeOp("b-1", "tb", 0, OpKind::Create,
                                                      R"({"who":"B"})") });

    // 各自全量拉取:两边都能看到两个实体,游标一致。
    const auto pullA = service.sync(kUser, "device-A", 0, {});
    const auto pullB = service.sync(kUser, "device-B", 0, {});
    ASSERT_EQ(pullA.changes.size(), 2U);
    ASSERT_EQ(pullB.changes.size(), 2U);
    EXPECT_EQ(pullA.nextCursor, pullB.nextCursor);

    // A 删除 ta、B 更新 ta(基于同 revision)—— 离线交叉:
    // A 先同步成功(删除),B 的更新因墓碑优先被拒(冲突)。
    const auto del = service.sync(kUser, "device-A", pullA.nextCursor,
                                  { makeOp("a-2", "ta", 1, OpKind::Delete) });
    EXPECT_EQ(del.outcomes[0].result, OpResult::Accepted);

    const auto upd = service.sync(kUser, "device-B", pullB.nextCursor,
                                  { makeOp("b-2", "ta", 1, OpKind::Update,
                                           R"({"who":"B2"})") });
    EXPECT_EQ(upd.outcomes[0].result, OpResult::Conflict);
    EXPECT_TRUE(upd.outcomes[0].serverDeleted);

    // B 重试同操作(网络重复)→ 仍为幂等 Duplicate,不复活实体。
    const auto retry = service.sync(kUser, "device-B", pullB.nextCursor,
                                    { makeOp("b-2", "ta", 1, OpKind::Update,
                                             R"({"who":"B2"})") });
    EXPECT_EQ(retry.outcomes[0].result, OpResult::Duplicate);

    // 最终状态:ta 墓碑、tb 存活 —— 无数据丢失、无重复。
    const auto final = service.sync(kUser, "device-A", 0, {});
    const auto tombstone = std::find_if(final.changes.begin(), final.changes.end(),
                                        [](const Change& c) {
                                            return c.entityId == "ta" &&
                                                   c.kind == OpKind::Delete;
                                        });
    ASSERT_NE(tombstone, final.changes.end());
    EXPECT_EQ(tombstone->revision, 2);
}

} // namespace
