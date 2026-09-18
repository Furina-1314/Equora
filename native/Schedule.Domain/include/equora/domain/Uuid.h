#pragma once

#include <array>
#include <compare>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace equora::domain {

// RFC 4122 UUID(16 字节)。所有跨设备实体标识统一使用 UUID,
// 不使用本机自增 ID。文本形式为规范小写 8-4-4-4-12。
class Uuid {
public:
    Uuid() = default; // nil UUID

    [[nodiscard]] static Uuid nil();
    [[nodiscard]] static Uuid random(); // 版本 4(随机)

    // 解析 8-4-4-4-12 文本形式,大小写不敏感;失败返回 std::nullopt。
    [[nodiscard]] static std::optional<Uuid> parse(std::string_view text);

    [[nodiscard]] std::string toString() const;
    [[nodiscard]] bool isNil() const noexcept;
    [[nodiscard]] std::array<uint8_t, 16> bytes() const noexcept { return bytes_; }

    [[nodiscard]] auto operator<=>(const Uuid&) const noexcept = default;

private:
    explicit Uuid(std::array<uint8_t, 16> bytes) : bytes_(bytes) {}

    std::array<uint8_t, 16> bytes_{};
};

} // namespace equora::domain
