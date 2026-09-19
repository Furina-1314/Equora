#include "CApiInternal.h"

#include <equora/core/FocusRepository.h>
#include <equora/domain/Focus.h>

// ---- 句柄定义(全局作用域,补全不透明类型) ----

struct EqFocusSessionHandle {
    equora::domain::FocusSession value;
    std::string taskBuffer;
    std::string blockBuffer;
    EqFocusSessionView view{};
    EqFocusSessionHandle(equora::domain::FocusSession s) : value(std::move(s)) { build(); }
    void build() {
        taskBuffer = value.taskId.value_or("");
        blockBuffer = value.blockId.value_or("");
        view.id = value.id.c_str();
        view.task_id = value.taskId.has_value() ? taskBuffer.c_str() : nullptr;
        view.block_id = value.blockId.has_value() ? blockBuffer.c_str() : nullptr;
        view.mode = static_cast<int32_t>(value.mode);
        view.planned_start = value.plannedStart;
        equora::capi::setOptionalView(view.planned_end, view.has_planned_end,
                                      value.plannedEnd);
        view.actual_start = value.actualStart;
        equora::capi::setOptionalView(view.actual_end, view.has_actual_end, value.actualEnd);
        view.paused_ms = value.pausedMs;
        view.state = static_cast<int32_t>(value.state);
        view.goal = value.goal.c_str();
        view.completion_note = value.completionNote.c_str();
        view.completion_level = value.completionLevel;
        view.created_at = value.createdAt;
        view.updated_at = value.updatedAt;
        view.revision = value.revision;
    }
};

struct EqFocusSessionList {
    std::vector<std::unique_ptr<EqFocusSessionHandle>> items;
};

struct EqInterruptionHandle {
    equora::domain::Interruption value;
    EqInterruptionView view{};
    EqInterruptionHandle(equora::domain::Interruption i) : value(std::move(i)) { build(); }
    void build() {
        view.id = value.id.c_str();
        view.session_id = value.sessionId.c_str();
        view.occurred_at = value.occurredAt;
        view.duration_ms = value.durationMs;
        view.reason = value.reason.c_str();
        view.source = value.source.c_str();
        view.handling = value.handling.c_str();
    }
};

struct EqInterruptionList {
    std::vector<std::unique_ptr<EqInterruptionHandle>> items;
};

struct EqDistractionHandle {
    equora::domain::DistractionItem value;
    std::string sessionBuffer;
    EqDistractionView view{};
    EqDistractionHandle(equora::domain::DistractionItem d) : value(std::move(d)) { build(); }
    void build() {
        sessionBuffer = value.sessionId.value_or("");
        view.id = value.id.c_str();
        view.session_id = value.sessionId.has_value() ? sessionBuffer.c_str() : nullptr;
        view.content = value.content.c_str();
        view.captured_at = value.capturedAt;
        view.resolution = value.resolution;
        view.resolved_ref = value.resolvedRef.c_str();
    }
};

struct EqDistractionList {
    std::vector<std::unique_ptr<EqDistractionHandle>> items;
};

struct EqFocusProfileHandle {
    equora::domain::FocusProfile value;
    EqFocusProfileView view{};
    EqFocusProfileHandle(equora::domain::FocusProfile p) : value(std::move(p)) { build(); }
    void build() {
        view.id = value.id.c_str();
        view.name = value.name.c_str();
        view.mode = static_cast<int32_t>(value.mode);
        view.planned_minutes = value.plannedMinutes;
        view.break_minutes = value.breakMinutes;
        view.allowed_apps = value.allowedApps.c_str();
        view.blocked_apps = value.blockedApps.c_str();
        view.allowed_sites = value.allowedSites.c_str();
        view.blocked_sites = value.blockedSites.c_str();
        view.notify_policy = value.notifyPolicy;
        view.is_default = value.isDefault ? 1 : 0;
        view.revision = value.revision;
    }
};

struct EqFocusProfileList {
    std::vector<std::unique_ptr<EqFocusProfileHandle>> items;
};

