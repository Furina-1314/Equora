#include <equora/domain/Uuid.h>

#include <gtest/gtest.h>

#include <unordered_set>

namespace {

using equora::domain::Uuid;

TEST(UuidTest, NilIsAllZero) {
    const Uuid u = Uuid::nil();
    EXPECT_TRUE(u.isNil());
    EXPECT_EQ(u.toString(), "00000000-0000-0000-0000-000000000000");
}

TEST(UuidTest, ParseRoundtrip) {
    for (int i = 0; i < 100; ++i) {
        const Uuid u = Uuid::random();
        const std::string text = u.toString();
        const auto parsed = Uuid::parse(text);
        ASSERT_TRUE(parsed.has_value());
        EXPECT_EQ(parsed->toString(), text);
        EXPECT_EQ(*parsed, u);
        EXPECT_FALSE(parsed->isNil());
    }
}

TEST(UuidTest, ParseAcceptsUppercase) {
    const auto parsed = Uuid::parse("01234567-89AB-CDEF-0123-456789ABCDEF");
    ASSERT_TRUE(parsed.has_value());
    EXPECT_EQ(parsed->toString(), "01234567-89ab-cdef-0123-456789abcdef");
}

TEST(UuidTest, RandomHasV4Layout) {
    for (int i = 0; i < 1000; ++i) {
        const auto b = Uuid::random().bytes();
        EXPECT_EQ(b[6] >> 4, 0x4);  // version 4
        EXPECT_EQ(b[8] >> 6, 0x2);  // RFC 4122 variant
    }
}

TEST(UuidTest, RandomIsUnique) {
    std::unordered_set<std::string> seen;
    for (int i = 0; i < 100'000; ++i) {
        seen.insert(Uuid::random().toString());
    }
    EXPECT_EQ(seen.size(), 100'000U);
}

TEST(UuidTest, ParseRejectsInvalid) {
    EXPECT_FALSE(Uuid::parse("").has_value());
    EXPECT_FALSE(Uuid::parse("0123456789ab cdef-0123-456789abcdef").has_value());
    EXPECT_FALSE(Uuid::parse("01234567-89ab-cdef-0123-456789abcdeg").has_value());
    EXPECT_FALSE(Uuid::parse("01234567_89ab_cdef_0123_456789abcdef").has_value());
    EXPECT_FALSE(Uuid::parse("01234567-89ab-cdef-0123-456789abcde").has_value());
    EXPECT_FALSE(Uuid::parse("01234567-89ab-cdef-0123-456789abcdeff").has_value());
    EXPECT_FALSE(Uuid::parse("01234567-89ab-cdef0123-456789abcdef").has_value());
}

} // namespace
