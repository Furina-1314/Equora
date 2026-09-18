#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

namespace equora::common {

// FIPS 180-4 SHA-256,流式接口。
class Sha256 {
public:
    Sha256() = default;

    void update(const void* data, std::size_t length);
    void update(std::string_view text) { update(text.data(), text.size()); }

    // 结束并输出 32 字节摘要;对象此后不可再用。
    [[nodiscard]] std::array<std::uint8_t, 32> finish();

    // 一次性便捷接口,返回小写十六进制。
    [[nodiscard]] static std::string hexOf(const void* data, std::size_t length);
    [[nodiscard]] static std::string hexOfFile(const std::string& path);

private:
    void processBlock(const std::uint8_t* block);

    std::uint32_t state_[8] = {0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au,
                               0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u};
    std::uint64_t totalBits_ = 0;
    std::uint8_t buffer_[64]{};
    std::size_t buffered_ = 0;
    bool finished_ = false;
};

} // namespace equora::common