namespace {

using namespace equora;
using namespace equora::capi;

std::optional<std::string> maybe(const char* s) {
    return s != nullptr && s[0] != '\0' ? std::optional<std::string>(s) : std::nullopt;
}

domain::FocusProfile profileFromInput(const EqFocusProfileInput& in) {
    domain::FocusProfile p;
    p.id = in.id != nullptr ? in.id : "";
    p.name = in.name != nullptr ? in.name : "";
    p.mode = static_cast<domain::FocusMode>(in.mode);
    p.plannedMinutes = in.planned_minutes;
    p.breakMinutes = in.break_minutes;
    p.allowedApps = in.allowed_apps != nullptr ? in.allowed_apps : "[]";
    p.blockedApps = in.blocked_apps != nullptr ? in.blocked_apps : "[]";
    p.allowedSites = in.allowed_sites != nullptr ? in.allowed_sites : "[]";
    p.blockedSites = in.blocked_sites != nullptr ? in.blocked_sites : "[]";
    p.notifyPolicy = in.notify_policy;
    p.isDefault = in.is_default != 0;
    p.revision = in.revision;
    return p;
}

} // namespace

// ---- 会话 ----

const EqFocusSessionView* eq_focus_view(const EqFocusSessionHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_focus_handle_destroy(EqFocusSessionHandle* handle) { delete handle; }

int32_t eq_focus_start(EqCore* core, int32_t mode, int32_t planned_minutes,
                       const char* goal_utf8, const char* task_id_utf8,
                       const char* block_id_utf8, int64_t now,
                       EqFocusSessionHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_handle must not be null");
        }
        auto s = core->focus.start(static_cast<domain::FocusMode>(mode), planned_minutes,
                                   goal_utf8 != nullptr ? goal_utf8 : "",
                                   maybe(task_id_utf8), maybe(block_id_utf8), now);
        *out_handle = new EqFocusSessionHandle(std::move(s));
        return 0;
    });
}

int32_t eq_focus_find(EqCore* core, const char* id_utf8, EqFocusSessionHandle** out_handle,
                      EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->focus.find(id_utf8);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "focus session not found");
        }
        *out_handle = new EqFocusSessionHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_focus_open(EqCore* core, EqFocusSessionHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_handle must not be null");
        }
        auto found = core->focus.openSession();
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "no open focus session");
        }
        *out_handle = new EqFocusSessionHandle(std::move(*found));
        return 0;
    });
}

#define EQ_FOCUS_TRANSITION(name, call)                                                  \
    int32_t name(EqCore* core, const char* id_utf8, int64_t now,                         \
                 EqFocusSessionHandle** out_handle, EqError* out_error) {               \
        using namespace equora;                                                          \
        using namespace equora::capi;                                                    \
        return guard(out_error, [&]() -> int32_t {                                       \
            if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {        \
                return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument), \
                                 "core/id/out_handle must not be null");                 \
            }                                                                            \
            *out_handle = new EqFocusSessionHandle(core->focus.call(id_utf8, now));      \
            return 0;                                                                    \
        });                                                                              \
    }

EQ_FOCUS_TRANSITION(eq_focus_pause, pause)
EQ_FOCUS_TRANSITION(eq_focus_resume, resume)
EQ_FOCUS_TRANSITION(eq_focus_abandon, abandon)

int32_t eq_focus_complete(EqCore* core, const char* id_utf8, int64_t now,
                          const char* note_utf8, int32_t completion_level,
                          EqFocusSessionHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        *out_handle = new EqFocusSessionHandle(core->focus.complete(
            id_utf8, now, note_utf8 != nullptr ? note_utf8 : "", completion_level));
        return 0;
    });
}

