#include "CApiInternal.h"

#include <equora/core/ReviewRepository.h>
#include <equora/core/TaskRepository.h>
#include <equora/scheduling/Estimate.h>
#include <equora/scheduling/Planner.h>
#include <equora/scheduling/Recurrence.h>

// ---- 句柄(全局作用域) ----

struct EqProposedList {
    std::vector<equora::scheduling::ProposedBlock> items;
    std::vector<EqProposedView> views;
    std::vector<std::unique_ptr<std::string>> strings;
};

struct EqLoadList {
    std::vector<EqDayLoadView> items;
};

struct EqSplitList {
    std::vector<EqSplitView> items;
};

struct EqAutomationHandle {
    equora::core::AutomationRule value;
    EqAutomationView view{};
    EqAutomationHandle(equora::core::AutomationRule r) : value(std::move(r)) { build(); }
    void build() {
        view.id = value.id.c_str();
        view.name = value.name.c_str();
        view.trigger = value.trigger.c_str();
        view.conditions = value.conditions.c_str();
        view.actions = value.actions.c_str();
        view.enabled = value.enabled ? 1 : 0;
        view.revision = value.revision;
    }
};

struct EqAutomationList {
    std::vector<std::unique_ptr<EqAutomationHandle>> items;
};

struct EqIdList {
    std::vector<std::string> ids;
};

namespace {

using namespace equora;
using namespace equora::capi;

equora::scheduling::WorkHours makeHours(int32_t start, int32_t end, int32_t mask) {
    equora::scheduling::WorkHours h;
    h.startMinute = start;
    h.endMinute = end;
    h.workdays.clear();
    for (int bit = 0; bit < 7; ++bit) {
        if ((mask & (1 << bit)) != 0) h.workdays.push_back(bit);
    }
    return h;
}

core::ReviewRepository& reviewOf(EqCore* core) {
    // 复用 focus 仓库的连接;ReviewRepository 无状态,每次构造轻量。
    thread_local std::string lastDevice;
    lastDevice = "shared";
    static thread_local std::unique_ptr<core::ReviewRepository> repo;
    // EqCore 生命周期由调用方保证;仓库仅持引用,构造代价低。
    repo = std::make_unique<core::ReviewRepository>(core->db, "device");
    return *repo;
}

core::AutomationRepository& automationOf(EqCore* core) {
    static thread_local std::unique_ptr<core::AutomationRepository> repo;
    repo = std::make_unique<core::AutomationRepository>(core->db, "device");
    return *repo;
}

} // namespace

// ---- 排程 ----

int32_t eq_plan_week(EqCore* core, int64_t from, int64_t to, int32_t work_start_minute,
                     int32_t work_end_minute, int32_t workday_mask, int32_t max_block_minutes,
                     int32_t tz_offset_minutes, EqProposedList** out_list,
                     EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }

        scheduling::PlannerInput in;
        in.windowFrom = from;
        in.windowTo = to;
        in.hours = makeHours(work_start_minute, work_end_minute, workday_mask);
        in.maxBlockMinutes = max_block_minutes > 0 ? max_block_minutes : 120;
        in.tzOffsetMinutes = tz_offset_minutes;

        // 候选:未完成且有估时(或无截止且无估时的用最短块兜底由引擎处理)。
        for (const auto& t : core->tasks.listAll()) {
            if (t.status == domain::TaskStatus::Done ||
                t.status == domain::TaskStatus::Cancelled) {
                continue;
            }
            in.tasks.push_back(t);
        }
        in.busy = core->calendar.materializeWindow(from, to, tz_offset_minutes);

        auto plan = scheduling::planWeek(in);
        auto list = std::make_unique<EqProposedList>();
        for (auto& p : plan) {
            list->strings.push_back(std::make_unique<std::string>(p.taskId));
            list->strings.push_back(std::make_unique<std::string>(p.reason));
        }
        list->views.reserve(plan.size());
        for (std::size_t i = 0; i < plan.size(); ++i) {
            EqProposedView v{};
            v.task_id = list->strings[i * 2]->c_str();
            v.start = plan[i].start;
            v.end = plan[i].end;
            v.reason = list->strings[i * 2 + 1]->c_str();
            list->views.push_back(v);
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_proposed_count(const EqProposedList* list) {
    return list != nullptr ? static_cast<int32_t>(list->views.size()) : 0;
}

const EqProposedView* eq_proposed_get(const EqProposedList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->views.size()) {
        return nullptr;
    }
    return &list->views[static_cast<std::size_t>(index)];
}

void eq_proposed_list_destroy(EqProposedList* list) { delete list; }

