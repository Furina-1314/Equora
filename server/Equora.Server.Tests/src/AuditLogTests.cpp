#include <gtest/gtest.h>

#include <equora/sync/MemorySyncStore.h>
#include <equora/sync/MemorySyncStore.h>
#include <chrono>
#include <algorithm>

#include <cstdio>

namespace {

using equora::sync::MemorySyncStore;
using equora::sync::OpKind;
using equora::sync::Operation;
using equora::sync::SyncService;

/// ---- 审计日志(服务端,内存版可测;生产写表) ----
/// 记录认证失败/限流触发/同步成功等事件,不含任务正文(脱敏,需求 §24)。
struct AuditEvent {
    std::int64_t atMs;
    std::string type;    // auth_fail | rate_limited | sync_ok | device_revoked
    std::string detail;  // 脱敏摘要(如 "token=***" / "device=uuid[:8]")
};

class AuditLog {
public:
    void Record(const std::string& type, const std::string& detail) {
        events_.push_back({std::chrono::duration_cast<std::chrono::milliseconds>(
                               std::chrono::system_clock::now().time_since_epoch())
                               .count(),
                           type, detail});
    }

    [[nodiscard]] std::vector<AuditEvent> Recent(std::size_t limit = 50) const {
        if (events_.size() <= limit) return events_;
        return {events_.end() - static_cast<std::ptrdiff_t>(limit), events_.end()};
    }

    [[nodiscard]] std::size_t Count(const std::string& type) const {
        return static_cast<std::size_t>(std::count_if(events_.begin(), events_.end(),
            [&](const AuditEvent& e) { return e.type == type; }));
    }

    /// 脱敏:令牌只留前 4 后 4;设备 id 只留前 8。
    [[nodiscard]] static std::string MaskToken(const std::string& token) {
        if (token.size() <= 8) return "***";
        return token.substr(0, 4) + "***" + token.substr(token.size() - 4);
    }
    [[nodiscard]] static std::string MaskDevice(const std::string& deviceId) {
        return deviceId.substr(0, std::min<std::size_t>(8, deviceId.size()));
    }

private:
    std::vector<AuditEvent> events_;
};

TEST(AuditLogTest, RecordsAndFilters) {
    AuditLog log;
    log.Record("auth_fail", "token=" + AuditLog::MaskToken("abcdefghijklmnop"));
    log.Record("sync_ok", "device=" + AuditLog::MaskDevice("uuid-1234-5678"));
    log.Record("auth_fail", "token=" + AuditLog::MaskToken("short"));
    log.Record("rate_limited", "device=" + AuditLog::MaskDevice("dev-abc"));

    EXPECT_EQ(log.Count("auth_fail"), 2U);
    EXPECT_EQ(log.Count("sync_ok"), 1U);
    EXPECT_EQ(log.Count("rate_limited"), 1U);

    const auto recent = log.Recent(2);
    ASSERT_EQ(recent.size(), 2U);
    EXPECT_EQ(recent[1].type, "rate_limited");
}

TEST(AuditLogTest, MaskingNeverExposesFullSecrets) {
    const std::string token = "my-secret-token-value-12345678";
    const auto masked = AuditLog::MaskToken(token);
    EXPECT_EQ(masked.find("secret"), std::string::npos);
    EXPECT_NE(masked.find("***"), std::string::npos);

    const std::string device = "01234567-89ab-cdef";
    const auto d = AuditLog::MaskDevice(device);
    EXPECT_EQ(d, "01234567");
    EXPECT_EQ(d.find("89ab"), std::string::npos);
}

} // namespace
