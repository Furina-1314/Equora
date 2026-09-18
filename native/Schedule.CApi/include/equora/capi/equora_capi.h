// Equora 稳定 C ABI。
//
// 约定(违反任何一条都视为破坏性变更,必须提升 EQUORA_CAPI_VERSION):
//  - 只使用 C 类型、不透明句柄、UTF-8 字符串与固定布局 DTO;
//  - 不跨边界抛出或返回 C++ 对象/STL/异常;
//  - 原生侧分配的缓冲区只能由原生侧的显式 destroy/free 函数释放;
//  - 每个导出函数在内部捕获所有 C++ 异常,转换为错误码 + EqError;
//  - 字符串指针的生命周期在其所属句柄内有效,不得在 destroy 后使用。
#ifndef EQUORA_CAPI_H
#define EQUORA_CAPI_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#if defined(EQUORA_CAPI_BUILD)
#define EQUORA_API __declspec(dllexport)
#else
#define EQUORA_API __declspec(dllimport)
#endif
#else
#define EQUORA_API __attribute__((visibility("default")))
#endif

#define EQUORA_CAPI_VERSION 2

// 错误对象:调用方栈分配;code 与 domain::ErrorCode 取值一致。
typedef struct EqError {
    int32_t code;
    char message[256]; // NUL 结尾 UTF-8,超长截断
} EqError;

// 任务输入(创建/更新)。字符串只读,调用方保留所有权;可空字段用 has_x 标志。
typedef struct EqTaskInput {
    const char* id;    // 创建时可 NULL(自动生成);更新时必填
    const char* title; // 必填
    const char* note;  // 可 NULL → ""
    int32_t status;
    int32_t priority;
    int32_t importance;
    int64_t due_at;
    int32_t has_due;
    int32_t estimate_minutes;
    int32_t has_estimate;
    int32_t actual_minutes;
    const char* project_id; // has_project 为 0 时忽略
    int32_t has_project;
    int64_t revision; // 更新时的乐观并发基线
} EqTaskInput;

// 任务输出视图:所有指针指向句柄内部存储,句柄销毁后失效。
typedef struct EqTaskView {
    const char* id;
    const char* title;
    const char* note;
    int32_t status;
    int32_t priority;
    int32_t importance;
    int64_t due_at;
    int32_t has_due;
    int32_t estimate_minutes;
    int32_t has_estimate;
    int32_t actual_minutes;
    const char* project_id;
    int32_t has_project;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
    int64_t deleted_at;
    int32_t has_deleted;
    const char* last_device_id;
} EqTaskView;

// ---- 基础 ----

int32_t EQUORA_API eq_api_version(void);
EQUORA_API const char* eq_version_string(void); // 静态存储,无需释放
int32_t EQUORA_API eq_ping(int32_t value);      // 返回 value + 1

// ---- 核心上下文 ----

typedef struct EqCore EqCore;

// 打开(必要时创建)数据库并应用迁移。db_path_utf8 可为 ":memory:"。
// device_id_utf8 为 NULL 时使用 "local"。失败返回 NULL 并填充 out_error。
EQUORA_API EqCore* eq_core_create(const char* db_path_utf8, const char* device_id_utf8,
                                  EqError* out_error);
void EQUORA_API eq_core_destroy(EqCore* core); // NULL 安全
int32_t EQUORA_API eq_core_schema_version(const EqCore* core, int32_t* out_version, EqError* out_error);

// ---- 任务句柄与视图 ----

typedef struct EqTaskHandle EqTaskHandle;

EQUORA_API const EqTaskView* eq_task_view(const EqTaskHandle* handle); // NULL → NULL
void EQUORA_API eq_task_handle_destroy(EqTaskHandle* handle);          // NULL 安全

// ---- 任务 CRUD(成功返回 0 并写入 *out_handle)----

