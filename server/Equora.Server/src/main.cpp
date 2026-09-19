// Equora 同步服务器(Drogon + PostgreSQL)。
//
// 环境变量:
//   EQUORA_PORT             监听端口(默认 8787)
//   EQUORA_DB_URL           postgres://user:pass@host:5432/equora(必填)
//   EQUORA_BOOTSTRAP_TOKEN  引导令牌:确保用户 "local" 存在并可登录(部署即用)
//   EQUORA_BOOTSTRAP_USER   引导用户 id(默认 "local")
//
// 端点:
//   GET  /api/v1/health   探活(含数据库检查)
//   POST /api/v1/sync     同步(Bearer <token>;协议 v1)
#include <drogon/drogon.h>

#include <cstdio>
#include <optional>
#include <string>

#include <equora/server/PostgresSyncStore.h>
#include <equora/sync/SyncJson.h>
#include <equora/sync/SyncService.h>

namespace {

using equora::sync::PostgresSyncStore;
using equora::sync::SyncService;

std::string envOr(const char* key, const std::string& fallback) {
    const char* v = std::getenv(key);
    return v != nullptr && v[0] != '\0' ? std::string(v) : fallback;
}

PostgresSyncStore* g_store = nullptr;

std::optional<std::string> bearerToken(const drogon::HttpRequestPtr& req) {
    const auto& header = req->getHeader("Authorization");
    constexpr char kPrefix[] = "Bearer ";
    if (header.rfind(kPrefix, 0) != 0) return std::nullopt;
    auto token = header.substr(sizeof(kPrefix) - 1);
    if (token.empty()) return std::nullopt;
    return token;
}

drogon::HttpResponsePtr jsonResponse(std::string body, drogon::HttpStatusCode code) {
    auto resp = drogon::HttpResponse::newHttpResponse();
    resp->setStatusCode(code);
    resp->setContentTypeString("application/json");
    resp->setBody(std::move(body));
    return resp;
}

} // namespace

int main() {
    const auto port = std::stoi(envOr("EQUORA_PORT", "8787"));
    const auto dbUrl = envOr("EQUORA_DB_URL", "");
    if (dbUrl.empty()) {
        std::fprintf(stderr, "EQUORA_DB_URL is required "
                             "(postgres://user:pass@host:5432/equora)\n");
        return 1;
    }

    try {
        g_store = new PostgresSyncStore(dbUrl);
    } catch (const std::exception& e) {
        std::fprintf(stderr, "db connect failed: %s\n", e.what());
        return 1;
    }

    if (const auto token = envOr("EQUORA_BOOTSTRAP_TOKEN", ""); !token.empty()) {
        g_store->ensureUser(envOr("EQUORA_BOOTSTRAP_USER", "local"), token);
        std::fprintf(stderr, "bootstrap user ready\n");
    }

    drogon::app().registerHandler(
        "/api/v1/health",
        [](const drogon::HttpRequestPtr&,
           std::function<void(const drogon::HttpResponsePtr&)>&& cb) {
            const bool db = g_store->healthy();
            const auto body = std::string(R"({"status":")") +
                              (db ? "ok" : "degraded") + R"("})";
            cb(jsonResponse(std::move(body),
                            db ? drogon::k200OK : drogon::k503ServiceUnavailable));
        },
        {drogon::Get});

    drogon::app().registerHandler(
        "/api/v1/sync",
        [](const drogon::HttpRequestPtr& req,
           std::function<void(const drogon::HttpResponsePtr&)>&& cb) {
            const auto token = bearerToken(req);
            if (!token.has_value()) {
                cb(jsonResponse(R"({"error":"missing bearer token"})", drogon::k401Unauthorized));
                return;
            }
            const auto userId = g_store->userIdForToken(*token);
            if (!userId.has_value()) {
                cb(jsonResponse(R"({"error":"invalid token"})", drogon::k401Unauthorized));
                return;
            }

            const auto body = equora::sync::parseSyncRequest(req->getBody());
            if (!body.has_value()) {
                cb(jsonResponse(R"({"error":"bad-request"})",
                                drogon::k400BadRequest));
                return;
            }
            if (!g_store->ensureDevice(*userId, body->deviceId)) {
                cb(jsonResponse(R"({"error":"device revoked"})", drogon::k403Forbidden));
                return;
            }

            try {
                SyncService service(*g_store);
                const auto response = service.sync(*userId, body->deviceId,
                                                   body->cursor, body->operations);
                g_store->touchDevice(*userId, body->deviceId);
                cb(jsonResponse(equora::sync::serializeSyncResponse(response),
                                drogon::k200OK));
            } catch (const std::exception& e) {
                std::fprintf(stderr, "sync error: %s\n", e.what());
                cb(jsonResponse(R"({"error":"internal"})", drogon::k500InternalServerError));
            }
        },
        {drogon::Post});

    std::fprintf(stderr, "Equora sync server listening on :%d\n", port);
    drogon::app().addListener("0.0.0.0", port).setThreadNum(4).run();
    return 0;
}
