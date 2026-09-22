#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Calendar.h>
#include <equora/domain/CalendarEvent.h>
#include <equora/domain/RecurrenceRule.h>
#include <equora/domain/TimeBlock.h>
#include <equora/scheduling/Conflict.h>
#include <equora/storage/Database.h>

namespace equora::core {

// 日历聚合仓库:日历、时间块、日程、重复规则,以及窗口查询/例外编辑。
// 与任务仓库共用一个 Database 连接。
class CalendarRepository {
public:
    CalendarRepository(const storage::Database& db, std::string deviceId);

    // ---- 日历 ----
    [[nodiscard]] domain::Calendar createCalendar(domain::Calendar draft) const;
    [[nodiscard]] std::vector<domain::Calendar> listCalendars(bool includeDeleted = false) const;

    // ---- 时间块 ----
    // 创建:start<end 由迁移 CHECK 保证,此处先校验;taskId 可空。
    [[nodiscard]] domain::TimeBlock createBlock(domain::TimeBlock draft) const;
    [[nodiscard]] std::optional<domain::TimeBlock> findBlock(const std::string& id,
                                                             bool includeDeleted = false) const;
    [[nodiscard]] domain::TimeBlock update(domain::TimeBlock block, bool ownTransaction = true) const; // 乐观并发
    void deleteBlock(const std::string& id) const;                         // 软删除
    // 与窗口 [from,to) 相交的未删除块(start < to AND end > from)。
    [[nodiscard]] std::vector<domain::TimeBlock> blocksInRange(domain::UtcMillis from,
                                                               domain::UtcMillis to) const;
    [[nodiscard]] std::vector<domain::TimeBlock> blocksForTask(const std::string& taskId) const;

    // ---- 日程(事件) ----
    [[nodiscard]] domain::CalendarEvent createEvent(domain::CalendarEvent draft) const;
    [[nodiscard]] std::optional<domain::CalendarEvent> findEvent(const std::string& id,
                                                                 bool includeDeleted = false) const;
    [[nodiscard]] domain::CalendarEvent update(domain::CalendarEvent ev) const;
    void deleteEvent(const std::string& id) const;
    [[nodiscard]] std::vector<domain::CalendarEvent> eventsInRange(domain::UtcMillis from,
                                                                   domain::UtcMillis to) const;

    // ---- 重复规则 ----
    [[nodiscard]] domain::RecurrenceRule createRule(domain::RecurrenceRule draft) const;
    [[nodiscard]] std::optional<domain::RecurrenceRule> findRule(const std::string& id) const;
    [[nodiscard]] domain::RecurrenceRule update(domain::RecurrenceRule rule) const;
    void deleteRule(const std::string& id) const;
    [[nodiscard]] std::vector<domain::RecurrenceRule> rulesForHost(
        std::string_view hostType, const std::string& hostId) const;

    // ---- 窗口物化 ----
    // 物化窗口:实际块 + 实际事件 + 规则展开实例(排除例外日期)。
    // tzOffsetMinutes 用于展开与例外;返回的 Span 序列可直接做冲突/空闲计算。
    [[nodiscard]] std::vector<scheduling::Span> materializeWindow(
        domain::UtcMillis from, domain::UtcMillis to, int tzOffsetMinutes) const;

    // ---- 例外编辑(重复事件系列) ----
    // 仅修改本次:物化该实例为真实事件,并把该本地日期加入规则例外。
    // occurrenceStartUtc 为展开实例的起点;找不到实例抛 NotFound。
    [[nodiscard]] domain::CalendarEvent detachOccurrence(const domain::RecurrenceRule& rule,
                                                         domain::UtcMillis occurrenceStartUtc,
                                                         int tzOffsetMinutes) const;
    // 修改本次及以后:原规则 until 设为该实例(不含),并基于该实例创建新规则。
    // 返回新规则。
    [[nodiscard]] domain::RecurrenceRule splitSeries(const domain::RecurrenceRule& rule,
                                                     domain::UtcMillis occurrenceStartUtc) const;

private:
    [[nodiscard]] static domain::TimeBlock rowToBlock(const storage::Statement& st);
    [[nodiscard]] static domain::CalendarEvent rowToEvent(const storage::Statement& st);
    [[nodiscard]] static domain::RecurrenceRule rowToRule(const storage::Statement& st);

    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
