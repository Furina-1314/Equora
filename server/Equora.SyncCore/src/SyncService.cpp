#include <equora/sync/SyncService.h>

#include <mutex>
#include <unordered_map>

namespace equora::sync {

// ---- 协议逻辑 ----

SyncResponse SyncService::sync(const std::string& userId, const std::string& deviceId,
                               std::int64_t sinceCursor,
                               const std::vector<Operation>& operations) {
    SyncResponse response;

    for (const auto& op : operations) {
        // 1) 幂等:同 operationId 重复提交,不产生重复效果,返回原结果。
        if (auto prior = store_.lookupProcessed(userId, op.operationId)) {
            OperationOutcome outcome = *prior;
            outcome.result = OpResult::Duplicate;
            response.outcomes.push_back(std::move(outcome));
            continue;
        }

        // 2) 实体校验。
        if (op.entityType.empty() || op.entityId.empty() || op.operationId.empty()) {
            OperationOutcome outcome;
            outcome.operationId = op.operationId;
            outcome.result = OpResult::InvalidEntity;
            response.outcomes.push_back(std::move(outcome));
            continue;
        }

        const auto existing = store_.getEntity(userId, op.entityType, op.entityId);

        OperationOutcome outcome;
        outcome.operationId = op.operationId;

        const bool baseMatches = [&] {
            if (!existing.has_value()) return op.baseRevision == 0; // 新建
            return op.baseRevision == existing->revision;
        }();

        if (!baseMatches) {
            // 3) 冲突:携带服务端当前版本;删除优先(需求 §18)。
            outcome.result = OpResult::Conflict;
            outcome.serverDeleted =
                existing.has_value() && existing->deleted;
            outcome.serverRevision = existing.value_or(EntityRecord{}).revision;
            outcome.serverPayload =
                existing.has_value() ? existing->payload : "";
        }
        else if (op.kind == OpKind::Delete) {
            EntityRecord record;
            record.userId = userId;
            record.entityType = op.entityType;
            record.entityId = op.entityId;
            record.revision = (existing ? existing->revision : 0) + 1;
            record.deleted = true;
            record.payload = existing ? existing->payload : ""; // 保留最后版本供恢复
            record.updatedAtMs = 0; // 由存储层/HTTP 层补真实时间
            (void)store_.upsertEntity(record, deviceId);
            outcome.result = OpResult::Accepted;
            outcome.newRevision = record.revision;
        }
        else { // Create / Update
            EntityRecord record;
            record.userId = userId;
            record.entityType = op.entityType;
            record.entityId = op.entityId;
            record.revision = op.baseRevision + 1;
            record.deleted = false;
            record.payload = op.payload;
            (void)store_.upsertEntity(record, deviceId);
            outcome.result = OpResult::Accepted;
            outcome.newRevision = record.revision;
        }

        // 幂等登记(同请求内同 id 二次出现也会走 Duplicate 分支)。
        OperationOutcome registered = outcome;
        (void)store_.markProcessed(userId, op.operationId, registered);
        response.outcomes.push_back(std::move(outcome));
    }

    // 4) Pull:自 sinceCursor 之后的全量变更(含本请求刚产生的)。
    response.changes = store_.listChanges(userId, sinceCursor);
    response.nextCursor = store_.currentSeq(userId);
    return response;
}

} // namespace equora::sync
