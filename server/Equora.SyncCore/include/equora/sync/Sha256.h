#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

namespace equora::sync {

// SHA-256(FIPS 180-4)。与 native/Schedule.Common 共享的纯算法副本
// (服务端与桌面端允许共享领域规则;两处保持同步)。
class Sha256 {
public:
    void update(const void* data, std::size_t length);
    void update(std::string_view text) { update(text.data(), text.size()); }
    [[nodiscard]] std::array<std::uint8_t, 32> finish();
    [[nodiscard]] static std::string hexOf(std::string_view text);

private:
    void processBlock(const std::uint8_t* block);
    std::uint32_t state_[8] = {0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au,
                               0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u};
    std::uint64_t totalBits_ = 0;
    std::uint8_t buffer_[64]{};
    std::size_t buffered_ = 0;
    bool finished_ = false;
};

} // namespace equora::sync
