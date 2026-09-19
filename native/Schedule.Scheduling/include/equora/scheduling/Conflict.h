#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include <equora/domain/Time.h>

namespace equora::scheduling {

// 参与冲突/空闲计算的忙碌区间(时间块、事件或重复实例展开后)。
struct Span {
    std::string sourceId;   // 记录 id 或 ruleId+序号
    std::string sourceType; // "block" | "event" | "recurring"
    std::string title;
    std::string taskId;     // 可空
    domain::UtcMillis start = 0;
    domain::UtcMillis end = 0;

    [[nodiscard]] std::int64_t minutes() const noexcept { return (end - start) / 60'000; }
};

struct Conflict {
    Span a;
    Span b;
    std::int64_t overlapMinutes = 0;
};

// 找出所有重叠对(含准备/缓冲后的有效区间;调用方负责把 prepare/buffer 并入)。
[[nodiscard]] std::vector<Conflict> detectConflicts(const std::vector<Span>& spans);

} // namespace equora::scheduling
