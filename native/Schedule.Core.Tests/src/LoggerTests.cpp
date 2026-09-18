#include <equora/common/Logger.h>

#include <gtest/gtest.h>

#include <filesystem>
#include <fstream>
#include <string>

namespace {

using equora::common::LogLevel;
using equora::common::logInfo;
using equora::common::logInit;
using equora::common::logShutdown;
using equora::common::logError;
using equora::common::logTrace;
using equora::common::logWarn;

std::string trimFile(const std::string& path) {
    std::ifstream in(path);
    return std::string((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
}

class LoggerTest : public ::testing::Test {
protected:
    void SetUp() override {
        std::filesystem::create_directories(EQUORA_TEST_TMPDIR);
        logFile_ = std::string(EQUORA_TEST_TMPDIR) + "/logger_test.log";
        std::filesystem::remove(logFile_);
    }
    void TearDown() override { logShutdown(); }
    std::string logFile_;
};

TEST_F(LoggerTest, WritesCategoryAndLevel) {
    logInit(logFile_, LogLevel::Info);
    logInfo("storage.test", "迁移完成 v2");
    logWarn("storage.test", "备份校验失败");

    const std::string content = trimFile(logFile_);
    EXPECT_NE(content.find("[storage.test]"), std::string::npos);
    EXPECT_NE(content.find("迁移完成 v2"), std::string::npos);
    EXPECT_NE(content.find("WARN"), std::string::npos);
    EXPECT_NE(content.find("Z "), std::string::npos); // ISO-8601 时间戳前缀
}

TEST_F(LoggerTest, LevelFiltersLowerEntries) {
    logInit(logFile_, LogLevel::Warn);
    logTrace("x", "不应出现-TRACE");
    logInfo("x", "不应出现-INFO");
    logWarn("x", "应当出现-WARN");
    logShutdown();

    const std::string content = trimFile(logFile_);
    EXPECT_EQ(content.find("不应出现-TRACE"), std::string::npos);
    EXPECT_EQ(content.find("不应出现-INFO"), std::string::npos);
    EXPECT_NE(content.find("应当出现-WARN"), std::string::npos);
}

TEST_F(LoggerTest, UninitializedWritesAreNoop) {
    logShutdown();
    logError("x", "未初始化时安全");
    SUCCEED();
}

TEST_F(LoggerTest, RotationKeepsBoundedFiles) {
    logInit(logFile_, LogLevel::Trace, /*maxFileBytes=*/64, /*keepFiles=*/2);
    for (int i = 0; i < 20; ++i) {
        logInfo("rot", std::string(40, 'x') + std::to_string(i));
    }
    logShutdown();

    EXPECT_TRUE(std::filesystem::exists(logFile_));
    EXPECT_TRUE(std::filesystem::exists(logFile_ + ".1"));
    EXPECT_TRUE(std::filesystem::exists(logFile_ + ".2"));
    EXPECT_FALSE(std::filesystem::exists(logFile_ + ".3")); // 超出 keepFiles
}

} // namespace
