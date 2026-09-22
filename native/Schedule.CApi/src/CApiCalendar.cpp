#include "CApiInternal.h"
#include <fstream>

#include <equora/core/CalendarRepository.h>
#include <equora/core/TransferIcs.h>
#include <equora/domain/Calendar.h>
#include <equora/domain/CalendarEvent.h>
#include <equora/domain/RecurrenceRule.h>
#include <equora/domain/TimeBlock.h>
#include <equora/scheduling/Conflict.h>
#include <equora/scheduling/FreeSlot.h>
#include <equora/scheduling/Recurrence.h>

#include <cstring>

// ---- 句柄定义(全局作用域,补全头文件不透明类型) ----

struct EqCalendarHandle {
    equora::domain::Calendar value;
    EqCalendarView view{};
    EqCalendarHandle(equora::domain::Calendar c) : value(std::move(c)) { build(); }
    void build() {
        view.id = value.id.c_str();
        view.name = value.name.c_str();
        view.color = value.color.c_str();
        view.source = value.source.c_str();
        view.is_visible = value.isVisible ? 1 : 0;
        view.created_at = value.createdAt;
        view.updated_at = value.updatedAt;
        view.revision = value.revision;
    }
};

struct EqCalendarList {
    std::vector<std::unique_ptr<EqCalendarHandle>> items;
};

struct EqBlockHandle {
    equora::domain::TimeBlock value;
    EqBlockView view{};
    std::string taskBuffer; // task_id 视图指针来源
    EqBlockHandle(equora::domain::TimeBlock b) : value(std::move(b)) { build(); }
    void build() {
        view.id = value.id.c_str();
        if (value.taskId.has_value()) {
            taskBuffer = *value.taskId;
            view.task_id = taskBuffer.c_str();
        } else {
            taskBuffer.clear();
            view.task_id = nullptr;
        }
        view.calendar_id = value.calendarId.c_str();
        view.start_at = value.startAt;
        view.end_at = value.endAt;
        view.prepare_minutes = value.prepareMinutes;
        view.buffer_minutes = value.bufferMinutes;
        view.actual_minutes = value.actualMinutes;
        view.note = value.note.c_str();
        view.title = value.title.c_str();
        view.batch_id = value.batchId.c_str();
        view.created_at = value.createdAt;
        view.updated_at = value.updatedAt;
        view.revision = value.revision;
        equora::capi::setOptionalView(view.deleted_at, view.has_deleted, value.deletedAt);
    }
};

struct EqBlockList {
    std::vector<std::unique_ptr<EqBlockHandle>> items;
};

struct EqEventHandle {
    equora::domain::CalendarEvent value;
    EqEventView view{};
    EqEventHandle(equora::domain::CalendarEvent e) : value(std::move(e)) { build(); }
    void build() {
        view.id = value.id.c_str();
        view.title = value.title.c_str();
        view.location = value.location.c_str();
        view.note = value.note.c_str();
        view.calendar_id = value.calendarId.c_str();
        view.start_at = value.startAt;
        view.end_at = value.endAt;
        view.is_all_day = value.isAllDay ? 1 : 0;
        view.created_at = value.createdAt;
        view.updated_at = value.updatedAt;
        view.revision = value.revision;
        equora::capi::setOptionalView(view.deleted_at, view.has_deleted, value.deletedAt);
    }
};

struct EqEventList {
    std::vector<std::unique_ptr<EqEventHandle>> items;
};

struct EqRuleHandle {
    equora::domain::RecurrenceRule value;
    std::string byCsv;
    std::string excludedCsv;
    std::string rruleText;
    EqRuleView view{};
    EqRuleHandle(equora::domain::RecurrenceRule r) : value(std::move(r)) { build(); }
    void build() {
        byCsv.clear();
        for (std::size_t i = 0; i < value.byWeekday.size(); ++i) {
            if (i > 0) byCsv.push_back(',');
            byCsv += std::to_string(static_cast<int>(value.byWeekday[i]));
        }
        excludedCsv.clear();
        for (std::size_t i = 0; i < value.excludedDates.size(); ++i) {
            if (i > 0) excludedCsv.push_back(',');
            excludedCsv += value.excludedDates[i];
        }
        rruleText = value.toRruleString();

        view.id = value.id.c_str();
        view.host_type = value.hostType.c_str();
        view.host_id = value.hostId.c_str();
        view.freq = static_cast<int32_t>(value.freq);
        view.interval = value.interval;
        view.by_weekday = byCsv.c_str();
        view.month_mode = static_cast<int32_t>(value.monthMode);
        view.month_nth = value.monthNth;
        view.month_weekday = static_cast<int32_t>(value.monthWeekday);
        equora::capi::setOptionalView(view.until_utc, view.has_until, value.untilUtc);
        equora::capi::setOptionalView(view.max_count, view.has_max_count, value.maxCount);
        view.complete_recur_days = value.completeRecurDays;
        view.excluded_dates = excludedCsv.c_str();
        view.rrule_text = rruleText.c_str();
        view.revision = value.revision;
    }
};

