#include <equora/syncclient/SyncClient.h>

#include <algorithm>
#include <cmath>

#include <nlohmann/json.hpp>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>

namespace equora::sync {

using domain::ErrorCode;
using domain::EquoraError;
using domain::Uuid;
using storage::Statement;
using storage::Transaction;

SyncClientRepository::SyncClientRepository(const storage::Database& db) : db_(db) {}

// ---- Outbox ----

OutboxEntry SyncClientRepository::enqueue(const std::string& operationId,
                                          const std::string& entityType,
                                          const std::string& entityId,
                                          std::int64_t baseRevision, std::int32_t kind,
                                          const std::string& payload,
                                          std::int64_t nowMs) {
    if (operationId.empty() || entityType.empty() || entityId.empty()) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "outbox enqueue requires operationId/entityType/entityId");
    }

    Transaction tx = db_.beginTransaction(); // 与调用方业务写共用同一连接即同事务
    {
        auto st = db_.prepare(
            "INSERT INTO sync_outbox (operation_id, entity_type, entity_id, "
            "base_revision, kind, payload, status, attempts, next_attempt_at, "
            "created_at) VALUES (?, ?, ?, ?, ?, ?, 0, 0, 0, ?)");
        st.bind(1, operationId)
            .bind(2, entityType)
            .bind(3, entityId)
            .bind(4, baseRevision)
            .bind(5, kind)
            .bind(6, payload)
            .bind(7, nowMs);
        st.step();
    }
    tx.commit();

    OutboxEntry e;
    e.rowId = db_.lastInsertRowId();
    e.operationId = operationId;
    e.entityType = entityType;
    e.entityId = entityId;
    e.baseRevision = baseRevision;
    e.kind = kind;
    e.payload = payload;
    e.createdAtMs = nowMs;
    return e;
}

std::vector<OutboxEntry> SyncClientRepository::duePending(std::int64_t nowMs,
                                                          std::size_t limit) {
    auto st = db_.prepare(
        "SELECT rowid, operation_id, entity_type, entity_id, base_revision, kind, "
        "payload, status, attempts, next_attempt_at, created_at FROM sync_outbox "
        "WHERE status != 1 AND next_attempt_at <= ? ORDER BY created_at, rowid LIMIT ?");
    st.bind(1, nowMs).bind(2, static_cast<std::int64_t>(limit));

    std::vector<OutboxEntry> out;
    while (st.step()) {
        OutboxEntry e;
        e.rowId = st.columnInt(0);
        e.operationId = st.columnText(1);
        e.entityType = st.columnText(2);
        e.entityId = st.columnText(3);
        e.baseRevision = st.columnInt(4);
        e.kind = static_cast<std::int32_t>(st.columnInt(5));
        e.payload = st.columnText(6);
        e.status = static_cast<OutboxStatus>(st.columnInt(7));
        e.attempts = static_cast<std::int32_t>(st.columnInt(8));
        e.nextAttemptAtMs = st.columnInt(9);
        e.createdAtMs = st.columnInt(10);
        out.push_back(std::move(e));
    }
    return out;
}

void SyncClientRepository::markSending(std::int64_t rowId) {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare("UPDATE sync_outbox SET status = 1 WHERE rowid = ?");
        st.bind(1, rowId);
        st.step();
    }
    tx.commit();
}

void SyncClientRepository::markResult(std::int64_t rowId, bool accepted,
                                      std::int64_t nowMs, std::int32_t maxAttempts) {
    Transaction tx = db_.beginTransaction();
    if (accepted) {
        auto st = db_.prepare("DELETE FROM sync_outbox WHERE rowid = ?");
        st.bind(1, rowId);
        st.step();
    }
    else {
        auto st = db_.prepare(
            "UPDATE sync_outbox SET attempts = attempts + 1, "
            "next_attempt_at = ?, status = CASE WHEN attempts + 1 >= ? THEN 2 ELSE 0 END "
            "WHERE rowid = ?");
        st.bind(1, nowMs) // 调用方已把退避加进 nowMs(或存精确值前算好)
            .bind(2, maxAttempts)
            .bind(3, rowId);
        st.step();
    }
    tx.commit();
}

void SyncClientRepository::resetStuck(std::int64_t nowMs) {
    Transaction tx = db_.beginTransaction();
    {
        (void)nowMs;
        auto st = db_.prepare("UPDATE sync_outbox SET status = 0 WHERE status = 1");
        st.step();
    }
    tx.commit();
}

std::size_t SyncClientRepository::pendingCount() {
    auto st = db_.prepare("SELECT COUNT(*) FROM sync_outbox WHERE status != 1");
    st.step();
    return static_cast<std::size_t>(st.columnInt(0));
}

// ---- 状态 ----

