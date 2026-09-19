#include <equora/server/PostgresSyncStore.h>

#include <libpq-fe.h>

#include <stdexcept>

#include <equora/sync/Sha256.h>

namespace equora::sync {

namespace {

[[noreturn]] void throwDb(pg_conn* conn, const char* context) {
    throw std::runtime_error(std::string("postgres ") + context + ": " +
                             (conn != nullptr ? PQerrorMessage(conn) : "no conn"));
}

} // namespace

PostgresSyncStore::PostgresSyncStore(const std::string& url) {
    conn_ = PQconnectdb(url.c_str());
    if (conn_ == nullptr || PQstatus(conn_) != CONNECTION_OK) {
        const std::string msg =
            conn_ != nullptr ? PQerrorMessage(conn_) : "alloc failed";
        if (conn_ != nullptr) PQfinish(conn_);
        conn_ = nullptr;
        throw std::runtime_error("connect: " + msg);
    }
}

PostgresSyncStore::~PostgresSyncStore() {
    if (conn_ != nullptr) PQfinish(conn_);
}

std::optional<EntityRecord> PostgresSyncStore::getEntity(const std::string& userId,
                                                         const std::string& entityType,
                                                         const std::string& entityId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const char* params[3] = {userId.c_str(), entityType.c_str(), entityId.c_str()};
    auto* res = PQexecParams(conn_,
        "SELECT revision, deleted, payload::text FROM sync_entities "
        "WHERE user_id = $1 AND entity_type = $2 AND entity_id = $3",
        3, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_TUPLES_OK) throwDb(conn_, "getEntity");
    std::optional<EntityRecord> out;
    if (PQntuples(res) == 1) {
        EntityRecord r;
        r.userId = userId;
        r.entityType = entityType;
        r.entityId = entityId;
        r.revision = std::stoll(PQgetvalue(res, 0, 0));
        r.deleted = PQgetvalue(res, 0, 1)[0] == 't';
        r.payload = PQgetvalue(res, 0, 2);
        out = std::move(r);
    }
    PQclear(res);
    return out;
}

std::int64_t PostgresSyncStore::upsertEntity(const EntityRecord& record,
                                             const std::string& deviceId) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (PQresultStatus(PQexec(conn_, "BEGIN")) != PGRES_COMMAND_OK) {
        throwDb(conn_, "begin");
    }
    try {
        std::int64_t seq = 0;
        char rev[24], kind[8];
        std::snprintf(rev, sizeof(rev), "%lld",
                      static_cast<long long>(record.revision));
        std::snprintf(kind, sizeof(kind), "%d",
                      static_cast<int>(record.deleted ? OpKind::Delete
                                                      : OpKind::Update));
        {
            const char* params[6] = {record.userId.c_str(), record.entityType.c_str(),
                                     record.entityId.c_str(), rev,
                                     record.deleted ? "t" : "f",
                                     record.payload.c_str()};
            auto* res = PQexecParams(conn_,
                "INSERT INTO sync_entities (user_id, entity_type, entity_id, revision, "
                "deleted, payload, updated_at) VALUES ($1, $2, $3, $4, $5, $6::jsonb, now()) "
                "ON CONFLICT (user_id, entity_type, entity_id) DO UPDATE SET "
                "revision = EXCLUDED.revision, deleted = EXCLUDED.deleted, "
                "payload = EXCLUDED.payload, updated_at = now()",
                6, nullptr, params, nullptr, nullptr, 1);
            if (PQresultStatus(res) != PGRES_COMMAND_OK) throwDb(conn_, "upsert");
            PQclear(res);
        }
        {
            const char* params[7] = {record.userId.c_str(), record.entityType.c_str(),
                                     record.entityId.c_str(), rev, kind,
                                     record.deleted ? "{}" : record.payload.c_str(),
                                     deviceId.c_str()};
            auto* res = PQexecParams(conn_,
                "INSERT INTO change_log (user_id, entity_type, entity_id, revision, "
                "kind, payload, device_id) VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7) "
                "RETURNING seq",
                7, nullptr, params, nullptr, nullptr, 1);
            if (PQresultStatus(res) != PGRES_TUPLES_OK) throwDb(conn_, "change_log");
            seq = std::stoll(PQgetvalue(res, 0, 0));
            PQclear(res);
        }
        if (PQresultStatus(PQexec(conn_, "COMMIT")) != PGRES_COMMAND_OK) {
            throwDb(conn_, "commit");
        }
        return seq;
    } catch (...) {
        PQexec(conn_, "ROLLBACK");
        throw;
    }
}