int32_t eq_day_loads(EqCore* core, int64_t from, int64_t to, int32_t work_start_minute,
                     int32_t work_end_minute, int32_t workday_mask, int32_t tz_offset_minutes,
                     EqLoadList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        scheduling::PlannerInput in;
        in.windowFrom = from;
        in.windowTo = to;
        in.hours = makeHours(work_start_minute, work_end_minute, workday_mask);
        in.tzOffsetMinutes = tz_offset_minutes;
        in.busy = core->calendar.materializeWindow(from, to, tz_offset_minutes);

        auto list = std::make_unique<EqLoadList>();
        for (const auto& load : scheduling::dayLoads(in.busy, in)) {
            EqDayLoadView v{};
            std::snprintf(v.local_date, sizeof(v.local_date), "%.10s",
                          load.localDate.c_str());
            v.planned_minutes = load.plannedMinutes;
            v.capacity_minutes = load.capacityMinutes;
            v.overloaded = load.overloaded() ? 1 : 0;
            list->items.push_back(v);
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_load_count(const EqLoadList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqDayLoadView* eq_load_get(const EqLoadList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)];
}

void eq_load_list_destroy(EqLoadList* list) { delete list; }

int32_t eq_suggest_splits(EqCore* core, int64_t from, int64_t to, int32_t max_block_minutes,
                          EqSplitList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        scheduling::PlannerInput in;
        in.windowFrom = from;
        in.windowTo = to;
        in.maxBlockMinutes = max_block_minutes > 0 ? max_block_minutes : 120;
        for (const auto& t : core->tasks.listAll()) {
            if (t.status != domain::TaskStatus::Done &&
                t.status != domain::TaskStatus::Cancelled) {
                in.tasks.push_back(t);
            }
        }

        auto list = std::make_unique<EqSplitList>();
        for (const auto& s : scheduling::suggestSplits(in)) {
            EqSplitView v{};
            std::snprintf(v.task_id, sizeof(v.task_id), "%.63s", s.taskId.c_str());
            std::snprintf(v.title, sizeof(v.title), "%.127s", s.title.c_str());
            v.total_minutes = s.totalEstimateMinutes;
            v.blocks = s.suggestedBlocks;
            v.block_minutes = s.blockMinutes;
            list->items.push_back(v);
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_split_count(const EqSplitList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqSplitView* eq_split_get(const EqSplitList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)];
}

void eq_split_list_destroy(EqSplitList* list) { delete list; }

int32_t eq_correct_estimate(EqCore* core, const char* task_id_utf8, int32_t* out_suggested,
                            int32_t* out_samples, int32_t* out_has, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || out_suggested == nullptr ||
            out_samples == nullptr || out_has == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/out_* must not be null");
        }
        auto task = core->tasks.findById(task_id_utf8);
        if (!task.has_value()) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "task not found");
        }
        // 同任务历史有效时长(分钟)。
        std::vector<std::int64_t> samples;
        for (const auto& s : core->focus.history(0, domain::utc::now())) {
            if (s.taskId == task->id && s.state == domain::SessionState::Completed) {
                samples.push_back(s.effectiveMs(domain::utc::now()) / 60'000);
            }
        }
        auto c = scheduling::correctEstimate(task->id,
                                             task->estimateMinutes.value_or(0), samples);
        *out_suggested = c.suggestedMinutes;
        *out_samples = c.sampleCount;
        *out_has = c.hasSuggestion() ? 1 : 0;
        return 0;
    });
}

// ---- 复盘 ----

int32_t eq_review_daily_compute(EqCore* core, int64_t day_start_utc, int32_t tz_offset_minutes,
                                EqDailyOut* out, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out must not be null");
        }
        auto d = reviewOf(core).computeDaily(day_start_utc, core->tasks, core->calendar,
                                             core->focus, tz_offset_minutes);
        out->completed = d.completedCount;
        out->deferred = d.deferredCount;
        out->cancelled = d.cancelledCount;
        out->big_three_done = d.bigThreeDone;
        out->distraction_count = d.distractionCount;
        out->planned_minutes = d.plannedMinutes;
        out->actual_minutes = d.actualMinutes;
        return 0;
    });
}

int32_t eq_review_daily_save(EqCore* core, int64_t day_start_utc, int32_t tz_offset_minutes,
                             const char* notes_utf8, const char* focus_tomorrow_utf8,
                             EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        auto d = reviewOf(core).computeDaily(day_start_utc, core->tasks, core->calendar,
                                             core->focus, tz_offset_minutes);
        d.notes = notes_utf8 != nullptr ? notes_utf8 : "";
        d.focusTomorrow = focus_tomorrow_utf8 != nullptr ? focus_tomorrow_utf8 : "";
        reviewOf(core).saveDaily(d);
        return 0;
    });
}

