#include <gtest/gtest.h>

#include <equora/sync/MemorySyncStore.h>
#include <equora/sync/Sha256.h>
#include <equora/sync/SyncJson.h>
#include <equora/sync/SyncService.h>

namespace {

using equora::sync::MemorySyncStore;
using equora::sync::OpResult;
using equora::sync::parseSyncRequest;
using equora::sync::serializeSyncResponse;
using equora::sync::SyncService;

TEST(SyncJsonTest, ParsesValidRequest) {
    const auto body = parseSyncRequest(R"({
        "protocolVersion": 1,
        "deviceId": "device-A",
        "cursor": 18291,
        "operations": [
            {"operationId": "op-1", "entityType": "task", "entityId": "e-1",
             "baseRevision": 5, "operation": "update", "payload": {"title": "A"}}
        ]
    })");
    ASSERT_TRUE(body.has_value());
    EXPECT_EQ(body->deviceId, "device-A");
    EXPECT_EQ(body->cursor, 18291);
    ASSERT_EQ(body->operations.size(), 1U);
    EXPECT_EQ(body->operations[0].operationId, "op-1");
    EXPECT_EQ(body->operations[0].kind, equora::sync::OpKind::Update);
    EXPECT_EQ(body->operations[0].baseRevision, 5);
    EXPECT_NE(body->operations[0].payload.find("\"title\":\"A\""), std::string::npos);
}

TEST(SyncJsonTest, RejectsBadVersionAndMalformed) {
    EXPECT_FALSE(parseSyncRequest("{\"protocolVersion\":2}").has_value());
    EXPECT_FALSE(parseSyncRequest("{ not json").has_value());
    // 缺 deviceId:操作条目缺 id 在协议层不拒绝,由服务层按 InvalidEntity 处理。
    const auto partial = parseSyncRequest(R"({"protocolVersion":1,"deviceId":"d","operations":[{}]})");
    ASSERT_TRUE(partial.has_value());
    EXPECT_EQ(partial->operations[0].operationId, "");
}

TEST(SyncJsonTest, EmptyOperationsAllowed) {
    const auto body = parseSyncRequest(
        R"({"protocolVersion":1,"deviceId":"d"})");
    ASSERT_TRUE(body.has_value());
    EXPECT_TRUE(body->operations.empty());
    EXPECT_EQ(body->cursor, 0);
}

TEST(SyncJsonTest, RoundtripThroughService) {
    MemorySyncStore store;
    SyncService service{store};

    const auto body = parseSyncRequest(R"({
        "protocolVersion": 1, "deviceId": "d-1", "cursor": 0,
        "operations": [
            {"operationId": "op-1", "entityType": "task", "entityId": "t-1",
             "baseRevision": 0, "operation": "create", "payload": {"v": 1}}
        ]})");
    ASSERT_TRUE(body.has_value());
    const auto response = service.sync("u", body->deviceId, body->cursor,
                                       body->operations);
    const auto json = serializeSyncResponse(response);

    EXPECT_NE(json.find("\"result\":\"accepted\""), std::string::npos);
    EXPECT_NE(json.find("\"nextCursor\":1"), std::string::npos);
    EXPECT_NE(json.find("\"deviceId\":\"d-1\""), std::string::npos);
}

TEST(SyncJsonTest, ConflictCarriesServerVersion) {
    MemorySyncStore store;
    SyncService service{store};
    // v1 → v2;再用旧基线提交 → 冲突带服务端版本。
    (void)service.sync("u", "a", 0, {{"op-1", "task", "t", 0,
                                     equora::sync::OpKind::Create, R"({"v":1})"}});
    (void)service.sync("u", "a", 0, {{"op-2", "task", "t", 1,
                                     equora::sync::OpKind::Update, R"({"v":2})"}});
    const auto r = service.sync("u", "b", 2, {{"op-3", "task", "t", 1,
                                              equora::sync::OpKind::Update,
                                              R"({"v":3})"}});
    const auto json = serializeSyncResponse(r);
    EXPECT_NE(json.find("\"result\":\"conflict\""), std::string::npos);
    EXPECT_NE(json.find("\"serverRevision\":2"), std::string::npos);
    EXPECT_NE(json.find("\"v\":2"), std::string::npos);
}

TEST(Sha256Test, KnownVectors) {
    EXPECT_EQ(equora::sync::Sha256::hexOf(""),
              "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    EXPECT_EQ(equora::sync::Sha256::hexOf("abc"),
              "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
}

TEST(Sha256Test, LongInputAcrossBlocks) {
    EXPECT_EQ(equora::sync::Sha256::hexOf(std::string(1'000'000, 'a')),
              "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
}

} // namespace
