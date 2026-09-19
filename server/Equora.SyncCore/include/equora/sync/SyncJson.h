#pragma once

#include <optional>
#include <string>
#include <string_view>

#include <equora/sync/SyncService.h>

namespace equora::sync {

/// POST /api/v1/sync 请求体(HTTP 层与协议层的边界编解码,可完整单测)。
struct SyncRequestBody {
    std::string deviceId;
    std::int64_t cursor = 0;
    std::vector<Operation> operations;
};

/// 解析请求 JSON;protocolVersion != 1 或结构非法返回 nullopt。
[[nodiscard]] std::optional<SyncRequestBody> parseSyncRequest(std::string_view json);

/// 序列化响应(outcomes/changes/nextCursor);kind/result 以小写串表示。
[[nodiscard]] std::string serializeSyncResponse(const SyncResponse& response);

} // namespace equora::sync
