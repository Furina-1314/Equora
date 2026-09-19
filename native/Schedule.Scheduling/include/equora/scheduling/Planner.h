#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Task.h>
#include <equora/domain/Time.h>
#include <equora/scheduling/Conflict.h>
#include <equora/scheduling/FreeSlot.h>

namespace equora::scheduling {

// 自动排程建议(未落库;差异预览由 UI 呈现,用户确认后才创建时间块)。
struct ProposedBlock {
    std::string taskId;
    domain::UtcMillis start = 0;
    domain::UtcMillis end = 0;
    std::string reason; // 可解释理由,如"截止前最后一个 90 分钟空闲段"
};

// 过载报告:某本地日计划总时长超出工作时长。
struct DayLoad {
    std::string localDate; // YYYY-MM-DD
    std::int64_t plannedMinutes = 0;
    std::int64_t capacityMinutes = 0;
    [[nodiscard]] bool overloaded() const noexcept { return plannedMinutes > capacityMinutes; }
};

// 排程输入:候选任务(按紧迫/优先级排序由调用方或 sortCandidates 完成)。
struct PlannerInput {
    std::vector<domain::Task> tasks;
    std::vector<Span> busy;       // 已占用(块/事件/展开实例)
    domain::UtcMillis windowFrom = 0;
    domain::UtcMillis windowTo = 0;
    WorkHours hours;
    std::int64_t minBlockMinutes = 25;  // 最短建议块
    std::int64_t maxBlockMinutes = 120; // 单块上限(超过建议拆分)
    int tzOffsetMinutes = 0;
};

// 确定性规则引擎:按候选顺序在空闲时间顺次填充。
// 规则:1) 只用工作时段空闲段;2) 块长 = min(剩余估时, 空闲段, maxBlock);
// 3) 估时超过 maxBlock 的任务拆多块(同日或跨日);4) 每条建议附理由。
[[nodiscard]] std::vector<ProposedBlock> planWeek(const PlannerInput& input);

// 候选排序(确定性):逾期/截止近 → 优先级高 → 估时长。
void sortCandidates(std::vector<domain::Task>& tasks, domain::UtcMillis now);

// 过载识别:窗口内按本地日聚合 busy 时长 vs 工作容量。
[[nodiscard]] std::vector<DayLoad> dayLoads(const std::vector<Span>& busy,
                                            const PlannerInput& input);

// 任务拆分建议:估时 > maxBlock 的任务 → 建议块数与单块时长。
struct SplitSuggestion {
    std::string taskId;
    std::string title;
    std::int32_t totalEstimateMinutes = 0;
    std::int32_t suggestedBlocks = 0;
    std::int32_t blockMinutes = 0;
};
[[nodiscard]] std::vector<SplitSuggestion> suggestSplits(const PlannerInput& input);

} // namespace equora::scheduling