int32_t EQUORA_API eq_task_create(EqCore* core, const EqTaskInput* input,
                                  EqTaskHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_task_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                               EqTaskHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_task_update(EqCore* core, const EqTaskInput* input,
                                  EqTaskHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_task_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                                       EqTaskHandle** out_handle, EqError* out_error);

// ---- 批量列表 ----

typedef struct EqTaskList EqTaskList;

int32_t EQUORA_API eq_task_list_all(EqCore* core, int32_t include_deleted,
                                    EqTaskList** out_list, EqError* out_error);
int32_t EQUORA_API eq_task_list_count(const EqTaskList* list);
EQUORA_API const EqTaskView* eq_task_list_get(const EqTaskList* list, int32_t index);
void EQUORA_API eq_task_list_destroy(EqTaskList* list); // NULL 安全

// ---- 任务查询(智能清单/搜索/分页的统一入口) ----
// due_state:0 任意 / 1 有截止 / 2 无截止;时间区间为闭开 [after, before)。
typedef struct EqTaskFilter {
    int32_t due_state;
    int64_t due_after;
    int64_t due_before;
    int64_t updated_after;
    int64_t updated_before;
    const char* project_id; // 可 NULL
    const char* tag_id;     // 可 NULL
    const char* search;     // 可 NULL,标题/备注子串
    int32_t status_count;
    const int32_t* statuses;
    int32_t excluded_count;
    const int32_t* excluded_statuses;
    int32_t include_deleted;
    int32_t sort; // 0 CreatedAsc 1 CreatedDesc 2 DueAsc 3 DueDesc 4 PriorityDesc 5 UpdatedDesc
    int32_t limit;  // 0 = 不限
    int32_t offset;
} EqTaskFilter;

int32_t EQUORA_API eq_task_query(EqCore* core, const EqTaskFilter* filter,
                                 EqTaskList** out_list, EqError* out_error);

// ---- 通用字符串句柄 ----

typedef struct EqStringHandle EqStringHandle;
EQUORA_API const char* eq_string_data(const EqStringHandle* handle); // NULL → NULL
void EQUORA_API eq_string_destroy(EqStringHandle* handle);           // NULL 安全

// 持久化设备 ID(UUID,存于 app_meta,首次调用生成)。
int32_t EQUORA_API eq_core_device_id(EqCore* core, EqStringHandle** out, EqError* out_error);

// ---- 日志(全局;level:0 Trace..4 Error) ----

int32_t EQUORA_API eq_log_init(const char* file_path_utf8, int32_t level);
int32_t EQUORA_API eq_log_shutdown(void);

// ---- 项目 ----

typedef struct EqProjectInput {
    const char* id;    // 创建时可 NULL;更新必填
    const char* name;  // 必填
    const char* color; // 可 NULL → ""
    const char* goal;  // 可 NULL → ""
    int32_t status;    // 0 Active / 1 Archived
    int64_t revision;  // 更新时的乐观并发基线
} EqProjectInput;

typedef struct EqProjectView {
    const char* id;
    const char* name;
    const char* color;
    const char* goal;
    int32_t status;
    int64_t archived_at;
    int32_t has_archived;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
    int64_t deleted_at;
    int32_t has_deleted;
    const char* last_device_id;
} EqProjectView;

typedef struct EqProjectHandle EqProjectHandle;
typedef struct EqProjectList EqProjectList;

EQUORA_API const EqProjectView* eq_project_view(const EqProjectHandle* handle);
void EQUORA_API eq_project_handle_destroy(EqProjectHandle* handle);
int32_t EQUORA_API eq_project_create(EqCore* core, const EqProjectInput* input,
                                     EqProjectHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_project_get(EqCore* core, const char* id_utf8,
                                  EqProjectHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_project_update(EqCore* core, const EqProjectInput* input,
                                     EqProjectHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_project_set_archived(EqCore* core, const char* id_utf8,
                                           int32_t archived, EqProjectHandle** out_handle,
                                           EqError* out_error);
int32_t EQUORA_API eq_project_set_deleted(EqCore* core, const char* id_utf8,
                                          int32_t deleted, EqProjectHandle** out_handle,
                                          EqError* out_error);
int32_t EQUORA_API eq_project_list(EqCore* core, int32_t include_archived,
                                   int32_t include_deleted, EqProjectList** out_list,
                                   EqError* out_error);
int32_t EQUORA_API eq_project_list_count(const EqProjectList* list);
EQUORA_API const EqProjectView* eq_project_list_get(const EqProjectList* list, int32_t index);
void EQUORA_API eq_project_list_destroy(EqProjectList* list);

// ---- 标签与任务-标签关系 ----

typedef struct EqTagInput {
    const char* id;    // 创建时可 NULL;更新必填
    const char* name;  // 必填,唯一(含墓碑,大小写不敏感)
    const char* color; // 可 NULL
    int64_t revision;
} EqTagInput;

typedef struct EqTagView {
    const char* id;
    const char* name;
    const char* color;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
    int64_t deleted_at;
    int32_t has_deleted;
    const char* last_device_id;
} EqTagView;

typedef struct EqTagHandle EqTagHandle;
typedef struct EqTagList EqTagList;

EQUORA_API const EqTagView* eq_tag_view(const EqTagHandle* handle);
void EQUORA_API eq_tag_handle_destroy(EqTagHandle* handle);
int32_t EQUORA_API eq_tag_create(EqCore* core, const EqTagInput* input,
                                 EqTagHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_tag_get(EqCore* core, const char* id_utf8,
                              EqTagHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_tag_update(EqCore* core, const EqTagInput* input,
                                 EqTagHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_tag_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                                      EqTagHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_tag_list(EqCore* core, int32_t include_deleted,
                               EqTagList** out_list, EqError* out_error);
int32_t EQUORA_API eq_tag_list_count(const EqTagList* list);
EQUORA_API const EqTagView* eq_tag_list_get(const EqTagList* list, int32_t index);
void EQUORA_API eq_tag_list_destroy(EqTagList* list);
int32_t EQUORA_API eq_task_add_tag(EqCore* core, const char* task_id_utf8,
                                   const char* tag_id_utf8, EqError* out_error);
int32_t EQUORA_API eq_task_remove_tag(EqCore* core, const char* task_id_utf8,
                                      const char* tag_id_utf8, EqError* out_error);
int32_t EQUORA_API eq_task_tags(EqCore* core, const char* task_id_utf8,
                                EqTagList** out_list, EqError* out_error);

// ---- 检查项 ----

typedef struct EqChecklistInput {
    const char* id;       // 更新必填;创建忽略(自动生成)
    const char* task_id;  // 必填
    const char* content;  // 必填
    int32_t is_checked;
    int32_t sort_order;
    int64_t revision;
} EqChecklistInput;

typedef struct EqChecklistView {
    const char* id;
    const char* task_id;
    const char* content;
    int32_t is_checked;
    int32_t sort_order;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
    int64_t deleted_at;
    int32_t has_deleted;
    const char* last_device_id;
} EqChecklistView;

typedef struct EqChecklistHandle EqChecklistHandle;
typedef struct EqChecklistList EqChecklistList;

EQUORA_API const EqChecklistView* eq_checklist_view(const EqChecklistHandle* handle);
void EQUORA_API eq_checklist_handle_destroy(EqChecklistHandle* handle);
int32_t EQUORA_API eq_checklist_add(EqCore* core, const char* task_id_utf8,
                                    const char* content_utf8, EqChecklistHandle** out_handle,
                                    EqError* out_error);
int32_t EQUORA_API eq_checklist_update(EqCore* core, const EqChecklistInput* input,
                                       EqChecklistHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_checklist_remove(EqCore* core, const char* id_utf8,
                                       EqError* out_error);
int32_t EQUORA_API eq_checklist_list(EqCore* core, const char* task_id_utf8,
                                     EqChecklistList** out_list, EqError* out_error);
int32_t EQUORA_API eq_checklist_list_count(const EqChecklistList* list);
EQUORA_API const EqChecklistView* eq_checklist_list_get(const EqChecklistList* list,
                                                        int32_t index);
void EQUORA_API eq_checklist_list_destroy(EqChecklistList* list);

// ---- 备份与导入导出 ----

// 创建一致性备份到目录;out 为备份文件路径。
int32_t EQUORA_API eq_backup_create(EqCore* core, const char* dir_utf8,
                                    EqStringHandle** out_path, EqError* out_error);
// 校验备份文件(校验和 + quick_check);结果写入 *out_ok。
int32_t EQUORA_API eq_backup_verify(const char* path_utf8, int32_t* out_ok,
                                    EqError* out_error);
// 恢复:先保护当前库,再替换;失败时主库保持可用。
int32_t EQUORA_API eq_backup_restore(EqCore* core, const char* path_utf8,
                                     EqError* out_error);
// 导出任务到文件(JSON/CSV),*out_count 为导出条数。
int32_t EQUORA_API eq_export_tasks_json(EqCore* core, const char* path_utf8,
                                        int32_t* out_count, EqError* out_error);
int32_t EQUORA_API eq_export_tasks_csv(EqCore* core, const char* path_utf8,
                                       int32_t* out_count, EqError* out_error);
// 从 JSON 文件导入(按 id 幂等)。
int32_t EQUORA_API eq_import_tasks_json(EqCore* core, const char* path_utf8,
                                        int32_t* out_imported, int32_t* out_skipped,
                                        EqError* out_error);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // EQUORA_CAPI_H
