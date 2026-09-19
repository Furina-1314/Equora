#include <equora/scheduling/Planner.h>

#include <equora/scheduling/Recurrence.h>

#include <algorithm>
#include <map>

namespace equora::scheduling {

using domain::Task;
using domain::TaskStatus;

void sortCandidates(std::vector<Task>& tasks, domain::UtcMillis now) {
    std::stable_sort(tasks.begin(), tasks.end(), [now](const Task& a, const Task& b) {
        // 1) 有截止且更近者先(逾期最优先);
        const auto da = a.dueAt.value_or(INT64_MAX);
        const auto db = b.dueAt.value_or(INT64_MAX);
        if (da != db) return da < db;
        // 2) 优先级高者先;
        if (a.priority != b.priority) return a.priority > b.priority;
        // 3) 估时长者先(大石先装)。
        return a.estimateMinutes.value_or(0) > b.estimateMinutes.value_or(0);
    });
}

namespace {

std::string localDateOf(domain::UtcMillis t, int tz) {
    return equora::scheduling::localDateString(t, tz);
}

} // namespace

std::vector<ProposedBlock> planWeek(const PlannerInput& input) {
    std::vector<ProposedBlock> out;

    auto candidates = input.tasks;
    sortCandidates(candidates, domain::utc::now());

    // 剩余估时(任务可能被拆成多块);按下标而非 id,避免未入库任务 id 冲突。
    std::vector<std::int64_t> remaining(candidates.size());
    for (std::size_t i = 0; i < candidates.size(); ++i) {
        remaining[i] = candidates[i].estimateMinutes.value_or(input.minBlockMinutes);
    }

    // 逐任务填充:每个任务在窗口空闲里找最早可用段。
    for (std::size_t ti = 0; ti < candidates.size(); ++ti) {
        const auto& task = candidates[ti];
        if (task.status == TaskStatus::Done || task.status == TaskStatus::Cancelled) {
            continue;
        }
        auto& left = remaining[ti];
        int piecesThisTask = 0;
        while (left > 0) {
            const auto slots = findFreeSlots(input.busy, input.windowFrom, input.windowTo,
                                             input.hours, input.minBlockMinutes, 0,
                                             input.tzOffsetMinutes);
            const Span* best = nullptr;
            for (const auto& s : slots) {
                // 起点不早于现在窗口内、且避开深夜(工作时段已由 findFreeSlots 保证)。
                (void)s;
            }
            // 选第一个长度足够的空闲段。
            for (const auto& slot : slots) {
                const std::int64_t lenMin = slot.minutes();
                if (lenMin >= input.minBlockMinutes) {
                    // 构造候选 span。
                    Span candidateSpan{};
                    candidateSpan.start = slot.start;
                    candidateSpan.end = slot.end;
                    best = &candidateSpan;
                    // 直接在此填充一块。
                    const std::int64_t take = std::min<std::int64_t>(
                        { left, input.maxBlockMinutes, lenMin });
                    ProposedBlock p;
                    p.taskId = task.id;
                    p.start = slot.start;
                    p.end = slot.start + take * 60'000;
                    p.reason = piecesThisTask == 0
                        ? "最早可用工作空闲段"
                        : "任务估时较长,拆分续排";
                    out.push_back(std::move(p));

                    // 占用该段,继续装剩余部分。
                    Span occupied{};
                    occupied.sourceId = "planned:" + task.id;
                    occupied.sourceType = "block";
                    occupied.title = task.title;
                    occupied.start = slot.start;
                    occupied.end = slot.start + take * 60'000;
                    const_cast<PlannerInput&>(input).busy.push_back(occupied);

                    left -= take;
                    ++piecesThisTask;
                    break;
                }
            }
            if (best == nullptr) break; // 无可用空闲段
        }
    }
    return out;
}

std::vector<DayLoad> dayLoads(const std::vector<Span>& busy, const PlannerInput& input) {
    std::map<std::string, std::int64_t> perDay;
    for (const auto& s : busy) {
        // 按起点归日(跨日块按比例分摊会更精细,这里保守按整天计入起点日)。
        perDay[localDateOf(s.start, input.tzOffsetMinutes)] += s.minutes();
    }

    std::vector<DayLoad> out;
    const std::int64_t capacity =
        (input.hours.endMinute - input.hours.startMinute);
    for (const auto& [date, minutes] : perDay) {
        DayLoad load;
        load.localDate = date;
        load.plannedMinutes = minutes;
        load.capacityMinutes = capacity;
        out.push_back(load);
    }
    return out;
}

std::vector<SplitSuggestion> suggestSplits(const PlannerInput& input) {
    std::vector<SplitSuggestion> out;
    for (const auto& t : input.tasks) {
        const auto estimate = t.estimateMinutes.value_or(0);
        if (estimate <= input.maxBlockMinutes) continue;
        if (t.status == TaskStatus::Done || t.status == TaskStatus::Cancelled) continue;

        SplitSuggestion s;
        s.taskId = t.id;
        s.title = t.title;
        s.totalEstimateMinutes = estimate;
        s.suggestedBlocks =
            static_cast<std::int32_t>((estimate + input.maxBlockMinutes - 1) /
                                      input.maxBlockMinutes);
        s.blockMinutes = static_cast<std::int32_t>(
            (estimate + s.suggestedBlocks - 1) / s.suggestedBlocks);
        out.push_back(std::move(s));
    }
    return out;
}

} // namespace equora::scheduling