struct EqRuleList {
    std::vector<std::unique_ptr<EqRuleHandle>> items;
};

namespace {

using namespace equora;
using namespace equora::capi;

std::vector<std::string> splitCsv(std::string_view csv) {
    std::vector<std::string> out;
    std::size_t pos = 0;
    while (pos < csv.size()) {
        const auto next = csv.find(',', pos);
        const auto part =
            csv.substr(pos, next == std::string_view::npos ? csv.size() - pos : next - pos);
        if (!part.empty()) out.emplace_back(part);
        if (next == std::string_view::npos) break;
        pos = next + 1;
    }
    return out;
}

domain::TimeBlock blockFromInput(const EqBlockInput& in) {
    domain::TimeBlock b;
    b.id = in.id != nullptr ? in.id : "";
    b.taskId = in.task_id != nullptr && in.task_id[0] != '\0'
                   ? std::optional<std::string>(in.task_id)
                   : std::nullopt;
    b.calendarId = in.calendar_id != nullptr ? in.calendar_id : "";
    b.startAt = in.start_at;
    b.endAt = in.end_at;
    b.prepareMinutes = in.prepare_minutes;
    b.bufferMinutes = in.buffer_minutes;
    b.actualMinutes = in.actual_minutes;
    b.note = in.note != nullptr ? in.note : "";
    b.revision = in.revision;
    b.title = in.title != nullptr ? in.title : "";
    b.batchId = in.batch_id != nullptr ? in.batch_id : "";
    return b;
}

domain::CalendarEvent eventFromInput(const EqEventInput& in) {
    domain::CalendarEvent e;
    e.id = in.id != nullptr ? in.id : "";
    e.title = in.title != nullptr ? in.title : "";
    e.location = in.location != nullptr ? in.location : "";
    e.note = in.note != nullptr ? in.note : "";
    e.calendarId = in.calendar_id != nullptr ? in.calendar_id : "";
    e.startAt = in.start_at;
    e.endAt = in.end_at;
    e.isAllDay = in.is_all_day != 0;
    e.revision = in.revision;
    return e;
}

domain::RecurrenceRule ruleFromInput(const EqRuleInput& in) {
    domain::RecurrenceRule r;
    r.id = in.id != nullptr ? in.id : "";
    r.hostType = in.host_type != nullptr ? in.host_type : "";
    r.hostId = in.host_id != nullptr ? in.host_id : "";
    r.freq = static_cast<domain::RecurFreq>(in.freq);
    r.interval = in.interval;
    for (const auto& token : splitCsv(in.by_weekday != nullptr ? in.by_weekday : "")) {
        r.byWeekday.push_back(static_cast<domain::Weekday>(atoi(token.c_str())));
    }
    r.monthMode = static_cast<domain::MonthMode>(in.month_mode);
    r.monthNth = in.month_nth;
    r.monthWeekday = static_cast<domain::Weekday>(in.month_weekday);
    r.untilUtc = in.has_until != 0 ? std::optional<domain::UtcMillis>(in.until_utc)
                                   : std::nullopt;
    r.maxCount = in.has_max_count != 0 ? std::optional<std::int64_t>(in.max_count)
                                       : std::nullopt;
    r.completeRecurDays = in.complete_recur_days;
    r.excludedDates = splitCsv(in.excluded_dates != nullptr ? in.excluded_dates : "");
    r.revision = in.revision;
    return r;
}

} // namespace

// ---- 日历 ----

const EqCalendarView* eq_calendar_view(const EqCalendarHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_calendar_handle_destroy(EqCalendarHandle* handle) { delete handle; }

int32_t eq_calendar_create(EqCore* core, const char* name_utf8, const char* color_utf8,
                           EqCalendarHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || name_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/name/out_handle must not be null");
        }
        auto draft = domain::Calendar::draft(name_utf8);
        draft.color = color_utf8 != nullptr ? color_utf8 : "";
        *out_handle = new EqCalendarHandle(core->calendar.createCalendar(std::move(draft)));
        return 0;
    });
}

