#include <equora/core/TaskRepository.h>

#include <algorithm>
#include <variant>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>

namespace equora::core {

using domain::ErrorCode;
using domain::EquoraError;
using domain::Task;
using domain::Uuid;
using domain::utc::now;
using storage::Statement;
using storage::Transaction;

namespace {

constexpr auto kSelectColumns =
    "SELECT id, title, note, status, priority, importance, due_at, "
    "estimate_minutes, actual_minutes, project_id, created_at, updated_at, "
    "revision, deleted_at, last_device_id FROM tasks";

[[nodiscard]] bool isBlank(std::string_view s) {
    return std::all_of(s.begin(), s.end(), [](char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    });
}

} // namespace

TaskRepository::TaskRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

Task TaskRepository::rowToTask(const storage::Statement& st) {
    Task t;
    t.id = st.columnText(0);
    t.title = st.columnText(1);
    t.note = st.columnText(2);
    t.status = static_cast<domain::TaskStatus>(st.columnInt(3));
    t.priority = static_cast<domain::Priority>(st.columnInt(4));
    t.importance = static_cast<std::int32_t>(st.columnInt(5));
    t.dueAt = st.isNull(6)
                  ? std::nullopt
                  : std::optional<domain::UtcMillis>(st.columnInt(6));
    t.estimateMinutes = st.isNull(7)
                            ? std::nullopt
                            : std::optional<std::int32_t>(
                                  static_cast<std::int32_t>(st.columnInt(7)));
    t.actualMinutes = static_cast<std::int32_t>(st.columnInt(8));
    t.projectId = st.isNull(9) ? std::nullopt : std::optional<std::string>(st.columnText(9));
    t.createdAt = st.columnInt(10);
    t.updatedAt = st.columnInt(11);
    t.revision = st.columnInt(12);
    t.deletedAt = st.isNull(13)
                      ? std::nullopt
                      : std::optional<domain::UtcMillis>(st.columnInt(13));
    t.lastDeviceId = st.columnText(14);
    return t;
}

Task TaskRepository::create(domain::Task draft) const {
    if (isBlank(draft.title)) {
        throw EquoraError(ErrorCode::InvalidArgument, "task title must not be blank");
    }

    if (draft.id.empty()) {
        draft.id = Uuid::random().toString();
    } else if (!Uuid::parse(draft.id).has_value()) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "task id must be a valid UUID: '" + draft.id + "'");
    }

    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;
    draft.deletedAt = std::nullopt;
    draft.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO tasks (id, title, note, status, priority, importance, "
            "due_at, estimate_minutes, actual_minutes, project_id, created_at, "
            "updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.title)
            .bind(3, draft.note)
            .bind(4, static_cast<std::int64_t>(draft.status))
            .bind(5, static_cast<std::int64_t>(draft.priority))
            .bind(6, static_cast<std::int64_t>(draft.importance))
            .bind(7, draft.dueAt)
            .bind(8, draft.estimateMinutes)
            .bind(9, static_cast<std::int64_t>(draft.actualMinutes))
            .bind(10, draft.projectId)
            .bind(11, draft.createdAt)
            .bind(12, draft.updatedAt)
            .bind(13, draft.revision)
            .bind(14, draft.deletedAt)
            .bind(15, draft.lastDeviceId);
        st.step();
    }
    tx.commit();
    return draft;
}

std::optional<Task> TaskRepository::findById(const std::string& id,
                                             bool includeDeleted) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          " WHERE id = ?" + (includeDeleted ? "" : " AND deleted_at IS NULL"));
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToTask(st);
}