std::vector<Change> PostgresSyncStore::listChanges(const std::string& userId,
                                                   std::int64_t sinceCursor,
                                                   std::size_t limit) {
    std::lock_guard<std::mutex> lock(mutex_);
    char cursor[24], lim[24];
    std::snprintf(cursor, sizeof(cursor), "%lld",
                  static_cast<long long>(sinceCursor));
    std::snprintf(lim, sizeof(lim), "%zu", limit > 0 ? limit : 100000);
    const char* params[3] = {userId.c_str(), cursor, lim};
    auto* res = PQexecParams(conn_,
        "SELECT seq, entity_type, entity_id, revision, kind, payload::text, device_id "
        "FROM change_log WHERE user_id = $1 AND seq > $2 ORDER BY seq LIMIT $3",
        3, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_TUPLES_OK) throwDb(conn_, "listChanges");

    std::vector<Change> out;
    for (int i = 0; i < PQntuples(res); ++i) {
        Change c;
        c.seq = std::stoll(PQgetvalue(res, i, 0));
        c.entityType = PQgetvalue(res, i, 1);
        c.entityId = PQgetvalue(res, i, 2);
        c.revision = std::stoll(PQgetvalue(res, i, 3));
        c.kind = static_cast<OpKind>(std::stoi(PQgetvalue(res, i, 4)));
        c.payload = PQgetvalue(res, i, 5);
        c.deviceId = PQgetvalue(res, i, 6);
        out.push_back(std::move(c));
    }
    PQclear(res);
    return out;
}

std::int64_t PostgresSyncStore::currentSeq(const std::string& userId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const char* params[1] = {userId.c_str()};
    auto* res = PQexecParams(conn_,
        "SELECT COALESCE(MAX(seq), 0) FROM change_log WHERE user_id = $1",
        1, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_TUPLES_OK) throwDb(conn_, "currentSeq");
    const std::int64_t seq = std::stoll(PQgetvalue(res, 0, 0));
    PQclear(res);
    return seq;
}

bool PostgresSyncStore::markProcessed(const std::string& userId,
                                      const std::string& operationId,
                                      const OperationOutcome& outcome) {
    std::lock_guard<std::mutex> lock(mutex_);
    char result[8], newRev[24], serverRev[24];
    std::snprintf(result, sizeof(result), "%d", static_cast<int>(outcome.result));
    std::snprintf(newRev, sizeof(newRev), "%lld",
                  static_cast<long long>(outcome.newRevision));
    std::snprintf(serverRev, sizeof(serverRev), "%lld",
                  static_cast<long long>(outcome.serverRevision));
    const char* params[7] = {userId.c_str(), operationId.c_str(), result, newRev,
                             serverRev, outcome.serverDeleted ? "t" : "f",
                             outcome.serverPayload.empty() ? "{}"
                                                           : outcome.serverPayload.c_str()};
    auto* res = PQexecParams(conn_,
        "INSERT INTO processed_ops (user_id, operation_id, result, new_revision, "
        "server_revision, server_deleted, server_payload) "
        "VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb) ON CONFLICT DO NOTHING",
        7, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_COMMAND_OK) throwDb(conn_, "markProcessed");
    // ON CONFLICT DO NOTHING:影响行数 "1" = 首次,"0" = 已存在(幂等命中)。
    const bool inserted = std::string(PQcmdTuples(res)) == "1";
    PQclear(res);
    return inserted;
}

