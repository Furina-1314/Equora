#pragma once

#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace equora::sync {

// 变更类型(有序变更流中的条目)。
enum class OpKind : std::int32_t { Create = 0, Update = 1, Delete = 2 };

// 客户端提交的一个操作。
struct Operation {
    std::string operationId;   // 幂等键(UUID)
    std::string entityType;    // "task" | "project" | ...
    std::string entityId;      // UUID
    std::int64_t baseRevision = 0; // 客户端所见的基线 revision;0 = 新建
    OpKind kind = OpKind::Create;
    std::string payload;       // JSON 文本(Delete 时空)
};

// 服务端变更流条目。
struct Change {
    std::int64_t seq = 0;      // 用户内单调递增游标
    std::string entityType;
    std::string entityId;
    std::int64_t revision = 0;
    OpKind kind = OpKind::Create;
    std::string payload;
    std::string deviceId;      // 产生该变更的设备
};

// 单个操作的处理结果。
enum class OpResult : std::int32_t {
    Accepted = 0,
    Duplicate = 1,        // 幂等命中(此前已处理,返回原结果)
    Conflict = 2,         // base_revision 不匹配;携带服务端当前版本供合并
    InvalidEntity = 3,    // 实体类型/ID 非法
};

struct OperationOutcome {
    std::string operationId;
    OpResult result = OpResult::Accepted;
    std::int64_t newRevision = 0;
    // Conflict 时:服务端当前版本(payload 与 revision),供字段级合并。
    std::string serverPayload;
    std::int64_t serverRevision = 0;
    bool serverDeleted = false;
};

struct SyncResponse {
    std::vector<OperationOutcome> outcomes;
    std::vector<Change> changes;   // sinceCursor 之后的所有服务端变更(含本次)
    std::int64_t nextCursor = 0;
};

// 实体在服务端的镜像状态。
struct EntityRecord {
    std::string userId;
    std::string entityType;
    std::string entityId;
    std::int64_t revision = 0;
    bool deleted = false;
    std::string payload;      // 最新有效载荷(墓碑保留最后版本供恢复)
    std::int64_t updatedAtMs = 0;
};

/// 同步存储抽象:Postgres(生产)与内存(测试/开发)各实现一份。
class SyncStore {
public:
    virtual ~SyncStore() = default;

    virtual std::optional<EntityRecord> getEntity(const std::string& userId,
                                                  const std::string& entityType,
                                                  const std::string& entityId) = 0;
    // 写入并分配全局递增 seq;返回新 seq。
    virtual std::int64_t upsertEntity(const EntityRecord& record,
                                      const std::string& deviceId) = 0;
    // 变更流:seq > sinceCursor,升序,至多 limit(0=不限)。
    virtual std::vector<Change> listChanges(const std::string& userId,
                                            std::int64_t sinceCursor,
                                            std::size_t limit = 0) = 0;
    virtual std::int64_t currentSeq(const std::string& userId) = 0;

    // 幂等登记:返回是否首次出现(首次则记录并返回 true)。
    virtual bool markProcessed(const std::string& userId,
                               const std::string& operationId,
                               const OperationOutcome& outcome) = 0;
    virtual std::optional<OperationOutcome> lookupProcessed(const std::string& userId,
                                                            const std::string& operationId) = 0;
};

/// 同步协议服务:纯逻辑,与 HTTP/存储实现解耦(可完整单测)。
class SyncService {
public:
    SyncService(SyncStore& store) : store_(store) {}

    /// Push+Pull 一体。deviceId 用于变更流溯源。
    SyncResponse sync(const std::string& userId, const std::string& deviceId,
                      std::int64_t sinceCursor,
                      const std::vector<Operation>& operations);

    std::int64_t currentCursor(const std::string& userId) {
        return store_.currentSeq(userId);
    }

    // 冲突策略辅助(确定性,供调用方在冲突中心展示):
    // 删除与修改冲突 → 默认保留墓碑(需求 §18)。
    static bool deletionWinsOverModify(bool serverDeleted) { return serverDeleted; }

private:
    SyncStore& store_;
};

} // namespace equora::sync
