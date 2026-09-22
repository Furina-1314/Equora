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

#define EQUORA_CAPI_VERSION 3

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
int32_t EQUORA_API eq_task_permanently_delete(EqCore* core, const char* id_utf8, EqError* out_error);
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


// ---- 日历(P6):时间块、日程、重复规则、窗口物化、冲突、空闲、ICS ----

typedef struct EqCalendarView {
    const char* id;
    const char* name;
    const char* color;
    const char* source;
    int32_t is_visible;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
} EqCalendarView;

typedef struct EqCalendarHandle EqCalendarHandle;
typedef struct EqCalendarList EqCalendarList;

EQUORA_API const EqCalendarView* eq_calendar_view(const EqCalendarHandle* handle);
void EQUORA_API eq_calendar_handle_destroy(EqCalendarHandle* handle);
int32_t EQUORA_API eq_calendar_create(EqCore* core, const char* name_utf8,
                                      const char* color_utf8, EqCalendarHandle** out_handle,
                                      EqError* out_error);
int32_t EQUORA_API eq_calendar_list(EqCore* core, EqCalendarList** out_list,
                                    EqError* out_error);
int32_t EQUORA_API eq_calendar_list_count(const EqCalendarList* list);
EQUORA_API const EqCalendarView* eq_calendar_list_get(const EqCalendarList* list, int32_t index);
void EQUORA_API eq_calendar_list_destroy(EqCalendarList* list);

typedef struct EqBlockInput {
    const char* id;        // 更新必填
    const char* task_id;   // 可 NULL
    const char* calendar_id;
    int64_t start_at;
    int64_t end_at;
    int32_t prepare_minutes;
    int32_t buffer_minutes;
    int32_t actual_minutes;
    const char* note;
    int64_t revision;
    const char* title;
    const char* batch_id;
} EqBlockInput;

typedef struct EqBlockView {
    const char* id;
    const char* task_id; // NULL = 独立块
    const char* calendar_id;
    int64_t start_at;
    int64_t end_at;
    int32_t prepare_minutes;
    int32_t buffer_minutes;
    int32_t actual_minutes;
    const char* note;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
    int64_t deleted_at;
    int32_t has_deleted;
    const char* title;
    const char* batch_id;
} EqBlockView;

typedef struct EqBlockHandle EqBlockHandle;
typedef struct EqBlockList EqBlockList;

EQUORA_API const EqBlockView* eq_block_view(const EqBlockHandle* handle);
int32_t EQUORA_API eq_block_apply_batch(EqCore* core, const EqBlockInput* inputs, int32_t count,
                                       int32_t deleted, int32_t affect_tasks, EqError* out_error);