int32_t eq_focus_history(EqCore* core, int64_t from, int64_t to, int32_t limit,
                         EqFocusSessionList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqFocusSessionList>();
        for (auto& s : core->focus.history(from, to, limit)) {
            list->items.push_back(std::make_unique<EqFocusSessionHandle>(std::move(s)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_focus_list_count(const EqFocusSessionList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqFocusSessionView* eq_focus_list_get(const EqFocusSessionList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_focus_list_destroy(EqFocusSessionList* list) { delete list; }

int32_t eq_focus_recover_interrupted(EqCore* core, int64_t now, int32_t* out_recovered,
                                     EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_recovered == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_recovered must not be null");
        }
        *out_recovered = core->focus.recoverInterrupted(now);
        return 0;
    });
}

// ---- 中断 ----

const EqInterruptionView* eq_interruption_view(const EqInterruptionHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_interruption_handle_destroy(EqInterruptionHandle* handle) { delete handle; }

int32_t eq_focus_add_interruption(EqCore* core, const char* session_id_utf8,
                                  int64_t occurred_at, int64_t duration_ms,
                                  const char* reason_utf8, const char* source_utf8,
                                  const char* handling_utf8,
                                  EqInterruptionHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || session_id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/session_id/out_handle must not be null");
        }
        auto it = core->focus.addInterruption(
            session_id_utf8, occurred_at, duration_ms,
            reason_utf8 != nullptr ? reason_utf8 : "",
            source_utf8 != nullptr ? source_utf8 : "manual",
            handling_utf8 != nullptr ? handling_utf8 : "");
        *out_handle = new EqInterruptionHandle(std::move(it));
        return 0;
    });
}

int32_t eq_focus_interruptions(EqCore* core, const char* session_id_utf8,
                               EqInterruptionList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || session_id_utf8 == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/session_id/out_list must not be null");
        }
        auto list = std::make_unique<EqInterruptionList>();
        for (auto& it : core->focus.interruptionsOf(session_id_utf8)) {
            list->items.push_back(std::make_unique<EqInterruptionHandle>(std::move(it)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_interruption_list_count(const EqInterruptionList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqInterruptionView* eq_interruption_list_get(const EqInterruptionList* list,
                                                   int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_interruption_list_destroy(EqInterruptionList* list) { delete list; }

// ---- 分心捕获 ----

const EqDistractionView* eq_distraction_view(const EqDistractionHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_distraction_handle_destroy(EqDistractionHandle* handle) { delete handle; }

int32_t eq_focus_capture(EqCore* core, const char* content_utf8, const char* session_id_utf8,
                         int64_t now, EqDistractionHandle** out_handle,
                         EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || content_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/content/out_handle must not be null");
        }
        auto d = core->focus.capture(content_utf8, maybe(session_id_utf8), now);
        *out_handle = new EqDistractionHandle(std::move(d));
        return 0;
    });
}

int32_t eq_focus_pending_distractions(EqCore* core, int32_t limit,
                                      EqDistractionList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqDistractionList>();
        for (auto& d : core->focus.pendingDistractions(limit)) {
            list->items.push_back(std::make_unique<EqDistractionHandle>(std::move(d)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_focus_resolve_distraction(EqCore* core, const char* id_utf8, int32_t resolution,
                                     const char* resolved_ref_utf8,
                                     EqDistractionHandle** out_handle,
                                     EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto d = core->focus.resolveDistraction(
            id_utf8, resolution, resolved_ref_utf8 != nullptr ? resolved_ref_utf8 : "");
        *out_handle = new EqDistractionHandle(std::move(d));
        return 0;
    });
}

int32_t eq_distraction_list_count(const EqDistractionList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqDistractionView* eq_distraction_list_get(const EqDistractionList* list,
                                                 int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_distraction_list_destroy(EqDistractionList* list) { delete list; }

// ---- 专注预设 ----

const EqFocusProfileView* eq_focus_profile_view(const EqFocusProfileHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_focus_profile_handle_destroy(EqFocusProfileHandle* handle) { delete handle; }

int32_t eq_focus_profile_create(EqCore* core, const EqFocusProfileInput* input,
                                EqFocusProfileHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = new EqFocusProfileHandle(
            core->focus.createProfile(profileFromInput(*input)));
        return 0;
    });
}

int32_t eq_focus_profile_find(EqCore* core, const char* id_utf8,
                              EqFocusProfileHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->focus.findProfile(id_utf8);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "focus profile not found");
        }
        *out_handle = new EqFocusProfileHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_focus_profile_update(EqCore* core, const EqFocusProfileInput* input,
                                EqFocusProfileHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || input->id == nullptr ||
            out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = new EqFocusProfileHandle(
            core->focus.updateProfile(profileFromInput(*input)));
        return 0;
    });
}

int32_t eq_focus_profile_delete(EqCore* core, const char* id_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        core->focus.deleteProfile(id_utf8);
        return 0;
    });
}

int32_t eq_focus_profile_list(EqCore* core, EqFocusProfileList** out_list,
                              EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqFocusProfileList>();
        for (auto& p : core->focus.listProfiles()) {
            list->items.push_back(std::make_unique<EqFocusProfileHandle>(std::move(p)));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_focus_profile_list_count(const EqFocusProfileList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqFocusProfileView* eq_focus_profile_list_get(const EqFocusProfileList* list,
                                                    int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_focus_profile_list_destroy(EqFocusProfileList* list) { delete list; }
