#include "CApiInternal.h"

// ---- 视图构建(全局作用域)与文件内转换助手 ----

namespace {

void buildProjectView(EqProjectHandle& h) {
    const equora::domain::Project& p = h.project;
    h.view.id = p.id.c_str();
    h.view.name = p.name.c_str();
    h.view.color = p.color.c_str();
    h.view.goal = p.goal.c_str();
    h.view.status = static_cast<int32_t>(p.status);
    equora::capi::setOptionalView(h.view.archived_at, h.view.has_archived, p.archivedAt);
    h.view.created_at = p.createdAt;
    h.view.updated_at = p.updatedAt;
    h.view.revision = p.revision;
    equora::capi::setOptionalView(h.view.deleted_at, h.view.has_deleted, p.deletedAt);
    h.view.last_device_id = p.lastDeviceId.c_str();
}

void buildTagView(EqTagHandle& h) {
    const equora::domain::Tag& t = h.tag;
    h.view.id = t.id.c_str();
    h.view.name = t.name.c_str();
    h.view.color = t.color.c_str();
    h.view.created_at = t.createdAt;
    h.view.updated_at = t.updatedAt;
    h.view.revision = t.revision;
    equora::capi::setOptionalView(h.view.deleted_at, h.view.has_deleted, t.deletedAt);
    h.view.last_device_id = t.lastDeviceId.c_str();
}

void buildChecklistView(EqChecklistHandle& h) {
    const equora::domain::ChecklistItem& it = h.item;
    h.view.id = it.id.c_str();
    h.view.task_id = it.taskId.c_str();
    h.view.content = it.content.c_str();
    h.view.is_checked = it.isChecked ? 1 : 0;
    h.view.sort_order = it.sortOrder;
    h.view.created_at = it.createdAt;
    h.view.updated_at = it.updatedAt;
    h.view.revision = it.revision;
    equora::capi::setOptionalView(h.view.deleted_at, h.view.has_deleted, it.deletedAt);
    h.view.last_device_id = it.lastDeviceId.c_str();
}

EqProjectHandle* makeProjectHandle(equora::domain::Project p) {
    auto* h = new EqProjectHandle(std::move(p));
    return h;
}

EqTagHandle* makeTagHandle(equora::domain::Tag t) { return new EqTagHandle(std::move(t)); }

equora::domain::Project projectFromInput(const EqProjectInput& in) {
    equora::domain::Project p;
    p.id = in.id != nullptr ? in.id : "";
    p.name = in.name != nullptr ? in.name : "";
    p.color = in.color != nullptr ? in.color : "";
    p.goal = in.goal != nullptr ? in.goal : "";
    p.status = static_cast<equora::domain::ProjectStatus>(in.status);
    p.revision = in.revision;
    return p;
}

equora::domain::Tag tagFromInput(const EqTagInput& in) {
    equora::domain::Tag t;
    t.id = in.id != nullptr ? in.id : "";
    t.name = in.name != nullptr ? in.name : "";
    t.color = in.color != nullptr ? in.color : "";
    t.revision = in.revision;
    return t;
}

} // namespace

// 构造函数委托:句柄构造时调用文件内视图构建。
EqProjectHandle::EqProjectHandle(equora::domain::Project p) : project(std::move(p)) {
    buildProjectView(*this);
}
void EqProjectHandle::buildView() { buildProjectView(*this); }

EqTagHandle::EqTagHandle(equora::domain::Tag t) : tag(std::move(t)) { buildTagView(*this); }
void EqTagHandle::buildView() { buildTagView(*this); }

EqChecklistHandle::EqChecklistHandle(equora::domain::ChecklistItem i) : item(std::move(i)) {
    buildChecklistView(*this);
}
void EqChecklistHandle::buildView() { buildChecklistView(*this); }

// ---- 字符串句柄 ----

const char* eq_string_data(const EqStringHandle* handle) {
    return handle != nullptr ? handle->text.c_str() : nullptr;
}

void eq_string_destroy(EqStringHandle* handle) { delete handle; }

// ---- 项目 ----

const EqProjectView* eq_project_view(const EqProjectHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_project_handle_destroy(EqProjectHandle* handle) { delete handle; }

int32_t eq_project_create(EqCore* core, const EqProjectInput* input,
                          EqProjectHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = makeProjectHandle(core->projects.create(projectFromInput(*input)));
        return 0;
    });
}

int32_t eq_project_get(EqCore* core, const char* id_utf8, EqProjectHandle** out_handle,
                       EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->projects.findById(id_utf8);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "project not found");
        }
        *out_handle = makeProjectHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_project_update(EqCore* core, const EqProjectInput* input,
                          EqProjectHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr ||
            input->id == nullptr || input->id[0] == '\0') {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = makeProjectHandle(core->projects.update(projectFromInput(*input)));
        return 0;
    });
}

int32_t eq_project_set_archived(EqCore* core, const char* id_utf8, int32_t archived,
                                EqProjectHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        *out_handle = makeProjectHandle(core->projects.setArchived(id_utf8, archived != 0));
        return 0;
    });
}

int32_t eq_project_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                               EqProjectHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        *out_handle = makeProjectHandle(core->projects.setDeleted(id_utf8, deleted != 0));
        return 0;
    });
}

