#include <gtest/gtest.h>

#include <equora/sync/MemorySyncStore.h>

#include <thread>

namespace {

using equora::sync::Change;
using equora::sync::MemorySyncStore;
using equora::sync::OpKind;
using equora::sync::SyncService;

// ---- 令牌桶限流器(服务端,纯逻辑可测) ----

/// 每秒 N 次、桶容量 B 的令牌桶;超限返回 false(429 语义)。
class RateLimiter {
public:
    RateLimiter(double permitsPerSecond, std::int64_t burst)
        : rate_(permitsPerSecond), burst_(burst), tokens_(static_cast<double>(burst)),
          lastRefill_(std::chrono::steady_clock::now()) {}

    bool TryAcquire() {
        Refill();
        if (tokens_ >= 1.0) {
            tokens_ -= 1.0;
            return true;
        }
        return false;
    }

    [[nodiscard]] double Available() const { return tokens_; }

private:
    void Refill() {
        const auto now = std::chrono::steady_clock::now();
        const auto elapsed = std::chrono::duration<double>(now - lastRefill_).count();
        if (elapsed <= 0) return;
        tokens_ = std::min(tokens_ + elapsed * rate_, static_cast<double>(burst_));
        lastRefill_ = now;
    }

    double rate_;
    std::int64_t burst_;
    double tokens_;
    std::chrono::steady_clock::time_point lastRefill_;
};

TEST(RateLimiterTest, AllowsUpToBurstThenRejects) {
    RateLimiter limiter(/*perSecond=*/1.0, /*burst=*/5);
    for (int i = 0; i < 5; ++i) {
        EXPECT_TRUE(limiter.TryAcquire()) << "第 " << i << " 次";
    }
    EXPECT_FALSE(limiter.TryAcquire()); // 超过桶容量 → 拒绝(429)
}

TEST(RateLimiterTest, RecoversOverTime) {
    RateLimiter limiter(/*perSecond=*/1000.0, /*burst=*/2);
    EXPECT_TRUE(limiter.TryAcquire());
    EXPECT_TRUE(limiter.TryAcquire());
    EXPECT_FALSE(limiter.TryAcquire());
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
    EXPECT_TRUE(limiter.TryAcquire()); // 1000/s → 5ms 恢复 5 个
}

TEST(RateLimiterTest, DoesNotExceedBurstOnRefill) {
    RateLimiter limiter(/*perSecond=*/1e6, /*burst=*/3);
    std::this_thread::sleep_for(std::chrono::milliseconds(2));
    EXPECT_TRUE(limiter.TryAcquire());
    EXPECT_TRUE(limiter.TryAcquire());
    EXPECT_TRUE(limiter.TryAcquire());
    EXPECT_FALSE(limiter.TryAcquire()); // 不因高速率超过桶容量
}

} // namespace
