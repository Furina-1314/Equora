#include <equora/common/Sha256.h>

#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>

namespace equora::common {

namespace {

constexpr std::uint32_t kRoundConstants[64] = {
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u,
    0x923f82a4u, 0xab1c5ed5u, 0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
    0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u, 0xe49b69c1u, 0xefbe4786u,
    0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u,
    0x06ca6351u, 0x14292967u, 0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
    0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u, 0xa2bfe8a1u, 0xa81a664bu,
    0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au,
    0x5b9cca4fu, 0x682e6ff3u, 0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
    0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u,
};

inline std::uint32_t rotr(std::uint32_t v, unsigned n) noexcept {
    return (v >> n) | (v << (32 - n));
}

} // namespace

void Sha256::processBlock(const std::uint8_t* block) {
    std::uint32_t w[64];
    for (int i = 0; i < 16; ++i) {
        w[i] = (static_cast<std::uint32_t>(block[i * 4 + 0]) << 24) |
               (static_cast<std::uint32_t>(block[i * 4 + 1]) << 16) |
               (static_cast<std::uint32_t>(block[i * 4 + 2]) << 8) |
               (static_cast<std::uint32_t>(block[i * 4 + 3]));
    }
    for (int i = 16; i < 64; ++i) {
        const std::uint32_t s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
        const std::uint32_t s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
        w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }

    std::uint32_t a = state_[0], b = state_[1], c = state_[2], d = state_[3];
    std::uint32_t e = state_[4], f = state_[5], g = state_[6], h = state_[7];

    for (int i = 0; i < 64; ++i) {
        const std::uint32_t s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
        const std::uint32_t ch = (e & f) ^ (~e & g);
        const std::uint32_t temp1 = h + s1 + ch + kRoundConstants[i] + w[i];
        const std::uint32_t s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
        const std::uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
        const std::uint32_t temp2 = s0 + maj;
        h = g; g = f; f = e; e = d + temp1;
        d = c; c = b; b = a; a = temp1 + temp2;
    }

    state_[0] += a; state_[1] += b; state_[2] += c; state_[3] += d;
    state_[4] += e; state_[5] += f; state_[6] += g; state_[7] += h;
}

void Sha256::update(const void* data, std::size_t length) {
    if (finished_) return;
    totalBits_ += static_cast<std::uint64_t>(length) * 8;
    const auto* p = static_cast<const std::uint8_t*>(data);
    while (length > 0) {
        if (buffered_ == 0 && length >= 64) {
            processBlock(p);
            p += 64;
            length -= 64;
            continue;
        }
        const std::size_t take = 64 - buffered_ < length ? 64 - buffered_ : length;
        std::memcpy(buffer_ + buffered_, p, take);
        buffered_ += take;
        p += take;
        length -= take;
        if (buffered_ == 64) {
            processBlock(buffer_);
            buffered_ = 0;
        }
    }
}

std::array<std::uint8_t, 32> Sha256::finish() {
    if (finished_) return {};

    // 填充直接操作缓冲区:update() 会因 finished_ 语义拒绝写入,不能复用。
    buffer_[buffered_++] = 0x80;
    if (buffered_ > 56) {
        while (buffered_ < 64) buffer_[buffered_++] = 0x00;
        processBlock(buffer_);
        buffered_ = 0;
    }
    while (buffered_ < 56) buffer_[buffered_++] = 0x00;

    for (int i = 0; i < 8; ++i) {
        buffer_[56 + i] = static_cast<std::uint8_t>(totalBits_ >> (56 - i * 8));
    }
    processBlock(buffer_);
    buffered_ = 0;
    finished_ = true;

    std::array<std::uint8_t, 32> out{};
    for (int i = 0; i < 8; ++i) {
        out[i * 4 + 0] = static_cast<std::uint8_t>(state_[i] >> 24);
        out[i * 4 + 1] = static_cast<std::uint8_t>(state_[i] >> 16);
        out[i * 4 + 2] = static_cast<std::uint8_t>(state_[i] >> 8);
        out[i * 4 + 3] = static_cast<std::uint8_t>(state_[i]);
    }
    return out;
}

std::string Sha256::hexOf(const void* data, std::size_t length) {
    Sha256 sha;
    sha.update(data, length);
    const auto digest = sha.finish();

    static constexpr char kHex[] = "0123456789abcdef";
    std::string out;
    out.reserve(digest.size() * 2);
    for (const std::uint8_t byte : digest) {
        out.push_back(kHex[byte >> 4]);
        out.push_back(kHex[byte & 0x0F]);
    }
    return out;
}

std::string Sha256::hexOfFile(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) return {};

    Sha256 sha;
    std::vector<char> chunk(64 * 1024);
    while (file) {
        file.read(chunk.data(), static_cast<std::streamsize>(chunk.size()));
        const std::streamsize got = file.gcount();
        if (got > 0) sha.update(chunk.data(), static_cast<std::size_t>(got));
    }

    const auto digest = sha.finish();
    static constexpr char kHex[] = "0123456789abcdef";
    std::string out;
    out.reserve(digest.size() * 2);
    for (const std::uint8_t byte : digest) {
        out.push_back(kHex[byte >> 4]);
        out.push_back(kHex[byte & 0x0F]);
    }
    return out;
}

} // namespace equora::common