int32_t eq_project_list(EqCore* core, int32_t include_archived, int32_t include_deleted,
                        EqProjectList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqProjectList>();
        for (domain::Project& p :
             core->projects.list(include_archived != 0, include_deleted != 0)) {
            list->items.push_back(std::unique_ptr<EqProjectHandle>(makeProjectHandle(std::move(p))));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_project_list_count(const EqProjectList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqProjectView* eq_project_list_get(const EqProjectList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_project_list_destroy(EqProjectList* list) { delete list; }

// ---- 标签 ----

const EqTagView* eq_tag_view(const EqTagHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_tag_handle_destroy(EqTagHandle* handle) { delete handle; }

int32_t eq_tag_create(EqCore* core, const EqTagInput* input, EqTagHandle** out_handle,
                      EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input/out_handle must not be null");
        }
        *out_handle = makeTagHandle(core->tags.create(tagFromInput(*input)));
        return 0;
    });
}

int32_t eq_tag_get(EqCore* core, const char* id_utf8, EqTagHandle** out_handle,
                   EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        auto found = core->tags.findById(id_utf8);
        if (!found.has_value()) {
            *out_handle = nullptr;
            return fillError(out_error, static_cast<int32_t>(ErrorCode::NotFound),
                             "tag not found");
        }
        *out_handle = makeTagHandle(std::move(*found));
        return 0;
    });
}

int32_t eq_tag_update(EqCore* core, const EqTagInput* input, EqTagHandle** out_handle,
                      EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr ||
            input->id == nullptr || input->id[0] == '\0') {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id)/out_handle must not be null");
        }
        *out_handle = makeTagHandle(core->tags.update(tagFromInput(*input)));
        return 0;
    });
}

int32_t eq_tag_set_deleted(EqCore* core, const char* id_utf8, int32_t deleted,
                           EqTagHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr || out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id/out_handle must not be null");
        }
        *out_handle = makeTagHandle(core->tags.setDeleted(id_utf8, deleted != 0));
        return 0;
    });
}

int32_t eq_tag_list(EqCore* core, int32_t include_deleted, EqTagList** out_list,
                    EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqTagList>();
        for (domain::Tag& t : core->tags.list(include_deleted != 0)) {
            list->items.push_back(std::unique_ptr<EqTagHandle>(makeTagHandle(std::move(t))));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_tag_list_count(const EqTagList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqTagView* eq_tag_list_get(const EqTagList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_tag_list_destroy(EqTagList* list) { delete list; }

int32_t eq_task_add_tag(EqCore* core, const char* task_id_utf8, const char* tag_id_utf8,
                        EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || tag_id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/tag_id must not be null");
        }
        core->tags.assignToTask(task_id_utf8, tag_id_utf8);
        return 0;
    });
}

int32_t eq_task_remove_tag(EqCore* core, const char* task_id_utf8, const char* tag_id_utf8,
                           EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || tag_id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/tag_id must not be null");
        }
        core->tags.unassignFromTask(task_id_utf8, tag_id_utf8);
        return 0;
    });
}

int32_t eq_task_tags(EqCore* core, const char* task_id_utf8, EqTagList** out_list,
                     EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/out_list must not be null");
        }
        auto list = std::make_unique<EqTagList>();
        for (domain::Tag& t : core->tags.forTask(task_id_utf8)) {
            list->items.push_back(std::unique_ptr<EqTagHandle>(makeTagHandle(std::move(t))));
        }
        *out_list = list.release();
        return 0;
    });
}

// ---- 检查项 ----

const EqChecklistView* eq_checklist_view(const EqChecklistHandle* handle) {
    return handle != nullptr ? &handle->view : nullptr;
}

void eq_checklist_handle_destroy(EqChecklistHandle* handle) { delete handle; }

int32_t eq_checklist_add(EqCore* core, const char* task_id_utf8, const char* content_utf8,
                         EqChecklistHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || content_utf8 == nullptr ||
            out_handle == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/content/out_handle must not be null");
        }
        *out_handle = new EqChecklistHandle(core->checklist.add(task_id_utf8, content_utf8));
        return 0;
    });
}

int32_t eq_checklist_update(EqCore* core, const EqChecklistInput* input,
                            EqChecklistHandle** out_handle, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr || out_handle == nullptr ||
            input->id == nullptr || input->id[0] == '\0' || input->task_id == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input(id,task_id)/out_handle must not be null");
        }
        domain::ChecklistItem it;
        it.id = input->id;
        it.taskId = input->task_id;
        it.content = input->content != nullptr ? input->content : "";
        it.isChecked = input->is_checked != 0;
        it.sortOrder = input->sort_order;
        it.revision = input->revision;
        *out_handle = new EqChecklistHandle(core->checklist.update(std::move(it)));
        return 0;
    });
}

int32_t eq_checklist_remove(EqCore* core, const char* id_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/id must not be null");
        }
        core->checklist.remove(id_utf8);
        return 0;
    });
}

int32_t eq_checklist_list(EqCore* core, const char* task_id_utf8,
                          EqChecklistList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || task_id_utf8 == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/task_id/out_list must not be null");
        }
        auto list = std::make_unique<EqChecklistList>();
        for (domain::ChecklistItem& it : core->checklist.listForTask(task_id_utf8)) {
            list->items.push_back(std::unique_ptr<EqChecklistHandle>(new EqChecklistHandle(std::move(it))));
        }
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_checklist_list_count(const EqChecklistList* list) {
    return list != nullptr ? static_cast<int32_t>(list->items.size()) : 0;
}

const EqChecklistView* eq_checklist_list_get(const EqChecklistList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->items.size()) {
        return nullptr;
    }
    return &list->items[static_cast<std::size_t>(index)]->view;
}

void eq_checklist_list_destroy(EqChecklistList* list) { delete list; }
