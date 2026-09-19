#pragma once

#include <mutex>
#include <optional>
#include <string>
#include <vector>

#include <equora/sync/SyncService.h>

struct pg_conn; // 前置声明,避免泄漏 libpq 头

namespace equora::sync {

/// PostgreSQL 实现(生产)。单连接 + 互斥 —— M7 单机/小规模足够;
/// 连接池化与多副本在 M8 加固阶段引入。
class PostgresSyncStore final : public SyncStore {
public:
    // url 形如 postgres://user:pass@host:5432/equora。
    // 连接失败抛 std::runtime_error。
    explicit PostgresSyncStore(const std::string& url);
    ~PostgresSyncStore() override;

    PostgresSyncStore(const PostgresSyncStore&) = delete;
    PostgresSyncStore& operator=(const PostgresSyncStore&) = delete;

    std::optional<EntityRecord> getEntity(const std::string& userId,
                                          const std::string& entityType,
                                          const std::string& entityId) override;
    std::int64_t upsertEntity(const EntityRecord& record,
                              const std::string& deviceId) override;
    std::vector<Change> listChanges(const std::string& userId, std::int64_t sinceCursor,
                                    std::size_t limit) override;
    std::int64_t currentSeq(const std::string& userId) override;
    bool markProcessed(const std::string& userId, const std::string& operationId,
                       const OperationOutcome& outcome) override;
    std::optional<OperationOutcome> lookupProcessed(const std::string& userId,
                                                    const std::string& operationId) override;

    // ---- 服务端附加能力(账号/设备) ----

    /// Bearer 令牌 → 用户 id;不存在返回 nullopt。
    [[nodiscard]] std::optional<std::string> userIdForToken(const std::string& token);
    /// 幂等确保引导用户存在(部署即用):token 哈希可更新。
    void ensureUser(const std::string& userId, const std::string& token);
    /// 设备注册(不存在则建);已撤销返回 false。
    bool ensureDevice(const std::string& userId, const std::string& deviceId);
    void touchDevice(const std::string& userId, const std::string& deviceId);

    /// SELECT 1 探活。
    [[nodiscard]] bool healthy();

private:
    std::mutex mutex_;
    pg_conn* conn_ = nullptr;
};

} // namespace equora::sync
