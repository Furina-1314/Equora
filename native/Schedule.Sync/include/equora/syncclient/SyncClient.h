#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Task.h>
#include <equora/storage/Database.h>

namespace equora::sync {

// ---- Outbox 条目:与业务写同事务产生,等待上传 ----

enum class OutboxStatus : std::int32_t {
    Pending = 0,    // 待上传
    Sending = 1,    // 上传中(崩溃后重启应重置回 Pending)
    Failed = 2,     // 连续失败超过阈值(需人工/退避上限触发)
};

struct OutboxEntry {
    std::int64_t rowId = 0;
    std::string operationId;   // UUID(幂等键,与服务器协议一致)
    std::string entityType;    // "task" | "project" | ...
    std::string entityId;
    std::int64_t baseRevision = 0;
    std::int32_t kind = 1;     // 0 create / 1 update / 2 delete(与服务器 OpKind 一致)
    std::string payload;       // JSON 文本
    OutboxStatus status = OutboxStatus::Pending;
    std::int32_t attempts = 0;
    std::int64_t nextAttemptAtMs = 0; // 下次可尝试时刻(退避)
    std::int64_t createdAtMs = 0;
};

// ---- 同步状态 ----

struct SyncState {
    std::int64_t cursor = 0;             // 服务端游标
    std::string serverUrl;               // https://host[:port]
    std::string deviceId;                // 本机设备 UUID(app_meta 亦存)
    std::int64_t lastSuccessAtMs = 0;
};

// ---- 冲突条目(冲突中心):等待用户选择 ----

struct ConflictEntry {
    std::int64_t rowId = 0;
    std::string operationId;    // 触发冲突的本地操作
    std::string entityType;
    std::string entityId;
    std::string localPayload;   // 我方版本
    std::string serverPayload;  // 服务端版本
    std::int64_t serverRevision = 0;
    bool serverDeleted = false;
    std::int64_t createdAtMs = 0;
    bool resolved = false;
    std::int32_t resolution = 0; // 0 未决 1 保留本地 2 采纳服务端 3 手动合并
};

/// 同步客户端仓库(本地侧):Outbox、游标状态、冲突登记。
class SyncClientRepository {
public:
    SyncClientRepository(const storage::Database& db);

    // ---- Outbox ----

    /// 入队(调用方负责在外层业务事务内调用 —— 传入同一 Database 引用即同连接)。
    OutboxEntry enqueue(const std::string& operationId, const std::string& entityType,
                        const std::string& entityId, std::int64_t baseRevision,
                        std::int32_t kind, const std::string& payload,
                        std::int64_t nowMs);
    /// 待上传条目(状态 != Sending,且退避时间已到),按创建序。
    std::vector<OutboxEntry> duePending(std::int64_t nowMs, std::size_t limit = 64);
    void markSending(std::int64_t rowId);
    void markResult(std::int64_t rowId, bool accepted, std::int64_t nowMs,
                    std::int32_t maxAttempts);
    /// 崩溃恢复:Sending → Pending(启动时调用)。
    void resetStuck(std::int64_t nowMs);
    std::size_t pendingCount();

    // ---- 状态 ----

    SyncState loadState();
    void saveCursor(std::int64_t cursor, std::int64_t nowMs);
    void saveServerUrl(const std::string& url);
    void saveDeviceId(const std::string& deviceId);

    // ---- 冲突 ----

    void addConflict(const std::string& operationId, const std::string& entityType,
                     const std::string& entityId, const std::string& localPayload,
                     const std::string& serverPayload, std::int64_t serverRevision,
                     bool serverDeleted, std::int64_t nowMs);
    std::vector<ConflictEntry> openConflicts();
    void resolveConflict(std::int64_t rowId, std::int32_t resolution);

private:
    const storage::Database& db_;
};

/// 退避计算(纯函数,可测):第 attempts 次失败后的下次尝试延迟(毫秒)。
/// 指数 + 上限;抖动由调用方(传输层)注入随机数 j∈[0,1) 传入。
[[nodiscard]] std::int64_t backoffDelayMs(std::int32_t attempts,
                                          std::int64_t baseMs = 1000,
                                          std::int64_t maxMs = 300'000,
                                          double jitter0to1 = 0.0);

/// 字段级合并(纯函数,可测):对两个字段相同的 JSON 对象,
/// 保留双方所有字段;同字段不同值时取 ours(最后修改胜出,调用方可换)。
/// 返回合并后的 JSON 文本;输入非法时返回 nullopt。
[[nodiscard]] std::optional<std::string> mergeJsonFields(const std::string& ours,
                                                         const std::string& theirs,
                                                         bool preferOurs);

} // namespace equora::sync
