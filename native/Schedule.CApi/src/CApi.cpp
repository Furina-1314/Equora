#include "CApiInternal.h"

#include <equora/storage/Migrations.h>

// ---- EqTaskHandle 视图构建(全局作用域,与不透明类型定义一致) ----

void EqTaskHandle::buildView() {
    const equora::domain::Task& t = task;
    view.id = t.id.c_str();
    view.title = t.title.c_str();
    view.note = t.note.c_str();
    view.status = static_cast<int32_t>(t.status);
    view.priority = static_cast<int32_t>(t.priority);
    view.importance = t.importance;
    equora::capi::setOptionalView(view.due_at, view.has_due, t.dueAt);
    view.has_estimate = t.estimateMinutes.has_value() ? 1 : 0;
    view.estimate_minutes = t.estimateMinutes.value_or(0);
    view.actual_minutes = t.actualMinutes;
    view.project_id = t.projectId.has_value() ? t.projectId->c_str() : nullptr;
    view.has_project = t.projectId.has_value() ? 1 : 0;
    view.created_at = t.createdAt;
    view.updated_at = t.updatedAt;
    view.revision = t.revision;
    equora::capi::setOptionalView(view.deleted_at, view.has_deleted, t.deletedAt);
    view.last_device_id = t.lastDeviceId.c_str();
}

namespace equora::capi {

EqTaskHandle* makeTaskHandle(domain::Task t) { return new EqTaskHandle(std::move(t)); }

} // namespace equora::capi

int32_t eq_api_version() { return EQUORA_CAPI_VERSION; }

const char* eq_version_string() { return "Equora native core 1.0.0 (capi v3)"; }

int32_t eq_ping(int32_t value) { return value + 1; }

EqCore* eq_core_create(const char* db_path_utf8, const char* device_id_utf8,
                       EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    try {
        if (db_path_utf8 == nullptr) {
            fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                      "db_path_utf8 must not be null");
            return nullptr;
        }
        auto db = storage::Database::open(db_path_utf8);
        storage::applyMigrations(db);
        return new EqCore(std::move(db),
                          device_id_utf8 != nullptr ? device_id_utf8 : "local");
    } catch (const std::exception& e) {
        translateException(out_error, e);
        return nullptr;
    } catch (...) {
        fillError(out_error, static_cast<int32_t>(ErrorCode::Unknown), "unknown native error");
        return nullptr;
    }
}

void eq_core_destroy(EqCore* core) { delete core; }

int32_t eq_core_schema_version(const EqCore* core, int32_t* out_version,
                               EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_version == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_version must not be null");
        }
        *out_version = storage::currentSchemaVersion(core->db);
        return 0;
    });
}

const EqTaskView* eq_task_view(const EqTaskHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_task_handle_destroy(EqTaskHandle* handle) { delete handle; }

namespace {

// 输入 DTO → 领域任务(创建/更新共用)。
equora::domain::Task taskFromInput(const EqTaskInput& input) {
    using namespace equora;
    domain::Task t;
    t.id = input.id != nullptr ? input.id : "";
    t.title = input.title != nullptr ? input.title : "";
    t.note = input.note != nullptr ? input.note : "";
    t.status = static_cast<domain::TaskStatus>(input.status);
    t.priority = static_cast<domain::Priority>(input.priority);
    t.importance = input.importance;
    t.dueAt = input.has_due != 0 ? std::optional<domain::UtcMillis>(input.due_at)
                                 : std::nullopt;
    t.estimateMinutes = input.has_estimate != 0
                            ? std::optional<std::int32_t>(input.estimate_minutes)
                            : std::nullopt;
    t.actualMinutes = input.actual_minutes;
    t.projectId = (input.has_project != 0 && input.project_id != nullptr)
                      ? std::optional<std::string>(input.project_id)
                      : std::nullopt;
    t.revision = input.revision;
    return t;
}

} // namespace

int32_t eq_task_create(EqCore* core, const EqTaskInput* input, EqTaskHandle** out_handle,
                       EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = makeTaskHandle(core->tasks.create(taskFromInput(*input)));
        return 0;
    });
}

int32_t eq_task_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                    EqTaskHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->tasks.findById(id_utf8, include_deleted != 0);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "task not found");
        }
        *out_handle = makeTaskHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_task_update(EqCore* core, const EqTaskInput* input, EqTaskHandle** out_handle,
                       EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        if (input->id == nullptr || input->id[0] == '\0') {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "update requires task id");
        }
        *out_handle = makeTaskHandle(core->tasks.update(taskFromInput(*input)));
        return 0;
    });
}

int32_t eq_task_permanently_delete(EqCore* core, const char* id_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (!core || !id_utf8) return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument), "core/id must not be null");
        core->tasks.permanentlyDelete(id_utf8);
        return 0;
    });
}

int32_t eq_task_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                            EqTaskHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        *out_handle = makeTaskHandle(core->tasks.setDeleted(id_utf8, deleted != 0));
        return 0;
    });
}

int32_t eq_task_list_all(EqCore* core, int32_t include_deleted, EqTaskList** out_list,
                         EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqTaskList>();
        for (domain::Task& t : core->tasks.listAll(include_deleted != 0)) {
            list->items.push_back(std::unique_ptr<EqTaskHandle>(makeTaskHandle(std::move(t))));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_task_list_count(const EqTaskList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqTaskView* eq_task_list_get(const EqTaskList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_task_list_destroy(EqTaskList* list) { delete list; }