std::optional<OperationOutcome> PostgresSyncStore::lookupProcessed(
    const std::string& userId, const std::string& operationId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const char* params[2] = {userId.c_str(), operationId.c_str()};
    auto* res = PQexecParams(conn_,
        "SELECT result, new_revision, server_revision, server_deleted, "
        "server_payload::text FROM processed_ops "
        "WHERE user_id = $1 AND operation_id = $2",
        2, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_TUPLES_OK) throwDb(conn_, "lookupProcessed");
    std::optional<OperationOutcome> out;
    if (PQntuples(res) == 1) {
        OperationOutcome o;
        o.operationId = operationId;
        o.result = static_cast<OpResult>(std::stoi(PQgetvalue(res, 0, 0)));
        o.newRevision = std::stoll(PQgetvalue(res, 0, 1));
        o.serverRevision = std::stoll(PQgetvalue(res, 0, 2));
        o.serverDeleted = PQgetvalue(res, 0, 3)[0] == 't';
        o.serverPayload = PQgetvalue(res, 0, 4);
        out = std::move(o);
    }
    PQclear(res);
    return out;
}

std::optional<std::string> PostgresSyncStore::userIdForToken(const std::string& token) {
    std::lock_guard<std::mutex> lock(mutex_);
    const std::string hash = Sha256::hexOf(token);
    const char* params[1] = {hash.c_str()};
    auto* res = PQexecParams(conn_,
        "SELECT id FROM users WHERE auth_token_hash = $1",
        1, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_TUPLES_OK) throwDb(conn_, "userIdForToken");
    std::optional<std::string> userId;
    if (PQntuples(res) == 1) userId = PQgetvalue(res, 0, 0);
    PQclear(res);
    return userId;
}

void PostgresSyncStore::ensureUser(const std::string& userId, const std::string& token) {
    std::lock_guard<std::mutex> lock(mutex_);
    const std::string hash = Sha256::hexOf(token);
    const char* params[2] = {userId.c_str(), hash.c_str()};
    auto* res = PQexecParams(conn_,
        "INSERT INTO users (id, auth_token_hash) VALUES ($1, $2) "
        "ON CONFLICT (id) DO UPDATE SET auth_token_hash = EXCLUDED.auth_token_hash",
        2, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_COMMAND_OK) throwDb(conn_, "ensureUser");
    PQclear(res);
}

bool PostgresSyncStore::ensureDevice(const std::string& userId,
                                     const std::string& deviceId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const char* params[2] = {deviceId.c_str(), userId.c_str()};
    auto* res = PQexecParams(conn_,
        "INSERT INTO devices (id, user_id) VALUES ($1, $2) ON CONFLICT DO NOTHING",
        2, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_COMMAND_OK) throwDb(conn_, "ensureDevice");
    PQclear(res);

    auto* check = PQexecParams(conn_,
        "SELECT revoked FROM devices WHERE id = $1 AND user_id = $2",
        2, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(check) != PGRES_TUPLES_OK) throwDb(conn_, "ensureDevice2");
    bool ok = PQntuples(check) == 1 && PQgetvalue(check, 0, 0)[0] == 'f';
    PQclear(check);
    return ok;
}

void PostgresSyncStore::touchDevice(const std::string& userId,
                                    const std::string& deviceId) {
    std::lock_guard<std::mutex> lock(mutex_);
    const char* params[2] = {deviceId.c_str(), userId.c_str()};
    auto* res = PQexecParams(conn_,
        "UPDATE devices SET last_sync_at = now() WHERE id = $1 AND user_id = $2",
        2, nullptr, params, nullptr, nullptr, 1);
    if (PQresultStatus(res) != PGRES_COMMAND_OK) throwDb(conn_, "touchDevice");
    PQclear(res);
}

bool PostgresSyncStore::healthy() {
    std::lock_guard<std::mutex> lock(mutex_);
    auto* res = PQexec(conn_, "SELECT 1");
    const bool ok = PQresultStatus(res) == PGRES_TUPLES_OK;
    PQclear(res);
    return ok;
}

} // namespace equora::sync
