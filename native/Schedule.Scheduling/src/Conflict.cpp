#include <equora/scheduling/Conflict.h>

#include <algorithm>

namespace equora::scheduling {

std::vector<Conflict> detectConflicts(const std::vector<Span>& spans) {
    std::vector<const Span*> sorted;
    sorted.reserve(spans.size());
    for (const auto& s : spans) sorted.push_back(&s);
    std::sort(sorted.begin(), sorted.end(),
              [](const Span* a, const Span* b) { return a->start < b->start; });

    std::vector<Conflict> out;
    for (std::size_t i = 0; i < sorted.size(); ++i) {
        for (std::size_t j = i + 1; j < sorted.size(); ++j) {
            if (sorted[j]->start >= sorted[i]->end) break; // 已按起点排序
            const auto overlap = std::min(sorted[i]->end, sorted[j]->end) - sorted[j]->start;
            if (overlap <= 0) continue;
            out.push_back(Conflict{*sorted[i], *sorted[j], overlap / 60'000});
        }
    }
    return out;
}

} // namespace equora::scheduling
