#include <equora/sync/MemorySyncStore.h>

namespace equora::sync {

std::optional<EntityRecord> MemorySyncStore::getEntity(const std::string& userId,
                                                       const std::string& entityType,
                                                       const std::string& entityId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const auto it = entities_.find(Key{userId, entityType, entityId});
    if (it == entities_.end()) return std::nullopt;
    return it->second;
}

std::int64_t MemorySyncStore::upsertEntity(const EntityRecord& record,
                                           const std::string& deviceId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const auto seq = ++seqByUser_[record.userId];
    entities_[Key{record.userId, record.entityType, record.entityId}] = record;

    Change change;
    change.seq = seq;
    change.entityType = record.entityType;
    change.entityId = record.entityId;
    change.revision = record.revision;
    change.kind = record.deleted ? OpKind::Delete : OpKind::Update;
    change.payload = record.deleted ? "" : record.payload;
    change.deviceId = deviceId;
    changes_[record.userId].push_back(std::move(change));
    return seq;
}

std::vector<Change> MemorySyncStore::listChanges(const std::string& userId,
                                                 std::int64_t sinceCursor,
                                                 std::size_t limit) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<Change> out;
    const auto it = changes_.find(userId);
    if (it == changes_.end()) return out;
    for (const auto& c : it->second) {
        if (c.seq <= sinceCursor) continue;
        out.push_back(c);
        if (limit > 0 && out.size() >= limit) break;
    }
    return out;
}

std::int64_t MemorySyncStore::currentSeq(const std::string& userId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const auto it = seqByUser_.find(userId);
    return it == seqByUser_.end() ? 0 : it->second;
}

bool MemorySyncStore::markProcessed(const std::string& userId,
                                    const std::string& operationId,
                                    const OperationOutcome& outcome) {
    std::lock_guard<std::mutex> lock(mutex_);
    return processed_[userId].emplace(operationId, outcome).second;
}

std::optional<OperationOutcome> MemorySyncStore::lookupProcessed(
    const std::string& userId, const std::string& operationId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const auto it = processed_.find(userId);
    if (it == processed_.end()) return std::nullopt;
    const auto found = it->second.find(operationId);
    if (found == it->second.end()) return std::nullopt;
    return found->second;
}

} // namespace equora::sync
