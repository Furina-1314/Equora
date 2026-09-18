#include <equora/common/Sha256.h>

#include <gtest/gtest.h>

#include <cstdio>
#include <filesystem>
#include <fstream>
#include <string>

namespace {

using equora::common::Sha256;

TEST(Sha256Test, KnownVectors) {
    EXPECT_EQ(Sha256::hexOf("", 0),
              "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    EXPECT_EQ(Sha256::hexOf("abc", 3),
              "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    EXPECT_EQ(Sha256::hexOf("The quick brown fox jumps over the lazy dog", 43),
              "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
    EXPECT_EQ(Sha256::hexOf("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", 56),
              "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1");
}

TEST(Sha256Test, MillionA) {
    const std::string chunk(1000, 'a');
    Sha256 sha;
    for (int i = 0; i < 1000; ++i) sha.update(chunk);
    const auto digest = sha.finish();

    static constexpr char kHex[] = "0123456789abcdef";
    std::string hex;
    for (const auto byte : digest) {
        hex.push_back(kHex[byte >> 4]);
        hex.push_back(kHex[byte & 0x0F]);
    }
    EXPECT_EQ(hex, "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
}

TEST(Sha256Test, StreamingMatchesOneShot) {
    const std::string data = "衡序 Equora — 分块与一次性摘要必须一致 0123456789";
    Sha256 streamed;
    for (std::size_t i = 0; i < data.size(); i += 7) {
        streamed.update(data.data() + i, std::min<std::size_t>(7, data.size() - i));
    }
    const auto a = streamed.finish();
    Sha256 oneShot;
    oneShot.update(data);
    const auto b = oneShot.finish();
    EXPECT_EQ(a, b);
}

TEST(Sha256Test, HexOfFile) {
    const std::string path = EQUORA_TEST_TMPDIR "/sha256_test.bin";
    std::filesystem::create_directories(EQUORA_TEST_TMPDIR);
    {
        std::ofstream out(path, std::ios::binary | std::ios::trunc);
        out << "abc";
    }
    EXPECT_EQ(Sha256::hexOfFile(path),
              "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    EXPECT_TRUE(Sha256::hexOfFile(EQUORA_TEST_TMPDIR "/definitely_missing.bin").empty());
}

} // namespace
