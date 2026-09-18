#include <equora/core/TaskQuery.h>

namespace equora::core {

using domain::TaskStatus;
using domain::utc::localDayIndex;
using domain::utc::localDayStartUtc;

TaskFilter SmartLists::inbox() {
    TaskFilter f;
    f.statuses = {TaskStatus::Inbox};
    f.sort = TaskSort::CreatedDesc;
    return f;
}

TaskFilter SmartLists::today(domain::UtcMillis nowMs, int offsetMinutes) {
    TaskFilter f;
    f.dueState = TaskFilter::DueSet;
    f.dueAfter = localDayStartUtc(localDayIndex(nowMs, offsetMinutes), offsetMinutes);
    f.dueBefore = *f.dueAfter + 86'400'000;
    f.sort = TaskSort::DueAsc;
    return f;
}

TaskFilter SmartLists::upcoming(domain::UtcMillis nowMs, int offsetMinutes, int days) {
    TaskFilter f;
    f.dueState = TaskFilter::DueSet;
    f.dueAfter = nowMs;
    const auto dayStart = localDayStartUtc(localDayIndex(nowMs, offsetMinutes), offsetMinutes);
    f.dueBefore = dayStart + static_cast<std::int64_t>(days) * 86'400'000;
    f.sort = TaskSort::DueAsc;
    return f;
}

TaskFilter SmartLists::overdue(domain::UtcMillis nowMs) {
    TaskFilter f;
    f.dueState = TaskFilter::DueSet;
    f.dueBefore = nowMs;
    f.excludedStatuses = {TaskStatus::Done, TaskStatus::Cancelled};
    f.sort = TaskSort::DueAsc;
    return f;
}

TaskFilter SmartLists::noDate() {
    TaskFilter f;
    f.dueState = TaskFilter::DueUnset;
    f.sort = TaskSort::PriorityDesc;
    return f;
}

TaskFilter SmartLists::scheduled() {
    TaskFilter f;
    f.dueState = TaskFilter::DueSet;
    f.sort = TaskSort::DueAsc;
    return f;
}

TaskFilter SmartLists::waiting() {
    TaskFilter f;
    f.statuses = {TaskStatus::Waiting};
    f.sort = TaskSort::UpdatedDesc;
    return f;
}

TaskFilter SmartLists::completed() {
    TaskFilter f;
    f.statuses = {TaskStatus::Done, TaskStatus::Cancelled};
    f.sort = TaskSort::UpdatedDesc;
    return f;
}

TaskFilter SmartLists::completedToday(domain::UtcMillis nowMs, int offsetMinutes) {
    TaskFilter f = completed();
    f.updatedAfter = localDayStartUtc(localDayIndex(nowMs, offsetMinutes), offsetMinutes);
    f.updatedBefore = *f.updatedAfter + 86'400'000;
    return f;
}

} // namespace equora::core
