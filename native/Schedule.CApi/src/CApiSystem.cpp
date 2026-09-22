#include "CApiInternal.h"

#include <filesystem>
#include <fstream>

#include <equora/common/Logger.h>
#include <equora/core/Transfer.h>
#include <equora/storage/AppMeta.h>
#include <equora/storage/Backup.h>

namespace equora::capi {

core::TaskFilter filterFromAbi(const EqTaskFilter& f) {
    core::TaskFilter out;
    out.dueState = f.due_state;
    // after/before 独立可选:值为 0 视为未设置(0 = 1970 纪元,实际过滤场景不会用到)。
    out.dueAfter = f.due_after != 0 ? std::optional<domain::UtcMillis>(f.due_after)
                                    : std::nullopt;
    out.dueBefore = f.due_before != 0 ? std::optional<domain::UtcMillis>(f.due_before)
                                      : std::nullopt;
    out.updatedAfter =
        f.updated_after != 0 ? std::optional<domain::UtcMillis>(f.updated_after) : std::nullopt;
    out.updatedBefore = f.updated_before != 0
                            ? std::optional<domain::UtcMillis>(f.updated_before)
                            : std::nullopt;
    out.projectId = f.project_id != nullptr ? std::optional<std::string>(f.project_id)
                                            : std::nullopt;
    out.tagId = f.tag_id != nullptr ? std::optional<std::string>(f.tag_id) : std::nullopt;
    out.searchText = f.search != nullptr ? f.search : "";
    for (int32_t i = 0; i < f.status_count && f.statuses != nullptr; ++i) {
        out.statuses.push_back(static_cast<domain::TaskStatus>(f.statuses[i]));
    }
    for (int32_t i = 0; i < f.excluded_count && f.excluded_statuses != nullptr; ++i) {
        out.excludedStatuses.push_back(
            static_cast<domain::TaskStatus>(f.excluded_statuses[i]));
    }
    out.includeDeleted = f.include_deleted != 0;
    out.sort = static_cast<core::TaskSort>(f.sort);
    out.limit = f.limit;
    out.offset = f.offset;
    return out;
}

} // namespace equora::capi

int32_t eq_task_query(EqCore* core, const EqTaskFilter* filter, EqTaskList** out_list,
                      EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || filter == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/filter/out_list must not be null");
        }
        auto list = std::make_unique<EqTaskList>();
        for (domain::Task& t : core->tasks.query(filterFromAbi(*filter))) {
            list->items.push_back(std::unique_ptr<EqTaskHandle>(makeTaskHandle(std::move(t))));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_core_device_id(EqCore* core, EqStringHandle** out, EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || out == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out must not be null");
        }
        *out = new EqStringHandle(storage::getOrCreateDeviceId(core->db));
        return 0;
    });
}

int32_t eq_log_init(const char* file_path_utf8, int32_t level) {
    using namespace equora;
    using namespace equora::capi;
    return guard(nullptr, [&]() -> int32_t {
        if (file_path_utf8 == nullptr) {
            return static_cast<int32_t>(ErrorCode::InvalidArgument);
        }
        common::logInit(pathFromUtf8(file_path_utf8),
                        static_cast<common::LogLevel>(level < 0 ? 0 : (level > 4 ? 4 : level)));
        return 0;
    });
}

int32_t eq_log_shutdown() {
    using namespace equora;
    common::logShutdown();
    return 0;
}

int32_t eq_backup_create(EqCore* core, const char* dir_utf8, EqStringHandle** out_path,
                         EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || dir_utf8 == nullptr || out_path == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/dir/out_path must not be null");
        }
        const storage::BackupInfo info =
            storage::BackupManager::create(core->db, pathFromUtf8(dir_utf8));
        *out_path = new EqStringHandle(utf8FromPath(info.file));
        return 0;
    });
}

int32_t eq_backup_verify(const char* path_utf8, int32_t* out_ok, EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (path_utf8 == nullptr || out_ok == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "path/out_ok must not be null");
        }
        std::string why;
        *out_ok = storage::BackupManager::verify(pathFromUtf8(path_utf8), &why) ? 1 : 0;
        return 0;
    });
}

int32_t eq_backup_restore(EqCore* core, const char* path_utf8, EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || path_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/path must not be null");
        }
        storage::BackupManager::restore(core->db, pathFromUtf8(path_utf8));
        return 0;
    });
}

namespace equora::capi {

void writeTextFileOrThrow(const std::filesystem::path& path, const std::string& content) {
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out) {
        throw domain::EquoraError(domain::ErrorCode::IoError,
                                  "cannot write: " + utf8FromPath(path));
    }
    out.write(content.data(), static_cast<std::streamsize>(content.size()));
    out.flush();
    if (!out) {
        throw domain::EquoraError(domain::ErrorCode::IoError,
                                  "write failed: " + utf8FromPath(path));
    }
}

std::string readTextFileOrThrow(const std::filesystem::path& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) {
        throw domain::EquoraError(domain::ErrorCode::IoError,
                                  "cannot read: " + utf8FromPath(path));
    }
    return std::string((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
}

} // namespace equora::capi

int32_t eq_export_tasks_json(EqCore* core, const char* path_utf8, int32_t* out_count,
                             EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || path_utf8 == nullptr || out_count == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/path/out_count must not be null");
        }
        *out_count = static_cast<int32_t>(core->tasks.listAll().size());
        writeTextFileOrThrow(pathFromUtf8(path_utf8), core::exportTasksJson(core->tasks));
        return 0;
    });
}

int32_t eq_export_tasks_csv(EqCore* core, const char* path_utf8, int32_t* out_count,
                            EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || path_utf8 == nullptr || out_count == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/path/out_count must not be null");
        }
        *out_count = static_cast<int32_t>(core->tasks.listAll().size());
        writeTextFileOrThrow(pathFromUtf8(path_utf8), core::exportTasksCsv(core->tasks));
        return 0;
    });
}

int32_t eq_import_tasks_json(EqCore* core, const char* path_utf8, int32_t* out_imported,
                             int32_t* out_skipped, EqError* out_error) {
    return equora::capi::guard(out_error, [&]() -> int32_t {
        using namespace equora;
        using namespace equora::capi;
        if (core == nullptr || path_utf8 == nullptr || out_imported == nullptr ||
            out_skipped == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/path/out_imported/out_skipped must not be null");
        }
        const std::string text = readTextFileOrThrow(pathFromUtf8(path_utf8));
        const core::ImportResult result = core::importTasksJson(core->tasks, text);
        *out_imported = result.imported;
        *out_skipped = result.skipped;
        return 0;
    });
}