int32_t eq_calendar_list(EqCore* core, EqCalendarList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqCalendarList>();
        for (auto& c : core->calendar.listCalendars()) {
            list->items.push_back(std::make_unique<EqCalendarHandle>(std::move(c)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_calendar_list_count(const EqCalendarList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqCalendarView* eq_calendar_list_get(const EqCalendarList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_calendar_list_destroy(EqCalendarList* list) { delete list; }

// ---- 时间块 ----

int32_t eq_block_apply_batch(EqCore* core, const EqBlockInput* inputs, int32_t count,
                             int32_t deleted, int32_t affect_tasks, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (!core || !inputs || count <= 0 || count > 10000)
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument), "invalid batch");
        auto tx = core->db.beginTransaction();
        for (int32_t i = 0; i < count; ++i) {
            auto block = blockFromInput(inputs[i]);
            if (affect_tasks && block.taskId.has_value()) {
                if (deleted) (void)core->tasks.setDeleted(*block.taskId, true, false);
                else {
                    auto task = core->tasks.findById(*block.taskId);
                    if (!task) throw domain::EquoraError(ErrorCode::NotFound, "linked task not found");
                    task->title = block.title;
                    (void)core->tasks.update(*task, false);
                }
            }
            if (deleted) {
                auto st = core->db.prepare("UPDATE time_blocks SET deleted_at = ?, updated_at = ?, revision = revision + 1 WHERE id = ? AND revision = ? AND deleted_at IS NULL");
                const auto now = domain::utc::now();
                st.bind(1, now).bind(2, now).bind(3, block.id).bind(4, block.revision);
                st.step();
                if (core->db.changes() != 1) throw domain::EquoraError(ErrorCode::Conflict, "time block changed; refresh before retry");
            } else {
                (void)core->calendar.update(std::move(block), false);
            }
        }
        tx.commit();
        return 0;
    });
}

const EqBlockView* eq_block_view(const EqBlockHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_block_handle_destroy(EqBlockHandle* handle) { delete handle; }

int32_t eq_block_create(EqCore* core, const EqBlockInput* input, EqBlockHandle** out_handle,
                        EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = new EqBlockHandle(core->calendar.createBlock(blockFromInput(*input)));
        return 0;
    });
}

int32_t eq_block_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                     EqBlockHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->calendar.findBlock(id_utf8, include_deleted != 0);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "block not found");
        }
        *out_handle = new EqBlockHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_block_update(EqCore* core, const EqBlockInput* input, EqBlockHandle** out_handle,
                        EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || input->id == nullptr ||
            out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = new EqBlockHandle(core->calendar.update(blockFromInput(*input)));
        return 0;
    });
}

int32_t eq_block_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                             EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        if (deleted != 0) core->calendar.deleteBlock(id_utf8);
        return 0;
    });
}

