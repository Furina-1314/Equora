#pragma once

#include <cstdint>
#include <filesystem>
#include <string_view>

namespace equora::common {

enum class LogLevel : int {
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Off = 5,
};

// 全局文件日志:线程安全,按大小轮转。所有模块共用一个实例。
// 未初始化时所有写操作为空操作(便于测试与无头环境)。
void logInit(const std::filesystem::path& file, LogLevel level,
             std::uintmax_t maxFileBytes = 2 * 1024 * 1024, int keepFiles = 5);
void logShutdown();
bool logEnabled(LogLevel level) noexcept;

// category 形如 "storage.migration";消息内不得包含敏感内容(令牌/任务正文)。
void logWrite(LogLevel level, std::string_view category, std::string_view message);

inline void logTrace(std::string_view category, std::string_view message) {
    logWrite(LogLevel::Trace, category, message);
}
inline void logDebug(std::string_view category, std::string_view message) {
    logWrite(LogLevel::Debug, category, message);
}
inline void logInfo(std::string_view category, std::string_view message) {
    logWrite(LogLevel::Info, category, message);
}
inline void logWarn(std::string_view category, std::string_view message) {
    logWrite(LogLevel::Warn, category, message);
}
inline void logError(std::string_view category, std::string_view message) {
    logWrite(LogLevel::Error, category, message);
}

} // namespace equora::common
