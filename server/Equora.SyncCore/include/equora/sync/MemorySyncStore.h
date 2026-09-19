#pragma once

#include <mutex>
#include <unordered_map>

#include <equora/sync/SyncService.h>

namespace equora::sync {

/// 内存同步存储(开发/测试用;生产为 PostgreSQL 实现)。
class MemorySyncStore final : public SyncStore {
public:
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

private:
    struct Key {
        std::string userId, entityType, entityId;
        bool operator==(const Key& o) const {
            return userId == o.userId && entityType == o.entityType &&
                   entityId == o.entityId;
        }
    };
    struct KeyHash {
        std::size_t operator()(const Key& k) const {
            return std::hash<std::string>{}(k.userId + "|" + k.entityType + "|" + k.entityId);
        }
    };

    std::mutex mutex_;
    std::unordered_map<Key, EntityRecord, KeyHash> entities_;
    std::unordered_map<std::string, std::int64_t> seqByUser_;
    std::unordered_map<std::string, std::vector<Change>> changes_;
    std::unordered_map<std::string,
                       std::unordered_map<std::string, OperationOutcome>> processed_;
};

} // namespace equora::sync