int32_t eq_block_list_range(EqCore* core, int64_t from, int64_t to, EqBlockList** out_list,
                            EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqBlockList>();
        for (auto& b : core->calendar.blocksInRange(from, to)) {
            list->items.push_back(std::make_unique<EqBlockHandle>(std::move(b)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_block_list_for_task(EqCore* core, const char* task_id_utf8, EqBlockList** out_list,
                               EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/out_list must not be null");
        }
        auto list = std::make_unique<EqBlockList>();
        for (auto& b : core->calendar.blocksForTask(task_id_utf8)) {
            list->items.push_back(std::make_unique<EqBlockHandle>(std::move(b)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_block_list_count(const EqBlockList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqBlockView* eq_block_list_get(const EqBlockList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_block_list_destroy(EqBlockList* list) { delete list; }

// ---- 事件 ----

const EqEventView* eq_event_view(const EqEventHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_event_handle_destroy(EqEventHandle* handle) { delete handle; }

int32_t eq_event_create(EqCore* core, const EqEventInput* input, EqEventHandle** out_handle,
                        EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = new EqEventHandle(core->calendar.createEvent(eventFromInput(*input)));
        return 0;
    });
}

int32_t eq_event_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                     EqEventHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->calendar.findEvent(id_utf8, include_deleted != 0);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "event not found");
        }
        *out_handle = new EqEventHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_event_update(EqCore* core, const EqEventInput* input, EqEventHandle** out_handle,
                        EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || input->id == nullptr ||
            out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = new EqEventHandle(core->calendar.update(eventFromInput(*input)));
        return 0;
    });
}

int32_t eq_event_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                             EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        if (deleted != 0) core->calendar.deleteEvent(id_utf8);
        return 0;
    });
}

int32_t eq_event_list_range(EqCore* core, int64_t from, int64_t to, EqEventList** out_list,
                            EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqEventList>();
        for (auto& e : core->calendar.eventsInRange(from, to)) {
            list->items.push_back(std::make_unique<EqEventHandle>(std::move(e)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_event_list_count(const EqEventList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqEventView* eq_event_list_get(const EqEventList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_event_list_destroy(EqEventList* list) { delete list; }

// ---- 重复规则 ----

const EqRuleView* eq_rule_view(const EqRuleHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_rule_handle_destroy(EqRuleHandle* handle) { delete handle; }

int32_t eq_rule_create(EqCore* core, const EqRuleInput* input, EqRuleHandle** out_handle,
                       EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = new EqRuleHandle(core->calendar.createRule(ruleFromInput(*input)));
        return 0;
    });
}

int32_t eq_rule_get(EqCore* core, const char* id_utf8, EqRuleHandle** out_handle,
                    EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->calendar.findRule(id_utf8);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "rule not found");
        }
        *out_handle = new EqRuleHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_rule_update(EqCore* core, const EqRuleInput* input, EqRuleHandle** out_handle,
                       EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || input->id == nullptr ||
            out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = new EqRuleHandle(core->calendar.update(ruleFromInput(*input)));
        return 0;
    });
}

int32_t eq_rule_delete(EqCore* core, const char* id_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        core->calendar.deleteRule(id_utf8);
        return 0;
    });
}

int32_t eq_rule_list_for_host(EqCore* core, const char* host_type_utf8,
                              const char* host_id_utf8, EqRuleList** out_list,
                              EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || host_type_utf8 == nullptr || host_id_utf8 == nullptr ||
            out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/host_type/host_id/out_list must not be null");
        }
        auto list = std::make_unique<EqRuleList>();
        for (auto& r : core->calendar.rulesForHost(host_type_utf8, host_id_utf8)) {
            list->items.push_back(std::make_unique<EqRuleHandle>(std::move(r)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_rule_list_count(const EqRuleList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqRuleView* eq_rule_list_get(const EqRuleList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_rule_list_destroy(EqRuleList* list) { delete list; }

// ---- 窗口物化、冲突、空闲 ----

namespace {

struct EqSpanListImpl {
    std::vector<scheduling::Span> spans;
    std::vector<std::unique_ptr<std::string>> strings; // 视图指针的宿主
};

void fillSpanView(EqSpanView& view, const scheduling::Span& s, EqSpanListImpl& owner) {
    auto pin = [&](std::string_view text) {
        owner.strings.push_back(std::make_unique<std::string>(text));
        return owner.strings.back()->c_str();
    };
    view.source_id = pin(s.sourceId);
    view.source_type = pin(s.sourceType);
    view.title = pin(s.title);
    view.task_id = s.taskId.empty() ? nullptr : pin(s.taskId);
    view.start = s.start;
    view.end = s.end;
}

} // namespace

struct EqSpanList {
    EqSpanListImpl impl;
    std::vector<EqSpanView> views;
};

int32_t eq_window_spans(EqCore* core, int64_t from, int64_t to, int32_t tz_offset_minutes,
                        EqSpanList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto spans = core->calendar.materializeWindow(from, to, tz_offset_minutes);
        auto list = std::make_unique<EqSpanList>();
        list->views.reserve(spans.size());
        for (const auto& s : spans) {
            EqSpanView v{};
            fillSpanView(v, s, list->impl);
            list->views.push_back(v);
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_span_list_count(const EqSpanList* list) {
    return list != nullptr ? static_cast<int32_t>(list->views.size()) : 0;
}

const EqSpanView* eq_span_list_get(const EqSpanList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->views.size()) {
        return nullptr;
    }
    return &list->views[static_cast<std::size_t>(index)];
}

void eq_span_list_destroy(EqSpanList* list) { delete list; }

struct EqConflictList {
    std::vector<scheduling::Conflict> conflicts;
    std::unique_ptr<EqSpanListImpl> impl{std::make_unique<EqSpanListImpl>()};
    std::vector<EqConflictView> views;
};

int32_t eq_window_conflicts(EqCore* core, int64_t from, int64_t to, int32_t tz_offset_minutes,
                            EqConflictList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto spans = core->calendar.materializeWindow(from, to, tz_offset_minutes);
        auto conflicts = scheduling::detectConflicts(spans);

        auto list = std::make_unique<EqConflictList>();
        list->views.reserve(conflicts.size());
        for (const auto& c : conflicts) {
            EqConflictView v{};
            fillSpanView(v.a, c.a, *list->impl);
            fillSpanView(v.b, c.b, *list->impl);
            v.overlap_minutes = c.overlapMinutes;
            list->views.push_back(v);
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_conflict_list_count(const EqConflictList* list) {
    return list != nullptr ? static_cast<int32_t>(list->views.size()) : 0;
}

const EqConflictView* eq_conflict_list_get(const EqConflictList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->views.size()) {
        return nullptr;
    }
    return &list->views[static_cast<std::size_t>(index)];
}

void eq_conflict_list_destroy(EqConflictList* list) { delete list; }

struct EqSlotList {
    std::vector<scheduling::FreeSlot> slots;
};

int32_t eq_find_free_slots(EqCore* core, int64_t from, int64_t to, int32_t work_start_minute,
                           int32_t work_end_minute, int32_t workday_mask, int64_t min_minutes,
                           int32_t limit, EqSlotList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        scheduling::WorkHours hours;
        hours.startMinute = work_start_minute;
        hours.endMinute = work_end_minute;
        hours.workdays.clear();
        for (int bit = 0; bit < 7; ++bit) {
            if ((workday_mask & (1 << bit)) != 0) hours.workdays.push_back(bit);
        }

        auto spans = core->calendar.materializeWindow(from, to, 0);
        auto found = scheduling::findFreeSlots(spans, from, to, hours, min_minutes,
                                               static_cast<std::size_t>(limit));
        auto list = std::make_unique<EqSlotList>(EqSlotList{std::move(found)});
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_slot_list_count(const EqSlotList* list) {
    return list != nullptr ? static_cast<int32_t>(list->slots.size()) : 0;
}

const EqSlotView* eq_slot_list_get(const EqSlotList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->slots.size()) {
        return nullptr;
    }
    // 视图按需返回稳定地址:直接存放于 list 内的 vector 不会重排。
    return reinterpret_cast<const EqSlotView*>(&list->slots[static_cast<std::size_t>(index)]);
}

void eq_slot_list_destroy(EqSlotList* list) { delete list; }

// ---- 例外编辑 ----

int32_t eq_event_detach_occurrence(EqCore* core, const char* rule_id_utf8,
                                   int64_t occurrence_start_utc, int32_t tz_offset_minutes,
                                   EqEventHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || rule_id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/rule_id/out_handle must not be null");
        }
        auto rule = core->calendar.findRule(rule_id_utf8);
        if (!rule.has_value()) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "rule not found");
        }
        *out_handle = new EqEventHandle(core->calendar.detachOccurrence(
            *rule, occurrence_start_utc, tz_offset_minutes));
        return 0;
    });
}

int32_t eq_rule_split_series(EqCore* core, const char* rule_id_utf8,
                             int64_t occurrence_start_utc, EqRuleHandle** out_handle,
                             EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || rule_id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/rule_id/out_handle must not be null");
        }
        auto rule = core->calendar.findRule(rule_id_utf8);
        if (!rule.has_value()) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "rule not found");
        }
        *out_handle = new EqRuleHandle(
            core->calendar.splitSeries(*rule, occurrence_start_utc));
        return 0;
    });
}

