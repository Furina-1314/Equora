#pragma once

#include <string>

#include <equora/core/CalendarRepository.h>

namespace equora::core {

// ICS(RFC 5545 子集)导入导出。
// 导出:窗口内事件 + 任务时间块(BEGIN:VCALENDAR 包装,CRLF,UTC 时间带 Z)。
// 导入:VEVENT 的 UID/SUMMARY/DESCRIPTION/LOCATION/DTSTART/DTEND/RRULE;
//       按 UID 幂等(已存在跳过);RRULE 解析失败的事件仍导入(无重复)。
[[nodiscard]] std::string exportIcs(const CalendarRepository& repo,
                                    domain::UtcMillis from, domain::UtcMillis to);

struct IcsImportResult {
    int imported = 0;
    int skipped = 0;
    int failed = 0; // 结构非法的 VEVENT
};

[[nodiscard]] IcsImportResult importIcs(const CalendarRepository& repo,
                                        std::string_view icsText);

} // namespace equora::core