int32_t eq_review_weekly_compute(EqCore* core, int64_t week_start_utc,
                                 int32_t tz_offset_minutes, EqWeeklyOut* out,
                                 EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out must not be null");
        }
        auto w = reviewOf(core).computeWeekly(week_start_utc, core->tasks, core->calendar,
                                              core->focus, tz_offset_minutes);
        out->deep_work_minutes = w.deepWorkMinutes;
        out->focus_ratio = w.focusRatio;
        out->estimate_accuracy = w.estimateAccuracy;
        out->best_focus_hour = w.bestFocusHour;
        return 0;
    });
}

int32_t eq_review_weekly_save(EqCore* core, int64_t week_start_utc,
                              int32_t tz_offset_minutes, const char* notes_utf8,
                              const char* next_week_goals_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        auto w = reviewOf(core).computeWeekly(week_start_utc, core->tasks, core->calendar,
                                              core->focus, tz_offset_minutes);
        w.notes = notes_utf8 != nullptr ? notes_utf8 : "";
        w.nextWeekGoals = next_week_goals_utf8 != nullptr ? next_week_goals_utf8 : "";
        reviewOf(core).saveWeekly(w);
        return 0;
    });
}

// ---- 自动化 ----

const EqAutomationView* eq_automation_view(const EqAutomationHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_automation_handle_destroy(EqAutomationHandle* handle) { delete handle; }

namespace {

core::AutomationRule ruleFromInput(const EqAutomationInput& in) {
    core::AutomationRule r;
    r.id = in.id != nullptr ? in.id : "";
    r.name = in.name != nullptr ? in.name : "";
    r.trigger = in.trigger != nullptr ? in.trigger : "";
    r.conditions = in.conditions != nullptr ? in.conditions : "{}";
    r.actions = in.actions != nullptr ? in.actions : "";
    r.enabled = in.enabled != 0;
    r.revision = in.revision;
    return r;
}

} // namespace

int32_t eq_automation_create(EqCore* core, const EqAutomationInput* input,
                             EqAutomationHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = new EqAutomationHandle(
            automationOf(core).create(ruleFromInput(*input)));
        return 0;
    });
}

int32_t eq_automation_find(EqCore* core, const char* id_utf8,
                           EqAutomationHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = automationOf(core).find(id_utf8);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "automation rule not found");
        }
        *out_handle = new EqAutomationHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_automation_update(EqCore* core, const EqAutomationInput* input,
                             EqAutomationHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || input->id == nullptr ||
            out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = new EqAutomationHandle(
            automationOf(core).update(ruleFromInput(*input)));
        return 0;
    });
}

int32_t eq_automation_set_enabled(EqCore* core, const char* id_utf8, int32_t enabled,
                                  EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        automationOf(core).setEnabled(id_utf8, enabled != 0);
        return 0;
    });
}

int32_t eq_automation_delete(EqCore* core, const char* id_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        automationOf(core).remove(id_utf8);
        return 0;
    });
}

int32_t eq_automation_list(EqCore* core, EqAutomationList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqAutomationList>();
        for (auto& r : automationOf(core).listEnabled()) {
            list->items.push_back(std::make_unique<EqAutomationHandle>(std::move(r)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_automation_list_count(const EqAutomationList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqAutomationView* eq_automation_list_get(const EqAutomationList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_automation_list_destroy(EqAutomationList* list) { delete list; }

int32_t eq_automation_evaluate(EqCore* core, const char* trigger_utf8,
                               const char* task_id_utf8, EqIdList** out_list,
                               EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || trigger_utf8 == nullptr || task_id_utf8 == nullptr ||
            out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/trigger/task_id/out_list must not be null");
        }
        auto task = core->tasks.findById(task_id_utf8);
        if (!task.has_value()) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "task not found");
        }
        auto hits = automationOf(core).evaluate(trigger_utf8, *task);
        auto list = std::make_unique<EqIdList>(EqIdList{std::move(hits)});
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_id_list_count(const EqIdList* list) {
    return list != nullptr ? static_cast<int32_t>(list->ids.size()) : 0;
}

const char* eq_id_list_get(const EqIdList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->ids.size()) {
        return nullptr;
    }
    return list->ids[static_cast<std::size_t>(index)].c_str();
}

void eq_id_list_destroy(EqIdList* list) { delete list; }
