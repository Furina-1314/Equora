#include <equora/common/Logger.h>

#include <cstdio>
#include <fstream>
#include <mutex>

#include <equora/domain/Time.h>

namespace equora::common {

namespace {

struct LogState {
    std::mutex mutex;
    std::ofstream file;
    std::filesystem::path path;
    LogLevel level = LogLevel::Off;
    std::uintmax_t maxFileBytes = 2 * 1024 * 1024;
    int keepFiles = 5;
};

LogState& state() {
    static LogState s;
    return s;
}

const char* levelName(LogLevel level) noexcept {
    switch (level) {
    case LogLevel::Trace: return "TRACE";
    case LogLevel::Debug: return "DEBUG";
    case LogLevel::Info:  return "INFO ";
    case LogLevel::Warn:  return "WARN ";
    case LogLevel::Error: return "ERROR";
    case LogLevel::Off:   return "OFF  ";
    }
    return "?????";
}

void rotateIfNeeded(LogState& s) {
    if (!s.file || s.maxFileBytes == 0) return;
    std::error_code ec;
    const auto size = std::filesystem::file_size(s.path, ec);
    if (ec || size < s.maxFileBytes) return;

    s.file.close();
    for (int i = s.keepFiles - 1; i >= 1; --i) {
        // file.log.N-1 → file.log.N
        std::error_code ignored;
        std::filesystem::rename(
            std::filesystem::path(s.path.string() + "." + std::to_string(i)),
            std::filesystem::path(s.path.string() + "." + std::to_string(i + 1)), ignored);
    }
    std::error_code ignored2;
    std::filesystem::rename(s.path, std::filesystem::path(s.path.string() + ".1"), ignored2);
    s.file.open(s.path, std::ios::app);
}

} // namespace

void logInit(const std::filesystem::path& file, LogLevel level, std::uintmax_t maxFileBytes,
             int keepFiles) {
    LogState& s = state();
    const std::lock_guard<std::mutex> lock(s.mutex);
    std::error_code ec;
    std::filesystem::create_directories(file.parent_path(), ec);
    s.file.close();
    s.path = file;
    s.level = level;
    s.maxFileBytes = maxFileBytes;
    s.keepFiles = keepFiles < 1 ? 1 : keepFiles;
    s.file.open(file, std::ios::app);
}

void logShutdown() {
    LogState& s = state();
    const std::lock_guard<std::mutex> lock(s.mutex);
    s.file.close();
    s.level = LogLevel::Off;
    s.path.clear();
}

bool logEnabled(LogLevel level) noexcept {
    return level >= state().level && level != LogLevel::Off && state().level != LogLevel::Off;
}

void logWrite(LogLevel level, std::string_view category, std::string_view message) {
    LogState& s = state();
    if (level < s.level || level == LogLevel::Off) return;

    std::lock_guard<std::mutex> lock(s.mutex);
    if (!s.file.is_open()) return;

    rotateIfNeeded(s);
    s.file << domain::utc::toIso8601(domain::utc::now()) << ' ' << levelName(level) << " ["
           << category << "] " << message << '\n';
    s.file.flush();
}

} // namespace equora::common
