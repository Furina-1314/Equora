#include <equora/capi/equora_capi.h>

#include <cstring>
#include <memory>
#include <string>
#include <vector>

#include <equora/core/TaskRepository.h>
#include <equora/domain/Error.h>
#include <equora/domain/Task.h>
#include <equora/storage/Database.h>
#include <equora/storage/Migrations.h>

// 不透明句柄的完整定义:补全头文件中声明的类型,不放入匿名命名空间,
// 以免与全局 typedef 形成歧义。
struct EqCore {
    equora::storage::Database db;
    equora::core::TaskRepository repo;

    EqCore(equora::storage::Database&& d, std::string deviceId)
        : db(std::move(d)), repo(db, std::move(deviceId)) {}
};

struct EqTaskHandle {
    equora::domain::Task task;
    EqTaskView view{}; // 指向 task 内字符串,构造时一次性填充

    explicit EqTaskHandle(equora::domain::Task t) : task(std::move(t)) { buildView(); }

    void buildView() {
        using equora::domain::Task;
        const Task& t = task;
        view.id = t.id.c_str();
        view.title = t.title.c_str();
        view.note = t.note.c_str();
        view.status = static_cast<int32_t>(t.status);
        view.priority = static_cast<int32_t>(t.priority);
        view.importance = t.importance;
        view.due_at = t.dueAt.value_or(0);
        view.has_due = t.dueAt.has_value() ? 1 : 0;
        view.estimate_minutes = t.estimateMinutes.value_or(0);
        view.has_estimate = t.estimateMinutes.has_value() ? 1 : 0;
        view.actual_minutes = t.actualMinutes;
        view.project_id = t.projectId.has_value() ? t.projectId->c_str() : nullptr;
        view.has_project = t.projectId.has_value() ? 1 : 0;
        view.created_at = t.createdAt;
        view.updated_at = t.updatedAt;
        view.revision = t.revision;
        view.deleted_at = t.deletedAt.value_or(0);
        view.has_deleted = t.deletedAt.has_value() ? 1 : 0;
        view.last_device_id = t.lastDeviceId.c_str();
    }
};

struct EqTaskList {
    std::vector<std::unique_ptr<EqTaskHandle>> items;
};

namespace {

using equora::domain::ErrorCode;
using equora::domain::EquoraError;
using equora::domain::Task;
using equora::domain::UtcMillis;

// 把任何异常转换为错误码并填充 EqError(超长截断,保证 NUL 结尾)。
int32_t fillError(EqError* out, int32_t code, const char* message) {
    if (out != nullptr) {
        out->code = code;
        const std::size_t len =
            message != nullptr ? std::strlen(message) : 0;
        const std::size_t n = len < sizeof(out->message) - 1 ? len : sizeof(out->message) - 1;
        if (n > 0) std::memcpy(out->message, message, n);
        out->message[n] = '\0';
    }
    return code;
}

int32_t translateException(EqError* out, const std::exception& e) {
    int32_t code = static_cast<int32_t>(ErrorCode::Unknown);
    if (const auto* ee = dynamic_cast<const EquoraError*>(&e)) {
        code = static_cast<int32_t>(ee->code());
    }
    return fillError(out, code, e.what());
}

// 边界包装:捕获一切异常(含 bad_alloc),绝不外泄。
template <typename Fn>
int32_t guard(EqError* out_error, Fn&& fn) noexcept {
    try {
        return fn();
    } catch (const std::exception& e) {
        return translateException(out_error, e);
    } catch (...) {
        return fillError(out_error, static_cast<int32_t>(ErrorCode::Unknown),
                         "unknown native error");
    }
}

EqTaskHandle* makeHandle(Task t) { return new EqTaskHandle(std::move(t)); }

Task inputToTask(const EqTaskInput& in) {
    using equora::domain::Priority;
    using equora::domain::TaskStatus;
    Task t;
    t.id = in.id != nullptr ? in.id : "";
    t.title = in.title != nullptr ? in.title : "";
    t.note = in.note != nullptr ? in.note : "";
    t.status = static_cast<TaskStatus>(in.status);
    t.priority = static_cast<Priority>(in.priority);
    t.importance = in.importance;
    t.dueAt = in.has_due != 0 ? std::optional<UtcMillis>(in.due_at) : std::nullopt;
    t.estimateMinutes =
        in.has_estimate != 0 ? std::optional<std::int32_t>(in.estimate_minutes) : std::nullopt;
    t.actualMinutes = in.actual_minutes;
    t.projectId = (in.has_project != 0 && in.project_id != nullptr)
                      ? std::optional<std::string>(in.project_id)
                      : std::nullopt;
    t.revision = in.revision;
    return t;
}

} // namespace

int32_t eq_api_version() { return EQUORA_CAPI_VERSION; }

const char* eq_version_string() { return "Equora native core 0.1.0 (capi v2)"; }

int32_t eq_ping(int32_t value) { return value + 1; }

EqCore* eq_core_create(const char* db_path_utf8, const char* device_id_utf8,
                       EqError* out_error) {
    try {
        if (db_path_utf8 == nullptr) {
            fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                      "db_path_utf8 must not be null");
            return nullptr;
        }
        auto db = equora::storage::Database::open(db_path_utf8);
        equora::storage::applyMigrations(db);
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
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_version == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_version must not be null");
        }
        *out_version = equora::storage::currentSchemaVersion(core->db);
        return 0;
    });
}

const EqTaskView* eq_task_view(const EqTaskHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_task_handle_destroy(EqTaskHandle* handle) { delete handle; }

int32_t eq_task_create(EqCore* core, const EqTaskInput* input, EqTaskHandle** out_handle,
                       EqError* out_error) {
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = makeHandle(core->repo.create(inputToTask(*input)));
        return 0;
    });
}

int32_t eq_task_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                    EqTaskHandle** out_handle, EqError* out_error) {
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->repo.findById(id_utf8, include_deleted != 0);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "task not found");
        }
        *out_handle = makeHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_task_update(EqCore* core, const EqTaskInput* input, EqTaskHandle** out_handle,
                       EqError* out_error) {
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        if (input->id == nullptr || input->id[0] == '\0') {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "update requires task id");
        }
        *out_handle = makeHandle(core->repo.update(inputToTask(*input)));
        return 0;
    });
}

int32_t eq_task_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                            EqTaskHandle** out_handle, EqError* out_error) {
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        *out_handle = makeHandle(core->repo.setDeleted(id_utf8, deleted != 0));
        return 0;
    });
}

int32_t eq_task_list_all(EqCore* core, int32_t include_deleted, EqTaskList** out_list,
                         EqError* out_error) {
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqTaskList>();
        for (Task& t : core->repo.listAll(include_deleted != 0)) {
            list->items.push_back(std::unique_ptr<EqTaskHandle>(makeHandle(std::move(t))));
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