Task TaskRepository::update(domain::Task task) const {
    const std::optional<Task> existing = findById(task.id, /*includeDeleted=*/true);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "task not found: '" + task.id + "'");
    }
    if (task.revision != existing->revision) {
        throw EquoraError(ErrorCode::Conflict,
                          "stale revision " + std::to_string(task.revision) + ", expected " +
                              std::to_string(existing->revision) + " for task " + task.id);
    }
    if (isBlank(task.title)) {
        throw EquoraError(ErrorCode::InvalidArgument, "task title must not be blank");
    }

    task.updatedAt = now();
    task.revision += 1;
    task.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE tasks SET title = ?, note = ?, status = ?, priority = ?, "
            "importance = ?, due_at = ?, estimate_minutes = ?, actual_minutes = ?, "
            "project_id = ?, updated_at = ?, revision = ?, last_device_id = ? "
            "WHERE id = ? AND revision = ?");
        st.bind(1, task.title)
            .bind(2, task.note)
            .bind(3, static_cast<std::int64_t>(task.status))
            .bind(4, static_cast<std::int64_t>(task.priority))
            .bind(5, static_cast<std::int64_t>(task.importance))
            .bind(6, task.dueAt)
            .bind(7, task.estimateMinutes)
            .bind(8, static_cast<std::int64_t>(task.actualMinutes))
            .bind(9, task.projectId)
            .bind(10, task.updatedAt)
            .bind(11, task.revision)
            .bind(12, task.lastDeviceId)
            .bind(13, task.id)
            .bind(14, task.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            // 并发窗口内 revision 已被其他写入方推进。
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for task " + task.id);
        }
    }
    tx.commit();
    return task;
}

Task TaskRepository::setDeleted(const std::string& id, bool deleted) const {
    const std::optional<Task> existing = findById(id, /*includeDeleted=*/true);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "task not found: '" + id + "'");
    }
    if (deleted == existing->deletedAt.has_value()) return *existing; // 幂等

    Task updated = *existing;
    updated.revision += 1;
    updated.updatedAt = now();
    updated.lastDeviceId = deviceId_;
    updated.deletedAt = deleted ? std::optional<domain::UtcMillis>(updated.updatedAt)
                                : std::nullopt;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE tasks SET deleted_at = ?, updated_at = ?, revision = ?, "
            "last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, updated.deletedAt)
            .bind(2, updated.updatedAt)
            .bind(3, updated.revision)
            .bind(4, updated.lastDeviceId)
            .bind(5, id)
            .bind(6, existing->revision);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for task " + id);
        }
    }
    tx.commit();
    return updated;
}

void TaskRepository::permanentlyDelete(const std::string& id) const {
    Transaction tx = db_.beginTransaction();
    const auto task = findById(id, true);
    if (!task) throw EquoraError(ErrorCode::NotFound, "task not found");
    if (!task->deletedAt) throw EquoraError(ErrorCode::InvalidArgument, "only trashed tasks can be permanently deleted");
    auto st = db_.prepare("DELETE FROM tasks WHERE id = ? AND deleted_at IS NOT NULL");
    st.bind(1, id);
    st.step();
    tx.commit();
}

std::vector<Task> TaskRepository::listAll(bool includeDeleted) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          (includeDeleted ? "" : " WHERE deleted_at IS NULL") +
                          " ORDER BY created_at, id");
    std::vector<Task> out;
    while (st.step()) {
        out.push_back(rowToTask(st));
    }
    return out;
}

