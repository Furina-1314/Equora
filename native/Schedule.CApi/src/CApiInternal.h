// C ABI 实现层共享内部声明:不对外安装,只被 src/*.cpp 使用。
//
// 注意:头文件以 `typedef struct EqCore EqCore;` 等形式在全局作用域声明了
// 不透明类型;它们的完整定义必须同样位于全局作用域,否则会产生两个不同类型。
#pragma once

#include <equora/capi/equora_capi.h>

#include <cstring>
#include <exception>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

#include <equora/core/CalendarRepository.h>
#include <equora/core/FocusRepository.h>
#include <equora/syncclient/SyncClient.h>
#include <equora/core/ChecklistRepository.h>
#include <equora/core/ProjectRepository.h>
#include <equora/core/TagRepository.h>
#include <equora/core/TaskRepository.h>
#include <equora/core/TaskQuery.h>
#include <equora/domain/Error.h>
#include <equora/domain/Project.h>
#include <equora/domain/Tag.h>
#include <equora/storage/Database.h>

// ---- 不透明类型的完整定义(全局作用域) ----

struct EqCore {
    equora::storage::Database db;
    equora::core::TaskRepository tasks;
    equora::core::CalendarRepository calendar;
    equora::core::FocusRepository focus;
    equora::sync::SyncClientRepository sync;
    equora::core::ProjectRepository projects;
    equora::core::TagRepository tags;
    equora::core::ChecklistRepository checklist;

    EqCore(equora::storage::Database&& d, std::string deviceId)
        : db(std::move(d)), tasks(db, deviceId), calendar(db, deviceId),
          focus(db, deviceId), sync(db), projects(db, deviceId),
          tags(db, deviceId), checklist(db, deviceId) {}
};

struct EqTaskHandle {
    equora::domain::Task task;
    EqTaskView view{};
    explicit EqTaskHandle(equora::domain::Task t) : task(std::move(t)) { buildView(); }
    void buildView();
};

struct EqTaskList {
    std::vector<std::unique_ptr<EqTaskHandle>> items;
};

struct EqProjectHandle {
    equora::domain::Project project;
    EqProjectView view{};
    explicit EqProjectHandle(equora::domain::Project p);
    void buildView();
};

struct EqProjectList {
    std::vector<std::unique_ptr<EqProjectHandle>> items;
};

struct EqTagHandle {
    equora::domain::Tag tag;
    EqTagView view{};
    explicit EqTagHandle(equora::domain::Tag t);
    void buildView();
};

struct EqTagList {
    std::vector<std::unique_ptr<EqTagHandle>> items;
};

struct EqChecklistHandle {
    equora::domain::ChecklistItem item;
    EqChecklistView view{};
    explicit EqChecklistHandle(equora::domain::ChecklistItem i);
    void buildView();
};

struct EqChecklistList {
    std::vector<std::unique_ptr<EqChecklistHandle>> items;
};

struct EqStringHandle {
    std::string text;
    explicit EqStringHandle(std::string t) : text(std::move(t)) {}
};

namespace equora::capi {

using domain::ErrorCode;
using domain::EquoraError;

// ---- 边界助手(异常 → 错误码 + EqError) ----

inline int32_t fillError(EqError* out, int32_t code, const char* message) {
    if (out != nullptr) {
        out->code = code;
        const std::size_t len = message != nullptr ? std::strlen(message) : 0;
        const std::size_t n = len < sizeof(out->message) - 1 ? len : sizeof(out->message) - 1;
        if (n > 0) std::memcpy(out->message, message, n);
        out->message[n] = '\0';
    }
    return code;
}

inline int32_t translateException(EqError* out, const std::exception& e) {
    int32_t code = static_cast<int32_t>(ErrorCode::Unknown);
    if (const auto* ee = dynamic_cast<const EquoraError*>(&e)) {
        code = static_cast<int32_t>(ee->code());
    }
    return fillError(out, code, e.what());
}

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

EqTaskHandle* makeTaskHandle(domain::Task t);

// ABI 传入的文件路径一律是 UTF-8 字节。Windows 上 fstream/path 的 const char*
// 重载按 ANSI 代码页解释,非 ASCII 路径会被误读成不存在的文件;char8_t 构造
// 始终按 UTF-8 转成原生宽路径。
inline std::filesystem::path pathFromUtf8(const char* utf8) {
    return std::filesystem::path(std::u8string_view(
        reinterpret_cast<const char8_t*>(utf8), std::strlen(utf8)));
}

// 把路径转回 ABI 约定的 UTF-8 字符串(如备份结果回传)。
inline std::string utf8FromPath(const std::filesystem::path& path) {
    const std::u8string u8 = path.u8string();
    return std::string(u8.begin(), u8.end());
}

// 把可选时间字段写进视图(has 标志 + 值)。
inline void setOptionalView(int64_t& value, int32_t& has,
                            const std::optional<domain::UtcMillis>& src) {
    has = src.has_value() ? 1 : 0;
    value = src.value_or(0);
}

} // namespace equora::capi
