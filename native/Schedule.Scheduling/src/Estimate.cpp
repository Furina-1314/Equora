#include <equora/scheduling/Estimate.h>

#include <algorithm>

namespace equora::scheduling {

EstimateCorrection correctEstimate(const std::string& taskId,
                                   std::int32_t originalMinutes,
                                   std::vector<std::int64_t> effectiveMinutes) {
    EstimateCorrection out;
    out.taskId = taskId;
    out.originalMinutes = originalMinutes;

    // 过滤明显异常样本(<1 分钟 或 >24 小时)。
    std::vector<std::int64_t> samples;
    for (auto m : effectiveMinutes) {
        if (m >= 1 && m <= 24 * 60) samples.push_back(m);
    }
    out.sampleCount = static_cast<std::int32_t>(samples.size());
    if (out.sampleCount < 2) return out;

    std::sort(samples.begin(), samples.end());
    const std::size_t n = samples.size();
    out.suggestedMinutes = static_cast<std::int32_t>(
        n % 2 == 1 ? samples[n / 2] : (samples[n / 2 - 1] + samples[n / 2]) / 2);
    return out;
}

} // namespace equora::scheduling
