#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Focus.h>
#include <equora/storage/Database.h>

namespace equora::core {

/// 专注会话仓库:状态转换持久化 + 中断/分心捕获/预设。
/// 计时滴答不落库,只在状态转换时写(暂停/恢复/中断/完成/放弃),
/// 崩溃恢复以 updated_at(最后活动时刻)收尾遗留会话。
class FocusRepository {
public:
    FocusRepository(const storage::Database& db, std::string deviceId);

    // ---- 会话状态机 ----

    // 开始会话:同一时刻只允许一个未闭合会话,已有则抛 Conflict。
    // plannedMinutes<=0 且模式为 Pomodoro/Deep/Stopwatch 时按默认 25 分钟。
    [[nodiscard]] domain::FocusSession start(domain::FocusMode mode, std::int32_t plannedMinutes,
                                             const std::string& goal,
                                             const std::optional<std::string>& taskId,
                                             const std::optional<std::string>& blockId,
                                             domain::UtcMillis now) const;

    [[nodiscard]] std::optional<domain::FocusSession> find(const std::string& id) const;
    [[nodiscard]] std::optional<domain::FocusSession> openSession() const; // 未闭合
    [[nodiscard]] std::vector<domain::FocusSession> history(domain::UtcMillis from,
                                                            domain::UtcMillis to,
                                                            std::int32_t limit = 0) const;

    // 暂停/恢复(幂等保护:状态不符抛 Conflict)。
    [[nodiscard]] domain::FocusSession pause(const std::string& id, domain::UtcMillis now) const;
    [[nodiscard]] domain::FocusSession resume(const std::string& id, domain::UtcMillis now) const;

    // 完成:闭合 actualEnd、记录完成信息;可在完成前写 note/level。
    [[nodiscard]] domain::FocusSession complete(const std::string& id, domain::UtcMillis now,
                                                const std::string& note,
                                                std::int32_t completionLevel) const;
    // 放弃:闭合并标记 Abandoned。
    [[nodiscard]] domain::FocusSession abandon(const std::string& id, domain::UtcMillis now) const;

    // ---- 崩溃恢复 ----
    // 把所有 actual_end 为空的遗留会话以 updated_at 收尾为 Abandoned。
    // 返回收尾的会话数。
    [[nodiscard]] int recoverInterrupted(domain::UtcMillis now) const;

    // ---- 中断 ----
    [[nodiscard]] domain::Interruption addInterruption(const std::string& sessionId,
                                                       domain::UtcMillis occurredAt,
                                                       std::int64_t durationMs,
                                                       const std::string& reason,
                                                       const std::string& source,
                                                       const std::string& handling) const;
    [[nodiscard]] std::vector<domain::Interruption> interruptionsOf(
        const std::string& sessionId) const;

    // ---- 分心捕获(即时落库,先持久化后整理) ----
    [[nodiscard]] domain::DistractionItem capture(const std::string& content,
                                                  const std::optional<std::string>& sessionId,
                                                  domain::UtcMillis now) const;
    [[nodiscard]] std::vector<domain::DistractionItem> pendingDistractions(
        std::int32_t limit = 100) const;
    [[nodiscard]] domain::DistractionItem resolveDistraction(const std::string& id,
                                                              std::int32_t resolution,
                                                              const std::string& resolvedRef) const;

    // ---- 专注预设 ----
    [[nodiscard]] domain::FocusProfile createProfile(domain::FocusProfile draft) const;
    [[nodiscard]] std::optional<domain::FocusProfile> findProfile(const std::string& id) const;
    [[nodiscard]] std::optional<domain::FocusProfile> findProfileByName(
        const std::string& name) const;
    [[nodiscard]] domain::FocusProfile updateProfile(domain::FocusProfile profile) const;
    void deleteProfile(const std::string& id) const;
    [[nodiscard]] std::vector<domain::FocusProfile> listProfiles(
        bool includeDeleted = false) const;

private:
    [[nodiscard]] static domain::FocusSession rowToSession(const storage::Statement& st);
    [[nodiscard]] static domain::DistractionItem rowToDistraction(const storage::Statement& st);
    [[nodiscard]] static domain::FocusProfile rowToProfile(const storage::Statement& st);
    [[nodiscard]] domain::FocusSession loadForTransition(const std::string& id) const;
    void writeSession(const domain::FocusSession& s) const;

    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