void EQUORA_API eq_block_handle_destroy(EqBlockHandle* handle);
int32_t EQUORA_API eq_block_create(EqCore* core, const EqBlockInput* input,
                                   EqBlockHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_block_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                                EqBlockHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_block_update(EqCore* core, const EqBlockInput* input,
                                   EqBlockHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_block_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                                        EqError* out_error);
int32_t EQUORA_API eq_block_list_range(EqCore* core, int64_t from, int64_t to,
                                       EqBlockList** out_list, EqError* out_error);
int32_t EQUORA_API eq_block_list_for_task(EqCore* core, const char* task_id_utf8,
                                          EqBlockList** out_list, EqError* out_error);
int32_t EQUORA_API eq_block_list_count(const EqBlockList* list);
EQUORA_API const EqBlockView* eq_block_list_get(const EqBlockList* list, int32_t index);
void EQUORA_API eq_block_list_destroy(EqBlockList* list);

typedef struct EqEventInput {
    const char* id;    // 更新必填;创建可空(自动生成,导入时可指定)
    const char* title;
    const char* location;
    const char* note;
    const char* calendar_id;
    int64_t start_at;
    int64_t end_at;
    int32_t is_all_day;
    int64_t revision;
} EqEventInput;

typedef struct EqEventView {
    const char* id;
    const char* title;
    const char* location;
    const char* note;
    const char* calendar_id;
    int64_t start_at;
    int64_t end_at;
    int32_t is_all_day;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
    int64_t deleted_at;
    int32_t has_deleted;
} EqEventView;

typedef struct EqEventHandle EqEventHandle;
typedef struct EqEventList EqEventList;

EQUORA_API const EqEventView* eq_event_view(const EqEventHandle* handle);
void EQUORA_API eq_event_handle_destroy(EqEventHandle* handle);
int32_t EQUORA_API eq_event_create(EqCore* core, const EqEventInput* input,
                                   EqEventHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_event_get(EqCore* core, const char* id_utf8, int32_t include_deleted,
                                EqEventHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_event_update(EqCore* core, const EqEventInput* input,
                                   EqEventHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_event_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                                        EqError* out_error);
int32_t EQUORA_API eq_event_list_range(EqCore* core, int64_t from, int64_t to,
                                       EqEventList** out_list, EqError* out_error);
int32_t EQUORA_API eq_event_list_count(const EqEventList* list);
EQUORA_API const EqEventView* eq_event_list_get(const EqEventList* list, int32_t index);
void EQUORA_API eq_event_list_destroy(EqEventList* list);

typedef struct EqRuleInput {
    const char* id;         // 更新必填
    const char* host_type;  // "task" | "event"
    const char* host_id;
    int32_t freq;           // 0 Daily 1 Weekly 2 Monthly 3 Yearly
    int32_t interval;
    const char* by_weekday; // CSV "0,2,4"(Mon=0);可空
    int32_t month_mode;     // 0 Date 1 NthWeekday 2 LastWeekday
    int32_t month_nth;
    int32_t month_weekday;
    int64_t until_utc;
    int32_t has_until;
    int64_t max_count;
    int32_t has_max_count;
    int32_t complete_recur_days;
    const char* excluded_dates; // CSV "YYYY-MM-DD";可空
    int64_t revision;
} EqRuleInput;

typedef struct EqRuleView {
    const char* id;
    const char* host_type;
    const char* host_id;
    int32_t freq;
    int32_t interval;
    const char* by_weekday;
    int32_t month_mode;
    int32_t month_nth;
    int32_t month_weekday;
    int64_t until_utc;
    int32_t has_until;
    int64_t max_count;
    int32_t has_max_count;
    int32_t complete_recur_days;
    const char* excluded_dates;
    const char* rrule_text;
    int64_t revision;
} EqRuleView;

typedef struct EqRuleHandle EqRuleHandle;
typedef struct EqRuleList EqRuleList;

EQUORA_API const EqRuleView* eq_rule_view(const EqRuleHandle* handle);
void EQUORA_API eq_rule_handle_destroy(EqRuleHandle* handle);
int32_t EQUORA_API eq_rule_create(EqCore* core, const EqRuleInput* input,
                                  EqRuleHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_rule_get(EqCore* core, const char* id_utf8,
                               EqRuleHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_rule_update(EqCore* core, const EqRuleInput* input,
                                  EqRuleHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_rule_delete(EqCore* core, const char* id_utf8, EqError* out_error);
int32_t EQUORA_API eq_rule_list_for_host(EqCore* core, const char* host_type_utf8,
                                         const char* host_id_utf8, EqRuleList** out_list,
                                         EqError* out_error);
int32_t EQUORA_API eq_rule_list_count(const EqRuleList* list);
EQUORA_API const EqRuleView* eq_rule_list_get(const EqRuleList* list, int32_t index);
void EQUORA_API eq_rule_list_destroy(EqRuleList* list);

typedef struct EqSpanView {
    const char* source_id;
    const char* source_type;
    const char* title;
    const char* task_id;
    int64_t start;
    int64_t end;
} EqSpanView;

typedef struct EqSpanList EqSpanList;
int32_t EQUORA_API eq_window_spans(EqCore* core, int64_t from, int64_t to,
                                   int32_t tz_offset_minutes, EqSpanList** out_list,
                                   EqError* out_error);
int32_t EQUORA_API eq_span_list_count(const EqSpanList* list);
EQUORA_API const EqSpanView* eq_span_list_get(const EqSpanList* list, int32_t index);
void EQUORA_API eq_span_list_destroy(EqSpanList* list);

typedef struct EqConflictView {
    EqSpanView a;
    EqSpanView b;
    int64_t overlap_minutes;
} EqConflictView;

typedef struct EqConflictList EqConflictList;
int32_t EQUORA_API eq_window_conflicts(EqCore* core, int64_t from, int64_t to,
                                       int32_t tz_offset_minutes, EqConflictList** out_list,
                                       EqError* out_error);
int32_t EQUORA_API eq_conflict_list_count(const EqConflictList* list);
EQUORA_API const EqConflictView* eq_conflict_list_get(const EqConflictList* list, int32_t index);
void EQUORA_API eq_conflict_list_destroy(EqConflictList* list);

typedef struct EqSlotView {
    int64_t start;
    int64_t end;
} EqSlotView;

typedef struct EqSlotList EqSlotList;
// workdayMask:bit0=Mon .. bit6=Sun。
int32_t EQUORA_API eq_find_free_slots(EqCore* core, int64_t from, int64_t to,
                                      int32_t work_start_minute, int32_t work_end_minute,
                                      int32_t workday_mask, int64_t min_minutes,
                                      int32_t limit, EqSlotList** out_list,
                                      EqError* out_error);
int32_t EQUORA_API eq_slot_list_count(const EqSlotList* list);
EQUORA_API const EqSlotView* eq_slot_list_get(const EqSlotList* list, int32_t index);
void EQUORA_API eq_slot_list_destroy(EqSlotList* list);

// 例外编辑:detach = 仅修改本次;split = 本次及以后另立新规则。
int32_t EQUORA_API eq_event_detach_occurrence(EqCore* core, const char* rule_id_utf8,
                                              int64_t occurrence_start_utc,
                                              int32_t tz_offset_minutes,
                                              EqEventHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_rule_split_series(EqCore* core, const char* rule_id_utf8,
                                        int64_t occurrence_start_utc,
                                        EqRuleHandle** out_handle, EqError* out_error);

// ICS 文件导入导出。
int32_t EQUORA_API eq_export_ics(EqCore* core, int64_t from, int64_t to,
                                 const char* path_utf8, int32_t* out_count,
                                 EqError* out_error);
int32_t EQUORA_API eq_import_ics(EqCore* core, const char* path_utf8, int32_t* out_imported,
                                 int32_t* out_skipped, int32_t* out_failed,
                                 EqError* out_error);


// ---- 专注会话(P9) ----

typedef struct EqFocusSessionView {
    const char* id;
    const char* task_id;   // NULL = 独立
    const char* block_id;
    int32_t mode;          // 0 番茄 1 深度 2 Flowtime 3 正计时 4 无计时
    int64_t planned_start;
    int64_t planned_end;
    int32_t has_planned_end;
    int64_t actual_start;
    int64_t actual_end;
    int32_t has_actual_end; // 0 = 未闭合
    int64_t paused_ms;
    int32_t state;          // 0 运行 1 暂停 2 完成 3 放弃
    const char* goal;
    const char* completion_note;
    int32_t completion_level;
    int64_t created_at;
    int64_t updated_at;
    int64_t revision;
} EqFocusSessionView;

typedef struct EqFocusSessionHandle EqFocusSessionHandle;
typedef struct EqFocusSessionList EqFocusSessionList;

EQUORA_API const EqFocusSessionView* eq_focus_view(const EqFocusSessionHandle* handle);
void EQUORA_API eq_focus_handle_destroy(EqFocusSessionHandle* handle);
int32_t EQUORA_API eq_focus_start(EqCore* core, int32_t mode, int32_t planned_minutes,
                                  const char* goal_utf8, const char* task_id_utf8,
                                  const char* block_id_utf8, int64_t now,
                                  EqFocusSessionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_find(EqCore* core, const char* id_utf8,
                                 EqFocusSessionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_open(EqCore* core, EqFocusSessionHandle** out_handle,
                                 EqError* out_error); // 无开放会话 → NotFound
int32_t EQUORA_API eq_focus_pause(EqCore* core, const char* id_utf8, int64_t now,
                                  EqFocusSessionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_resume(EqCore* core, const char* id_utf8, int64_t now,
                                   EqFocusSessionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_complete(EqCore* core, const char* id_utf8, int64_t now,
                                     const char* note_utf8, int32_t completion_level,
                                     EqFocusSessionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_abandon(EqCore* core, const char* id_utf8, int64_t now,
                                    EqFocusSessionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_history(EqCore* core, int64_t from, int64_t to, int32_t limit,
                                    EqFocusSessionList** out_list, EqError* out_error);
int32_t EQUORA_API eq_focus_list_count(const EqFocusSessionList* list);
EQUORA_API const EqFocusSessionView* eq_focus_list_get(const EqFocusSessionList* list,
                                                       int32_t index);
void EQUORA_API eq_focus_list_destroy(EqFocusSessionList* list);
// 崩溃恢复:收尾未闭合会话,条数存于 *out_recovered。
int32_t EQUORA_API eq_focus_recover_interrupted(EqCore* core, int64_t now,
                                                int32_t* out_recovered, EqError* out_error);

typedef struct EqInterruptionView {
    const char* id;
    const char* session_id;
    int64_t occurred_at;
    int64_t duration_ms;
    const char* reason;
    const char* source;
    const char* handling;
} EqInterruptionView;

typedef struct EqInterruptionHandle EqInterruptionHandle;
typedef struct EqInterruptionList EqInterruptionList;

EQUORA_API const EqInterruptionView* eq_interruption_view(const EqInterruptionHandle* handle);
void EQUORA_API eq_interruption_handle_destroy(EqInterruptionHandle* handle);
int32_t EQUORA_API eq_focus_add_interruption(EqCore* core, const char* session_id_utf8,
                                             int64_t occurred_at, int64_t duration_ms,
                                             const char* reason_utf8, const char* source_utf8,
                                             const char* handling_utf8,
                                             EqInterruptionHandle** out_handle,
                                             EqError* out_error);
int32_t EQUORA_API eq_focus_interruptions(EqCore* core, const char* session_id_utf8,
                                          EqInterruptionList** out_list, EqError* out_error);
int32_t EQUORA_API eq_interruption_list_count(const EqInterruptionList* list);
EQUORA_API const EqInterruptionView* eq_interruption_list_get(const EqInterruptionList* list,
                                                              int32_t index);
void EQUORA_API eq_interruption_list_destroy(EqInterruptionList* list);

typedef struct EqDistractionView {
    const char* id;
    const char* session_id; // NULL = 非会话期捕获
    const char* content;
    int64_t captured_at;
    int32_t resolution;
    const char* resolved_ref;
} EqDistractionView;

typedef struct EqDistractionHandle EqDistractionHandle;
typedef struct EqDistractionList EqDistractionList;

EQUORA_API const EqDistractionView* eq_distraction_view(const EqDistractionHandle* handle);
void EQUORA_API eq_distraction_handle_destroy(EqDistractionHandle* handle);
int32_t EQUORA_API eq_focus_capture(EqCore* core, const char* content_utf8,
                                    const char* session_id_utf8, int64_t now,
                                    EqDistractionHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_focus_pending_distractions(EqCore* core, int32_t limit,
                                                 EqDistractionList** out_list,
                                                 EqError* out_error);
int32_t EQUORA_API eq_focus_resolve_distraction(EqCore* core, const char* id_utf8,
                                                int32_t resolution,
                                                const char* resolved_ref_utf8,
                                                EqDistractionHandle** out_handle,
                                                EqError* out_error);
int32_t EQUORA_API eq_distraction_list_count(const EqDistractionList* list);
EQUORA_API const EqDistractionView* eq_distraction_list_get(const EqDistractionList* list,
                                                             int32_t index);
void EQUORA_API eq_distraction_list_destroy(EqDistractionList* list);

typedef struct EqFocusProfileInput {
    const char* id; // 更新必填
    const char* name;
    int32_t mode;
    int32_t planned_minutes;
    int32_t break_minutes;
    const char* allowed_apps;
    const char* blocked_apps;
    const char* allowed_sites;
    const char* blocked_sites;
    int32_t notify_policy;
    int32_t is_default;
    int64_t revision;
} EqFocusProfileInput;

typedef struct EqFocusProfileView {
    const char* id;
    const char* name;
    int32_t mode;
    int32_t planned_minutes;
    int32_t break_minutes;
    const char* allowed_apps;
    const char* blocked_apps;
    const char* allowed_sites;
    const char* blocked_sites;
    int32_t notify_policy;
    int32_t is_default;
    int64_t revision;
} EqFocusProfileView;

typedef struct EqFocusProfileHandle EqFocusProfileHandle;
typedef struct EqFocusProfileList EqFocusProfileList;

EQUORA_API const EqFocusProfileView* eq_focus_profile_view(
    const EqFocusProfileHandle* handle);
void EQUORA_API eq_focus_profile_handle_destroy(EqFocusProfileHandle* handle);
int32_t EQUORA_API eq_focus_profile_create(EqCore* core, const EqFocusProfileInput* input,
                                           EqFocusProfileHandle** out_handle,
                                           EqError* out_error);
int32_t EQUORA_API eq_focus_profile_find(EqCore* core, const char* id_utf8,
                                         EqFocusProfileHandle** out_handle,
                                         EqError* out_error);
int32_t EQUORA_API eq_focus_profile_update(EqCore* core, const EqFocusProfileInput* input,
                                           EqFocusProfileHandle** out_handle,
                                           EqError* out_error);
int32_t EQUORA_API eq_focus_profile_delete(EqCore* core, const char* id_utf8,
                                           EqError* out_error);
int32_t EQUORA_API eq_focus_profile_list(EqCore* core, EqFocusProfileList** out_list,
                                         EqError* out_error);
int32_t EQUORA_API eq_focus_profile_list_count(const EqFocusProfileList* list);
EQUORA_API const EqFocusProfileView* eq_focus_profile_list_get(
    const EqFocusProfileList* list, int32_t index);
void EQUORA_API eq_focus_profile_list_destroy(EqFocusProfileList* list);


// ---- 智能规划 / 复盘 / 自动化(P12) ----

typedef struct EqProposedView {
    const char* task_id;
    int64_t start;
    int64_t end;
    const char* reason;
} EqProposedView;

typedef struct EqProposedList EqProposedList;
// 自动排程:候选=未完成且有估时的任务;忙碌=当前窗口物化。
int32_t EQUORA_API eq_plan_week(EqCore* core, int64_t from, int64_t to,
                                int32_t work_start_minute, int32_t work_end_minute,
                                int32_t workday_mask, int32_t max_block_minutes,
                                int32_t tz_offset_minutes, EqProposedList** out_list,
                                EqError* out_error);
int32_t EQUORA_API eq_proposed_count(const EqProposedList* list);
EQUORA_API const EqProposedView* eq_proposed_get(const EqProposedList* list, int32_t index);
void EQUORA_API eq_proposed_list_destroy(EqProposedList* list);

typedef struct EqDayLoadView {
    char local_date[11]; // YYYY-MM-DD
    int64_t planned_minutes;
    int64_t capacity_minutes;
    int32_t overloaded;
} EqDayLoadView;

typedef struct EqLoadList EqLoadList;
int32_t EQUORA_API eq_day_loads(EqCore* core, int64_t from, int64_t to,
                                int32_t work_start_minute, int32_t work_end_minute,
                                int32_t workday_mask, int32_t tz_offset_minutes,
                                EqLoadList** out_list, EqError* out_error);
int32_t EQUORA_API eq_load_count(const EqLoadList* list);
EQUORA_API const EqDayLoadView* eq_load_get(const EqLoadList* list, int32_t index);
void EQUORA_API eq_load_list_destroy(EqLoadList* list);

typedef struct EqSplitView {
    char task_id[64];
    char title[128];
    int32_t total_minutes;
    int32_t blocks;
    int32_t block_minutes;
} EqSplitView;

typedef struct EqSplitList EqSplitList;
int32_t EQUORA_API eq_suggest_splits(EqCore* core, int64_t from, int64_t to,
                                     int32_t max_block_minutes,
                                     EqSplitList** out_list, EqError* out_error);
int32_t EQUORA_API eq_split_count(const EqSplitList* list);
EQUORA_API const EqSplitView* eq_split_get(const EqSplitList* list, int32_t index);
void EQUORA_API eq_split_list_destroy(EqSplitList* list);

// 估时校正:返回建议分钟数;样本 <2 时 has_suggestion=0。
int32_t EQUORA_API eq_correct_estimate(EqCore* core, const char* task_id_utf8,
                                       int32_t* out_suggested, int32_t* out_samples,
                                       int32_t* out_has, EqError* out_error);

// 复盘:计算返回标量结构;notes 等文本由调用方保存时传入。
typedef struct EqDailyOut {
    int32_t completed;
    int32_t deferred;
    int32_t cancelled;
    int32_t big_three_done;
    int32_t distraction_count;
    int64_t planned_minutes;
    int64_t actual_minutes;
} EqDailyOut;

int32_t EQUORA_API eq_review_daily_compute(EqCore* core, int64_t day_start_utc,
                                           int32_t tz_offset_minutes, EqDailyOut* out,
                                           EqError* out_error);
int32_t EQUORA_API eq_review_daily_save(EqCore* core, int64_t day_start_utc,
                                        int32_t tz_offset_minutes,
                                        const char* notes_utf8,
                                        const char* focus_tomorrow_utf8,
                                        EqError* out_error);

typedef struct EqWeeklyOut {
    int64_t deep_work_minutes;
    double focus_ratio;
    double estimate_accuracy;
    int32_t best_focus_hour;
} EqWeeklyOut;

int32_t EQUORA_API eq_review_weekly_compute(EqCore* core, int64_t week_start_utc,
                                            int32_t tz_offset_minutes, EqWeeklyOut* out,
                                            EqError* out_error);
int32_t EQUORA_API eq_review_weekly_save(EqCore* core, int64_t week_start_utc,
                                         int32_t tz_offset_minutes,
                                         const char* notes_utf8,
                                         const char* next_week_goals_utf8,
                                         EqError* out_error);

// ---- 自动化 ----

typedef struct EqAutomationInput {
    const char* id; // 更新必填
    const char* name;
    const char* trigger;
    const char* conditions;
    const char* actions;
    int32_t enabled;
    int64_t revision;
} EqAutomationInput;

typedef struct EqAutomationView {
    const char* id;
    const char* name;
    const char* trigger;
    const char* conditions;
    const char* actions;
    int32_t enabled;
    int64_t revision;
} EqAutomationView;

typedef struct EqAutomationHandle EqAutomationHandle;
typedef struct EqAutomationList EqAutomationList;

EQUORA_API const EqAutomationView* eq_automation_view(const EqAutomationHandle* handle);
void EQUORA_API eq_automation_handle_destroy(EqAutomationHandle* handle);
int32_t EQUORA_API eq_automation_create(EqCore* core, const EqAutomationInput* input,
                                        EqAutomationHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_automation_find(EqCore* core, const char* id_utf8,
                                      EqAutomationHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_automation_update(EqCore* core, const EqAutomationInput* input,
                                        EqAutomationHandle** out_handle, EqError* out_error);
int32_t EQUORA_API eq_automation_set_enabled(EqCore* core, const char* id_utf8,
                                             int32_t enabled, EqError* out_error);
int32_t EQUORA_API eq_automation_delete(EqCore* core, const char* id_utf8,
                                        EqError* out_error);
int32_t EQUORA_API eq_automation_list(EqCore* core, EqAutomationList** out_list,
                                      EqError* out_error);
int32_t EQUORA_API eq_automation_list_count(const EqAutomationList* list);
EQUORA_API const EqAutomationView* eq_automation_list_get(const EqAutomationList* list,
                                                          int32_t index);
void EQUORA_API eq_automation_list_destroy(EqAutomationList* list);
typedef struct EqIdList EqIdList;
int32_t EQUORA_API eq_automation_evaluate(EqCore* core, const char* trigger_utf8,
                                          const char* task_id_utf8,
                                          EqIdList** out_list,
                                          EqError* out_error);
int32_t EQUORA_API eq_id_list_count(const EqIdList* list);
EQUORA_API const char* eq_id_list_get(const EqIdList* list, int32_t index);
void EQUORA_API eq_id_list_destroy(EqIdList* list);


// ---- 同步客户端(P14):Outbox、状态、冲突、退避与合并 ----

typedef struct EqOutboxInput {
    const char* operation_id; // UUID(幂等键)
    const char* entity_type;
    const char* entity_id;
    int64_t base_revision;
    int32_t kind; // 0 create / 1 update / 2 delete
    const char* payload; // JSON 文本
} EqOutboxInput;

typedef struct EqOutboxView {
    int64_t row_id;
    const char* operation_id;
    const char* entity_type;
    const char* entity_id;
    int64_t base_revision;
    int32_t kind;
    const char* payload;
    int32_t status;   // 0 待上传 1 上传中 2 失败(超限)
    int32_t attempts;
    int64_t next_attempt_at;
} EqOutboxView;

typedef struct EqOutboxList EqOutboxList;

int32_t EQUORA_API eq_outbox_enqueue(EqCore* core, const EqOutboxInput* input,
                                     EqError* out_error);
int32_t EQUORA_API eq_outbox_due_pending(EqCore* core, int64_t now_ms, int32_t limit,
                                         EqOutboxList** out_list, EqError* out_error);
int32_t EQUORA_API eq_outbox_mark_sending(EqCore* core, int64_t row_id,
                                          EqError* out_error);
int32_t EQUORA_API eq_outbox_mark_result(EqCore* core, int64_t row_id,
                                         int32_t accepted, int64_t now_ms,
                                         int32_t max_attempts, EqError* out_error);
int32_t EQUORA_API eq_outbox_reset_stuck(EqCore* core, EqError* out_error);
int32_t EQUORA_API eq_outbox_pending_count(EqCore* core, int32_t* out_count,
                                           EqError* out_error);
int32_t EQUORA_API eq_outbox_list_count(const EqOutboxList* list);
EQUORA_API const EqOutboxView* eq_outbox_list_get(const EqOutboxList* list, int32_t index);
void EQUORA_API eq_outbox_list_destroy(EqOutboxList* list);

typedef struct EqSyncStateView {
    int64_t cursor;
    const char* server_url;
    const char* device_id;
    int64_t last_success_at;
} EqSyncStateView;

int32_t EQUORA_API eq_sync_state(EqCore* core, EqSyncStateView* out, EqError* out_error);
int32_t EQUORA_API eq_sync_save_cursor(EqCore* core, int64_t cursor, int64_t now_ms,
                                       EqError* out_error);
int32_t EQUORA_API eq_sync_save_url(EqCore* core, const char* url_utf8,
                                    EqError* out_error);

// 同步冲突(与日历重叠冲突 EqConflictView 区分,前缀 Sync)。
typedef struct EqSyncConflictView {
    int64_t row_id;
    const char* operation_id;
    const char* entity_type;
    const char* entity_id;
    const char* local_payload;
    const char* server_payload;
    int64_t server_revision;
    int32_t server_deleted;
    int64_t created_at;
    int32_t resolution;
} EqSyncConflictView;

typedef struct EqSyncConflictList EqSyncConflictList;

int32_t EQUORA_API eq_sync_conflict_add(EqCore* core, const char* operation_id_utf8,
                                        const char* entity_type_utf8,
                                        const char* entity_id_utf8,
                                        const char* local_payload_utf8,
                                        const char* server_payload_utf8,
                                        int64_t server_revision, int32_t server_deleted,
                                        int64_t now_ms, EqError* out_error);
int32_t EQUORA_API eq_sync_conflict_open(EqCore* core, EqSyncConflictList** out_list,
                                         EqError* out_error);
int32_t EQUORA_API eq_sync_conflict_resolve(EqCore* core, int64_t row_id,
                                            int32_t resolution, EqError* out_error);
int32_t EQUORA_API eq_sync_conflict_list_count(const EqSyncConflictList* list);
EQUORA_API const EqSyncConflictView* eq_sync_conflict_list_get(
    const EqSyncConflictList* list, int32_t index);
void EQUORA_API eq_sync_conflict_list_destroy(EqSyncConflictList* list);

// 退避(纯):attempts 次失败后的下次延迟毫秒;jitter∈[0,1)。
int32_t EQUORA_API eq_sync_backoff_delay(int32_t attempts, int64_t base_ms,
                                         int64_t max_ms, double jitter,
                                         int64_t* out_ms);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // EQUORA_CAPI_H