std::vector<Task> TaskRepository::query(const TaskFilter& filter) const {
    // 子句与绑定值按最终 SQL 中的出现顺序同步收集。
    std::vector<std::string> clauses;
    std::vector<std::variant<std::int64_t, std::string>> binds;

    auto addInt = [&](std::int64_t v) { binds.emplace_back(v); };
    auto addText = [&](std::string v) { binds.emplace_back(std::move(v)); };

    if (!filter.includeDeleted) clauses.push_back("deleted_at IS NULL");

    if (filter.dueState == TaskFilter::DueSet) clauses.push_back("due_at IS NOT NULL");
    if (filter.dueState == TaskFilter::DueUnset) clauses.push_back("due_at IS NULL");

    if (filter.dueAfter) {
        clauses.push_back("due_at >= ?");
        addInt(*filter.dueAfter);
    }
    if (filter.dueBefore) {
        clauses.push_back("due_at < ?");
        addInt(*filter.dueBefore);
    }
    if (filter.updatedAfter) {
        clauses.push_back("updated_at >= ?");
        addInt(*filter.updatedAfter);
    }
    if (filter.updatedBefore) {
        clauses.push_back("updated_at < ?");
        addInt(*filter.updatedBefore);
    }
    if (filter.projectId) {
        clauses.push_back("project_id = ?");
        addText(*filter.projectId);
    }
    if (filter.tagId) {
        clauses.push_back(
            "EXISTS (SELECT 1 FROM task_tags tt WHERE tt.task_id = tasks.id AND tt.tag_id = ?)");
        addText(*filter.tagId);
    }
    if (!filter.searchText.empty()) {
        // instr 子串匹配:无 LIKE 通配语义,免转义;lower 实现大小写不敏感。
        clauses.push_back("(instr(lower(title), lower(?)) > 0 OR instr(lower(note), lower(?)) > 0)");
        addText(filter.searchText);
        addText(filter.searchText);
    }

    auto appendStatusList = [&](const char* keyword,
                                const std::vector<domain::TaskStatus>& statuses) {
        if (statuses.empty()) return;
        std::string clause = std::string(keyword) + " (";
        for (std::size_t i = 0; i < statuses.size(); ++i) {
            clause += i == 0 ? "?" : ", ?";
            addInt(static_cast<std::int64_t>(statuses[i]));
        }
        clause += ")";
        clauses.push_back(std::move(clause));
    };
    appendStatusList("status IN", filter.statuses);
    appendStatusList("status NOT IN", filter.excludedStatuses);

    std::string sql = std::string(kSelectColumns);
    if (!clauses.empty()) {
        sql += " WHERE ";
        for (std::size_t i = 0; i < clauses.size(); ++i) {
            sql += i == 0 ? clauses[i] : " AND " + clauses[i];
        }
    }

    switch (filter.sort) {
    case TaskSort::CreatedDesc:  sql += " ORDER BY created_at DESC, id DESC"; break;
    case TaskSort::DueAsc:       sql += " ORDER BY due_at IS NULL, due_at, id"; break;
    case TaskSort::DueDesc:      sql += " ORDER BY due_at IS NULL, due_at DESC, id"; break;
    case TaskSort::PriorityDesc: sql += " ORDER BY priority DESC, created_at, id"; break;
    case TaskSort::UpdatedDesc:  sql += " ORDER BY updated_at DESC, id"; break;
    case TaskSort::CreatedAsc:   sql += " ORDER BY created_at, id"; break;
    }

    const bool useLimit = filter.limit > 0 || filter.offset > 0;
    if (useLimit) sql += " LIMIT ? OFFSET ?";

    auto st = db_.prepare(sql);
    int idx = 1;
    for (const auto& b : binds) {
        std::visit([&](const auto& v) { st.bind(idx, v); }, b);
        ++idx;
    }
    if (useLimit) {
        st.bind(idx++, filter.limit > 0 ? static_cast<std::int64_t>(filter.limit) : -1);
        st.bind(idx++, static_cast<std::int64_t>(filter.offset));
    }

    std::vector<Task> out;
    while (st.step()) {
        out.push_back(rowToTask(st));
    }
    return out;
}

Task TaskRepository::importTask(domain::Task preserved) const {
    if (isBlank(preserved.title) || !Uuid::parse(preserved.id).has_value() ||
        preserved.revision < 1 || preserved.createdAt <= 0 || preserved.updatedAt <= 0) {
        throw EquoraError(ErrorCode::InvalidArgument, "invalid task record for import");
    }
    if (findById(preserved.id, /*includeDeleted=*/true).has_value()) {
        throw EquoraError(ErrorCode::Conflict,
                          "task already exists: '" + preserved.id + "'");
    }
    if (preserved.lastDeviceId.empty()) preserved.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO tasks (id, title, note, status, priority, importance, "
            "due_at, estimate_minutes, actual_minutes, project_id, created_at, "
            "updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, preserved.id)
            .bind(2, preserved.title)
            .bind(3, preserved.note)
            .bind(4, static_cast<std::int64_t>(preserved.status))
            .bind(5, static_cast<std::int64_t>(preserved.priority))
            .bind(6, static_cast<std::int64_t>(preserved.importance))
            .bind(7, preserved.dueAt)
            .bind(8, preserved.estimateMinutes)
            .bind(9, static_cast<std::int64_t>(preserved.actualMinutes))
            .bind(10, preserved.projectId)
            .bind(11, preserved.createdAt)
            .bind(12, preserved.updatedAt)
            .bind(13, preserved.revision)
            .bind(14, preserved.deletedAt)
            .bind(15, preserved.lastDeviceId);
        st.step();
    }
    tx.commit();
    return preserved;
}

} // namespace equora::core
