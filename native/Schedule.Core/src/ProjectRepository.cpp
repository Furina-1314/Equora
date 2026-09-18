#include <equora/core/ProjectRepository.h>

#include <algorithm>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>

namespace equora::core {

using domain::ErrorCode;
using domain::EquoraError;
using domain::Project;
using domain::Uuid;
using domain::utc::now;
using storage::Statement;
using storage::Transaction;

namespace {

constexpr auto kSelectColumns =
    "SELECT id, name, color, goal, status, archived_at, created_at, updated_at, "
    "revision, deleted_at, last_device_id FROM projects";

[[nodiscard]] bool isBlank(std::string_view s) {
    return std::all_of(s.begin(), s.end(),
                       [](char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; });
}

} // namespace

ProjectRepository::ProjectRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

Project ProjectRepository::rowToProject(const storage::Statement& st) {
    Project p;
    p.id = st.columnText(0);
    p.name = st.columnText(1);
    p.color = st.columnText(2);
    p.goal = st.columnText(3);
    p.status = static_cast<domain::ProjectStatus>(st.columnInt(4));
    p.archivedAt = st.isNull(5)
                       ? std::nullopt
                       : std::optional<domain::UtcMillis>(st.columnInt(5));
    p.createdAt = st.columnInt(6);
    p.updatedAt = st.columnInt(7);
    p.revision = st.columnInt(8);
    p.deletedAt = st.isNull(9)
                      ? std::nullopt
                      : std::optional<domain::UtcMillis>(st.columnInt(9));
    p.lastDeviceId = st.columnText(10);
    return p;
}

Project ProjectRepository::create(domain::Project draft) const {
    if (isBlank(draft.name)) {
        throw EquoraError(ErrorCode::InvalidArgument, "project name must not be blank");
    }
    draft.id = Uuid::random().toString();
    draft.status = domain::ProjectStatus::Active;
    draft.archivedAt = std::nullopt;
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;
    draft.deletedAt = std::nullopt;
    draft.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO projects (id, name, color, goal, status, archived_at, "
            "created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.name)
            .bind(3, draft.color)
            .bind(4, draft.goal)
            .bind(5, static_cast<std::int64_t>(draft.status))
            .bind(6, draft.archivedAt)
            .bind(7, draft.createdAt)
            .bind(8, draft.updatedAt)
            .bind(9, draft.revision)
            .bind(10, draft.deletedAt)
            .bind(11, draft.lastDeviceId);
        st.step();
    }
    tx.commit();
    return draft;
}

std::optional<Project> ProjectRepository::findById(const std::string& id,
                                                   bool includeDeleted) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          " WHERE id = ?" +
                          (includeDeleted ? "" : " AND deleted_at IS NULL"));
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToProject(st);
}

Project ProjectRepository::update(domain::Project project) const {
    const std::optional<Project> existing = findById(project.id, /*includeDeleted=*/true);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "project not found: '" + project.id + "'");
    }
    if (project.revision != existing->revision) {
        throw EquoraError(ErrorCode::Conflict,
                          "stale revision " + std::to_string(project.revision) +
                              ", expected " + std::to_string(existing->revision) +
                              " for project " + project.id);
    }
    if (isBlank(project.name)) {
        throw EquoraError(ErrorCode::InvalidArgument, "project name must not be blank");
    }

    project.updatedAt = now();
    project.revision += 1;
    project.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE projects SET name = ?, color = ?, goal = ?, status = ?, "
            "archived_at = ?, updated_at = ?, revision = ?, last_device_id = ? "
            "WHERE id = ? AND revision = ?");
        st.bind(1, project.name)
            .bind(2, project.color)
            .bind(3, project.goal)
            .bind(4, static_cast<std::int64_t>(project.status))
            .bind(5, project.archivedAt)
            .bind(6, project.updatedAt)
            .bind(7, project.revision)
            .bind(8, project.lastDeviceId)
            .bind(9, project.id)
            .bind(10, project.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for project " + project.id);
        }
    }
    tx.commit();
    return project;
}

Project ProjectRepository::setArchived(const std::string& id, bool archived) const {
    const std::optional<Project> existing = findById(id, /*includeDeleted=*/true);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "project not found: '" + id + "'");
    }
    if (archived == (existing->status == domain::ProjectStatus::Archived)) return *existing;

    Project updated = *existing;
    updated.status = archived ? domain::ProjectStatus::Archived
                              : domain::ProjectStatus::Active;
    updated.archivedAt = archived ? std::optional<domain::UtcMillis>(now())
                                  : std::nullopt;
    updated.revision += 1;
    updated.updatedAt = now();
    updated.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE projects SET status = ?, archived_at = ?, updated_at = ?, "
            "revision = ?, last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, static_cast<std::int64_t>(updated.status))
            .bind(2, updated.archivedAt)
            .bind(3, updated.updatedAt)
            .bind(4, updated.revision)
            .bind(5, updated.lastDeviceId)
            .bind(6, id)
            .bind(7, existing->revision);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for project " + id);
        }
    }
    tx.commit();
    return updated;
}

Project ProjectRepository::setDeleted(const std::string& id, bool deleted) const {
    const std::optional<Project> existing = findById(id, /*includeDeleted=*/true);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "project not found: '" + id + "'");
    }
    if (deleted == existing->deletedAt.has_value()) return *existing;

    Project updated = *existing;
    updated.revision += 1;
    updated.updatedAt = now();
    updated.lastDeviceId = deviceId_;
    updated.deletedAt = deleted ? std::optional<domain::UtcMillis>(updated.updatedAt)
                                : std::nullopt;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE projects SET deleted_at = ?, updated_at = ?, revision = ?, "
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
                              "concurrent update detected for project " + id);
        }
    }
    tx.commit();
    return updated;
}

std::vector<Project> ProjectRepository::list(bool includeArchived,
                                             bool includeDeleted) const {
    std::string sql = std::string(kSelectColumns);
    std::vector<std::string> clauses;
    if (!includeDeleted) clauses.push_back("deleted_at IS NULL");
    if (!includeArchived) clauses.push_back("status = 0");
    if (!clauses.empty()) {
        sql += " WHERE ";
        for (std::size_t i = 0; i < clauses.size(); ++i) {
            sql += i == 0 ? clauses[i] : " AND " + clauses[i];
        }
    }
    sql += " ORDER BY name COLLATE NOCASE, id";

    auto st = db_.prepare(sql);
    std::vector<Project> out;
    while (st.step()) {
        out.push_back(rowToProject(st));
    }
    return out;
}

} // namespace equora::core
