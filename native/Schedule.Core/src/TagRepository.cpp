#include <equora/core/TagRepository.h>

#include <algorithm>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>

namespace equora::core {

using domain::ErrorCode;
using domain::EquoraError;
using domain::Tag;
using domain::Uuid;
using domain::utc::now;
using storage::Statement;
using storage::Transaction;

namespace {

constexpr auto kSelectColumns =
    "SELECT id, name, color, created_at, updated_at, revision, deleted_at, "
    "last_device_id FROM tags";

[[nodiscard]] bool isBlank(std::string_view s) {
    return std::all_of(s.begin(), s.end(),
                       [](char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; });
}

} // namespace

TagRepository::TagRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

Tag TagRepository::rowToTag(const storage::Statement& st) {
    Tag t;
    t.id = st.columnText(0);
    t.name = st.columnText(1);
    t.color = st.columnText(2);
    t.createdAt = st.columnInt(3);
    t.updatedAt = st.columnInt(4);
    t.revision = st.columnInt(5);
    t.deletedAt = st.isNull(6)
                      ? std::nullopt
                      : std::optional<domain::UtcMillis>(st.columnInt(6));
    t.lastDeviceId = st.columnText(7);
    return t;
}

Tag TagRepository::create(domain::Tag draft) const {
    if (isBlank(draft.name)) {
        throw EquoraError(ErrorCode::InvalidArgument, "tag name must not be blank");
    }
    if (findByName(draft.name, /*includeDeleted=*/true).has_value()) {
        throw EquoraError(ErrorCode::Conflict,
                          "tag name already exists: '" + draft.name + "'");
    }

    draft.id = Uuid::random().toString();
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;
    draft.deletedAt = std::nullopt;
    draft.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO tags (id, name, color, created_at, updated_at, revision, "
            "deleted_at, last_device_id) VALUES (?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.name)
            .bind(3, draft.color)
            .bind(4, draft.createdAt)
            .bind(5, draft.updatedAt)
            .bind(6, draft.revision)
            .bind(7, draft.deletedAt)
            .bind(8, draft.lastDeviceId);
        st.step();
    }
    tx.commit();
    return draft;
}

std::optional<Tag> TagRepository::findById(const std::string& id, bool includeDeleted) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          " WHERE id = ?" +
                          (includeDeleted ? "" : " AND deleted_at IS NULL"));
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToTag(st);
}

std::optional<Tag> TagRepository::findByName(const std::string& name,
                                             bool includeDeleted) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          " WHERE name = ? COLLATE NOCASE" +
                          (includeDeleted ? "" : " AND deleted_at IS NULL"));
    st.bind(1, name);
    if (!st.step()) return std::nullopt;
    return rowToTag(st);
}

Tag TagRepository::update(domain::Tag tag) const {
    const std::optional<Tag> existing = findById(tag.id, /*includeDeleted=*/true);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "tag not found: '" + tag.id + "'");
    }
    if (tag.revision != existing->revision) {
        throw EquoraError(ErrorCode::Conflict,
                          "stale revision " + std::to_string(tag.revision) + ", expected " +
                              std::to_string(existing->revision) + " for tag " + tag.id);
    }
    if (isBlank(tag.name)) {
        throw EquoraError(ErrorCode::InvalidArgument, "tag name must not be blank");
    }
    // 改名唯一性:与其他标签冲突(含墓碑)时拒绝。
    if (tag.name != existing->name) {
        if (auto clash = findByName(tag.name, /*includeDeleted=*/true);
            clash.has_value() && clash->id != tag.id) {
            throw EquoraError(ErrorCode::Conflict,
                              "tag name already exists: '" + tag.name + "'");
        }
    }

    tag.updatedAt = now();
    tag.revision += 1;
    tag.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE tags SET name = ?, color = ?, updated_at = ?, revision = ?, "
            "last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, tag.name)
            .bind(2, tag.color)
            .bind(3, tag.updatedAt)
            .bind(4, tag.revision)
            .bind(5, tag.lastDeviceId)
            .bind(6, tag.id)
            .bind(7, tag.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for tag " + tag.id);
        }
    }
    tx.commit();
    return tag;
}

Tag TagRepository::setDeleted(const std::string& id, bool deleted) const {
    const std::optional<Tag> existing = findById(id, /*includeDeleted=*/true);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "tag not found: '" + id + "'");
    }
    if (deleted == existing->deletedAt.has_value()) return *existing;

    Tag updated = *existing;
    updated.revision += 1;
    updated.updatedAt = now();
    updated.lastDeviceId = deviceId_;
    updated.deletedAt = deleted ? std::optional<domain::UtcMillis>(updated.updatedAt)
                                : std::nullopt;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE tags SET deleted_at = ?, updated_at = ?, revision = ?, "
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
                              "concurrent update detected for tag " + id);
        }
    }
    tx.commit();
    return updated;
}

std::vector<Tag> TagRepository::list(bool includeDeleted) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          (includeDeleted ? "" : " WHERE deleted_at IS NULL") +
                          " ORDER BY name COLLATE NOCASE, id");
    std::vector<Tag> out;
    while (st.step()) {
        out.push_back(rowToTag(st));
    }
    return out;
}

void TagRepository::assignToTask(const std::string& taskId, const std::string& tagId) const {
    // 双方必须存在且未删除(抛错带清晰原因)。
    {
        auto st = db_.prepare("SELECT COUNT(*) FROM tasks WHERE id = ? AND deleted_at IS NULL");
        st.bind(1, taskId);
        st.step();
        if (st.columnInt(0) != 1) {
            throw EquoraError(ErrorCode::NotFound, "task not found: '" + taskId + "'");
        }
    }
    if (!findById(tagId).has_value()) {
        throw EquoraError(ErrorCode::NotFound, "tag not found: '" + tagId + "'");
    }

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT OR IGNORE INTO task_tags (task_id, tag_id, created_at) VALUES (?, ?, ?)");
        st.bind(1, taskId).bind(2, tagId).bind(3, now());
        st.step();
    }
    tx.commit();
}

void TagRepository::unassignFromTask(const std::string& taskId,
                                     const std::string& tagId) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare("DELETE FROM task_tags WHERE task_id = ? AND tag_id = ?");
        st.bind(1, taskId).bind(2, tagId);
        st.step();
    }
    tx.commit();
}

std::vector<Tag> TagRepository::forTask(const std::string& taskId) const {
    // JOIN 后列名必须限定 tags.,与 task_tags 的 created_at 区分。
    auto st = db_.prepare(
        "SELECT tags.id, tags.name, tags.color, tags.created_at, tags.updated_at, "
        "tags.revision, tags.deleted_at, tags.last_device_id "
        "FROM tags JOIN task_tags tt ON tt.tag_id = tags.id"
        " WHERE tt.task_id = ? AND tags.deleted_at IS NULL"
        " ORDER BY tags.name COLLATE NOCASE, tags.id");
    st.bind(1, taskId);
    std::vector<Tag> out;
    while (st.step()) {
        out.push_back(rowToTag(st));
    }
    return out;
}

} // namespace equora::core
