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

#ifdef __cplusplus
} // extern "C"
#endif

#endif // EQUORA_CAPI_H
