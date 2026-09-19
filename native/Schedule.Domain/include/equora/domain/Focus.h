#pragma once

#include <cstdint>
#include <optional>
#include <string>

#include <equora/domain/Time.h>

namespace equora::domain {

// 专注模式。
enum class FocusMode : std::int32_t {
    Pomodoro  = 0, // 标准番茄(25+5,可自定义)
    Deep      = 1, // 60-120 分钟深度
    Flowtime  = 2, // 自由计时,结束时记录
    Stopwatch = 3, // 正向计时
    Untimed   = 4, // 无计时沉浸
};

// 会话状态(冗余列,便于查询;权威语义:actual_end 是否闭合 + state)。
enum class SessionState : std::int32_t {
    Running   = 0,
    Paused    = 1,
    Completed = 2,
    Abandoned = 3,
};

struct FocusSession {
    std::string id;
    std::optional<std::string> taskId;   // 可空 = 独立专注
    std::optional<std::string> blockId;  // 关联时间块
    FocusMode mode = FocusMode::Pomodoro;
    UtcMillis plannedStart = 0;
    std::optional<UtcMillis> plannedEnd; // Flowtime/Untimed 可空
    UtcMillis actualStart = 0;
    std::optional<UtcMillis> actualEnd;  // 空 = 未闭合(运行/暂停/崩溃遗留)
    std::int64_t pausedMs = 0;           // 累计暂停
    SessionState state = SessionState::Running;
    std::string goal;                    // 会话前目标
    std::string completionNote;          // 会话后记录
    std::int32_t completionLevel = -1;   // 0-100 自评;-1 未评
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    // 有效专注毫秒(不含暂停)。closed:已闭合用 actualEnd,否则用 now。
    [[nodiscard]] std::int64_t effectiveMs(UtcMillis now) const;
};

struct Interruption {
    std::string id;
    std::string sessionId;
    UtcMillis occurredAt = 0;
    std::int64_t durationMs = 0;
    std::string reason;
    std::string source = "manual"; // manual | app-switch
    std::string handling;          // 返回 / 放弃 / 其他
};

// 分心捕获箱条目:先持久化,后整理。
struct DistractionItem {
    std::string id;
    std::optional<std::string> sessionId;
    std::string content;
    UtcMillis capturedAt = 0;
    std::int32_t resolution = 0;    // 0未整理 1转任务 2转日程 3笔记 4并入当前任务 5丢弃
    std::string resolvedRef;        // 整理去向 id(可空)
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;
};

// 专注环境预设(执行侧在 P10 接入,此处存储与校验)。
struct FocusProfile {
    std::string id;
    std::string name;
    FocusMode mode = FocusMode::Pomodoro;
    std::int32_t plannedMinutes = 25;
    std::int32_t breakMinutes = 5;
    std::string allowedApps;  // JSON 数组文本
    std::string blockedApps;
    std::string allowedSites; // 域名 JSON 数组文本
    std::string blockedSites;
    std::int32_t notifyPolicy = 0; // 0 静音普通提醒 1 全部静音
    bool isDefault = false;
    UtcMillis createdAt = 0;
    UtcMillis updatedAt = 0;
    std::int64_t revision = 1;
    std::optional<UtcMillis> deletedAt;
    std::string lastDeviceId;

    [[nodiscard]] static FocusProfile draft(std::string name);
};

} // namespace equora::domain
