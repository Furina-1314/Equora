#include <equora/domain/Uuid.h>

#include <random>

namespace equora::domain {

namespace {

[[nodiscard]] char hexDigit(uint8_t v) noexcept {
    return static_cast<char>("0123456789abcdef"[v & 0x0F]);
}

[[nodiscard]] int hexValue(char c) noexcept {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

} // namespace

Uuid Uuid::nil() {
    return Uuid{};
}

Uuid Uuid::random() {
    // thread_local 引擎避免频繁重建;两次 random_device 抽取组合成 64 位种子。
    thread_local std::mt19937_64 rng(
        (static_cast<uint64_t>(std::random_device{}()) << 32) |
        static_cast<uint64_t>(std::random_device{}()));
    std::array<uint8_t, 16> b{};
    for (int i = 0; i < 16; i += 8) {
        const uint64_t v = rng();
        b[i + 0] = static_cast<uint8_t>(v >> 0);
        b[i + 1] = static_cast<uint8_t>(v >> 8);
        b[i + 2] = static_cast<uint8_t>(v >> 16);
        b[i + 3] = static_cast<uint8_t>(v >> 24);
        b[i + 4] = static_cast<uint8_t>(v >> 32);
        b[i + 5] = static_cast<uint8_t>(v >> 40);
        b[i + 6] = static_cast<uint8_t>(v >> 48);
        b[i + 7] = static_cast<uint8_t>(v >> 56);
    }
    b[6] = static_cast<uint8_t>((b[6] & 0x0F) | 0x40); // version 4
    b[8] = static_cast<uint8_t>((b[8] & 0x3F) | 0x80); // RFC 4122 variant
    return Uuid(b);
}

std::optional<Uuid> Uuid::parse(std::string_view text) {
    if (text.size() != 36) return std::nullopt;

    std::array<uint8_t, 16> b{};
    int byteIndex = 0;
    int hi = -1;
    for (std::size_t i = 0; i < text.size(); ++i) {
        if (i == 8 || i == 13 || i == 18 || i == 23) {
            if (text[i] != '-') return std::nullopt;
            continue;
        }
        const int v = hexValue(text[i]);
        if (v < 0) return std::nullopt;
        if (hi < 0) {
            hi = v;
        } else {
            b[static_cast<std::size_t>(byteIndex++)] =
                static_cast<uint8_t>((hi << 4) | v);
            hi = -1;
        }
    }
    return Uuid(b);
}

std::string Uuid::toString() const {
    static constexpr int kHyphenAt[4] = {8, 13, 18, 23};
    std::string out;
    out.reserve(36);
    int h = 0;
    for (std::size_t i = 0; i < bytes_.size(); ++i) {
        out.push_back(hexDigit(bytes_[i] >> 4));
        out.push_back(hexDigit(bytes_[i]));
        if (h < 4 && static_cast<int>(out.size()) == kHyphenAt[h]) {
            out.push_back('-');
            ++h;
        }
    }
    return out;
}

bool Uuid::isNil() const noexcept {
    for (const uint8_t v : bytes_) {
        if (v != 0) return false;
    }
    return true;
}

} // namespace equora::domain
