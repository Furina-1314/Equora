#include <equora/sync/SyncJson.h>

#include <nlohmann/json.hpp>

namespace equora::sync {

using nlohmann::json;

namespace {

[[nodiscard]] std::optional<OpKind> parseKind(const std::string& s) {
    if (s == "create") return OpKind::Create;
    if (s == "update") return OpKind::Update;
    if (s == "delete") return OpKind::Delete;
    return std::nullopt;
}

[[nodiscard]] const char* kindText(OpKind k) {
    switch (k) {
    case OpKind::Create: return "create";
    case OpKind::Update: return "update";
    case OpKind::Delete: return "delete";
    }
    return "update";
}

[[nodiscard]] const char* resultText(OpResult r) {
    switch (r) {
    case OpResult::Accepted: return "accepted";
    case OpResult::Duplicate: return "duplicate";
    case OpResult::Conflict: return "conflict";
    case OpResult::InvalidEntity: return "invalid";
    }
    return "invalid";
}

} // namespace

std::optional<SyncRequestBody> parseSyncRequest(std::string_view text) {
    json root;
    try {
        root = json::parse(text);
    } catch (const json::parse_error&) {
        return std::nullopt;
    }
    if (!root.is_object()) return std::nullopt;
    if (!root.contains("protocolVersion") ||
        root["protocolVersion"].get<int>() != 1) {
        return std::nullopt;
    }
    if (!root.contains("deviceId") || !root["deviceId"].is_string()) {
        return std::nullopt;
    }

    SyncRequestBody body;
    body.deviceId = root["deviceId"].get<std::string>();
    body.cursor = root.value("cursor", 0);

    if (root.contains("operations")) {
        if (!root["operations"].is_array()) return std::nullopt;
        for (const auto& j : root["operations"]) {
            if (!j.is_object()) return std::nullopt;
            Operation op;
            op.operationId = j.value("operationId", "");
            op.entityType = j.value("entityType", "");
            op.entityId = j.value("entityId", "");
            op.baseRevision = j.value("baseRevision", 0);
            const auto kind = parseKind(j.value("operation", "update"));
            if (!kind.has_value()) return std::nullopt;
            op.kind = *kind;
            op.payload = j.value("payload", json(json::object())).dump();
            body.operations.push_back(std::move(op));
        }
    }
    return body;
}

std::string serializeSyncResponse(const SyncResponse& response) {
    auto outcomes = json::array();
    for (const auto& o : response.outcomes) {
        auto entry = json{
            {"operationId", o.operationId},
            {"result", resultText(o.result)},
            {"newRevision", o.newRevision},
        };
        if (o.result == OpResult::Conflict) {
            entry["serverRevision"] = o.serverRevision;
            entry["serverDeleted"] = o.serverDeleted;
            entry["serverPayload"] = json::parse(o.serverPayload.empty()
                                                     ? "{}"
                                                     : o.serverPayload,
                                                 nullptr, false);
        }
        outcomes.push_back(std::move(entry));
    }

    auto changes = json::array();
    for (const auto& c : response.changes) {
        changes.push_back(json{
            {"seq", c.seq},
            {"entityType", c.entityType},
            {"entityId", c.entityId},
            {"revision", c.revision},
            {"kind", kindText(c.kind)},
            {"payload", json::parse(c.payload.empty() ? "{}" : c.payload,
                                    nullptr, false)},
            {"deviceId", c.deviceId},
        });
    }

    const json root = {
        {"protocolVersion", 1},
        {"outcomes", std::move(outcomes)},
        {"changes", std::move(changes)},
        {"nextCursor", response.nextCursor},
    };
    return root.dump();
}

} // namespace equora::sync