SyncState SyncClientRepository::loadState() {
    SyncState s;
    auto get = [&](const char* key) {
        auto st = db_.prepare("SELECT value FROM sync_state WHERE key = ?");
        st.bind(1, key);
        return st.step() ? st.columnText(0) : std::string();
    };
    s.cursor = [&] {
        const auto v = get("cursor");
        return v.empty() ? 0 : std::stoll(v);
    }();
    s.serverUrl = get("server_url");
    s.deviceId = get("device_id");
    s.lastSuccessAtMs = [&] {
        const auto v = get("last_success_at");
        return v.empty() ? 0 : std::stoll(v);
    }();
    return s;
}

void SyncClientRepository::saveCursor(std::int64_t cursor, std::int64_t nowMs) {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO sync_state (key, value) VALUES ('cursor', ?) "
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        st.bind(1, std::to_string(cursor));
        st.step();

        auto ts = db_.prepare(
            "INSERT INTO sync_state (key, value) VALUES ('last_success_at', ?) "
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        ts.bind(1, std::to_string(nowMs));
        ts.step();
    }
    tx.commit();
}

void SyncClientRepository::saveServerUrl(const std::string& url) {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO sync_state (key, value) VALUES ('server_url', ?) "
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        st.bind(1, url);
        st.step();
    }
    tx.commit();
}

void SyncClientRepository::saveDeviceId(const std::string& deviceId) {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO sync_state (key, value) VALUES ('device_id', ?) "
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        st.bind(1, deviceId);
        st.step();
    }
    tx.commit();
}

// ---- 冲突 ----

void SyncClientRepository::addConflict(const std::string& operationId,
                                       const std::string& entityType,
                                       const std::string& entityId,
                                       const std::string& localPayload,
                                       const std::string& serverPayload,
                                       std::int64_t serverRevision, bool serverDeleted,
                                       std::int64_t nowMs) {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO sync_conflicts (operation_id, entity_type, entity_id, "
            "local_payload, server_payload, server_revision, server_deleted, "
            "created_at, resolved, resolution) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, 0)");
        st.bind(1, operationId)
            .bind(2, entityType)
            .bind(3, entityId)
            .bind(4, localPayload)
            .bind(5, serverPayload)
            .bind(6, serverRevision)
            .bind(7, serverDeleted ? 1 : 0)
            .bind(8, nowMs);
        st.step();
    }
    tx.commit();
}

std::vector<ConflictEntry> SyncClientRepository::openConflicts() {
    auto st = db_.prepare(
        "SELECT rowid, operation_id, entity_type, entity_id, local_payload, "
        "server_payload, server_revision, server_deleted, created_at, resolution "
        "FROM sync_conflicts WHERE resolved = 0 ORDER BY created_at");
    std::vector<ConflictEntry> out;
    while (st.step()) {
        ConflictEntry c;
        c.rowId = st.columnInt(0);
        c.operationId = st.columnText(1);
        c.entityType = st.columnText(2);
        c.entityId = st.columnText(3);
        c.localPayload = st.columnText(4);
        c.serverPayload = st.columnText(5);
        c.serverRevision = st.columnInt(6);
        c.serverDeleted = st.columnInt(7) != 0;
        c.createdAtMs = st.columnInt(8);
        c.resolution = static_cast<std::int32_t>(st.columnInt(9));
        out.push_back(std::move(c));
    }
    return out;
}

void SyncClientRepository::resolveConflict(std::int64_t rowId, std::int32_t resolution) {
    if (resolution < 1 || resolution > 3) {
        throw EquoraError(ErrorCode::InvalidArgument, "resolution must be 1..3");
    }
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE sync_conflicts SET resolved = 1, resolution = ? "
            "WHERE rowid = ? AND resolved = 0");
        st.bind(1, resolution).bind(2, rowId);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "conflict already resolved");
        }
    }
    tx.commit();
}

// ---- 纯函数 ----

std::int64_t backoffDelayMs(std::int32_t attempts, std::int64_t baseMs,
                            std::int64_t maxMs, double jitter0to1) {
    if (attempts <= 0) return 0;
    const double j = jitter0to1 < 0.0 ? 0.0 : (jitter0to1 > 1.0 ? 1.0 : jitter0to1);
    // 指数:base * 2^(n-1),乘 [1, 2) 抖动,封顶 maxMs。
    const double exp = baseMs * std::pow(2.0, attempts - 1) * (1.0 + j);
    const auto ms = static_cast<std::int64_t>(exp);
    return ms > maxMs ? maxMs : ms;
}

std::optional<std::string> mergeJsonFields(const std::string& ours,
                                           const std::string& theirs, bool preferOurs) {
    try {
        auto a = nlohmann::json::parse(ours);
        auto b = nlohmann::json::parse(theirs);
        if (!a.is_object() || !b.is_object()) return std::nullopt;

        const nlohmann::json& winner = preferOurs ? a : b;
        const nlohmann::json& loser = preferOurs ? b : a;
        auto merged = loser; // 先取败方全部字段
        for (auto it = winner.begin(); it != winner.end(); ++it) {
            merged[it.key()] = it.value(); // 胜方字段覆盖
        }
        return merged.dump();
    } catch (const std::exception&) {
        return std::nullopt;
    }
}

} // namespace equora::sync