// ---- ICS ----

int32_t eq_export_ics(EqCore* core, int64_t from, int64_t to, const char* path_utf8,
                      int32_t* out_count, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || path_utf8 == nullptr || out_count == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/path/out_count must not be null");
        }
        const std::string text = core::exportIcs(core->calendar, from, to);
        std::ofstream out(pathFromUtf8(path_utf8), std::ios::binary | std::ios::trunc);
        if (!out) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::IoError),
                             "cannot write ics file");
        }
        out.write(text.data(), static_cast<std::streamsize>(text.size()));
        *out_count = 1;
        return 0;
    });
}

int32_t eq_import_ics(EqCore* core, const char* path_utf8, int32_t* out_imported,
                      int32_t* out_skipped, int32_t* out_failed, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || path_utf8 == nullptr || out_imported == nullptr ||
            out_skipped == nullptr || out_failed == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/path/out_* must not be null");
        }
        std::ifstream in(pathFromUtf8(path_utf8), std::ios::binary);
        if (!in) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::IoError),
                             "cannot read ics file");
        }
        const std::string text((std::istreambuf_iterator<char>(in)),
                               std::istreambuf_iterator<char>());
        const auto result = core::importIcs(core->calendar, text);
        *out_imported = result.imported;
        *out_skipped = result.skipped;
        *out_failed = result.failed;
        return 0;
    });
}
