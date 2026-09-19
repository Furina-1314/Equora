#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include <equora/domain/Time.h>

namespace equora::scheduling {

// 估时校正:基于同任务历史有效专注时长(中位数),保留用户原始估计。
struct EstimateCorrection {
    std::string taskId;
    std::int32_t originalMinutes = 0;
    std::int32_t suggestedMinutes = 0; // = median(有效时长)
    std::int32_t sampleCount = 0;      // 样本(会话)数
    [[nodiscard]] bool hasSuggestion() const noexcept { return sampleCount >= 2; }
};

// 有效时长样本(由仓库从 focus_sessions 聚合同任务历史得出,单位分钟)。
[[nodiscard]] EstimateCorrection correctEstimate(const std::string& taskId,
                                                 std::int32_t originalMinutes,
                                                 std::vector<std::int64_t> effectiveMinutes);

} // namespace equora::scheduling
