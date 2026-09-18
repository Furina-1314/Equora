#include <equora/core/ChecklistRepository.h>

#include <algorithm>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>

namespace equora::core {

using domain::ChecklistItem;
using domain::ErrorCode;
using domain::EquoraError;
using domain::Uuid;
using domain::utc::now;
using storage::Statement;
using storage::Transaction;

namespace {

constexpr auto kSelectColumns =
    "SELECT id, task_id, content, is_checked, sort_order, created_at, updated_at, "
    "revision, deleted_at, last_device_id FROM checklist_items";

[[nodiscard]] bool isBlank(std::string_view s) {
    return std::all_of(s.begin(), s.end(),
                       [](char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; });
}

} // namespace

ChecklistRepository::ChecklistRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

ChecklistItem ChecklistRepository::rowToItem(const storage::Statement& st) {
    ChecklistItem it;
    it.id = st.columnText(0);
    it.taskId = st.columnText(1);
    it.content = st.columnText(2);
    it.isChecked = st.columnInt(3) != 0;
    it.sortOrder = static_cast<std::int32_t>(st.columnInt(4));
    it.createdAt = st.columnInt(5);
    it.updatedAt = st.columnInt(6);
    it.revision = st.columnInt(7);
    it.deletedAt = st.isNull(8)
                       ? std::nullopt
                       : std::optional<domain::UtcMillis>(st.columnInt(8));
    it.lastDeviceId = st.columnText(9);
    return it;
}

ChecklistItem ChecklistRepository::add(const std::string& taskId, std::string content) const {
    if (isBlank(content)) {
        throw EquoraError(ErrorCode::InvalidArgument, "checklist content must not be blank");
    }
    {
        auto st = db_.prepare("SELECT COUNT(*) FROM tasks WHERE id = ? AND deleted_at IS NULL");
        st.bind(1, taskId);
        st.step();
        if (st.columnInt(0) != 1) {
            throw EquoraError(ErrorCode::NotFound, "task not found: '" + taskId + "'");
        }
    }

    ChecklistItem item;
    item.id = Uuid::random().toString();
    item.taskId = taskId;
    item.content = std::move(content);
    item.isChecked = false;
    item.createdAt = now();
    item.updatedAt = item.createdAt;
    item.revision = 1;
    item.deletedAt = std::nullopt;
    item.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto maxSt = db_.prepare(
            "SELECT COALESCE(MAX(sort_order), 0) FROM checklist_items "
            "WHERE task_id = ? AND deleted_at IS NULL");
        maxSt.bind(1, taskId);
        maxSt.step();
        item.sortOrder = static_cast<std::int32_t>(maxSt.columnInt(0)) + 1;

        auto st = db_.prepare(
            "INSERT INTO checklist_items (id, task_id, content, is_checked, sort_order, "
            "created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, item.id)
            .bind(2, item.taskId)
            .bind(3, item.content)
            .bind(4, static_cast<std::int64_t>(item.isChecked ? 1 : 0))
            .bind(5, static_cast<std::int64_t>(item.sortOrder))
            .bind(6, item.createdAt)
            .bind(7, item.updatedAt)
            .bind(8, item.revision)
            .bind(9, item.deletedAt)
            .bind(10, item.lastDeviceId);
        st.step();
    }
    tx.commit();
    return item;
}

std::optional<ChecklistItem> ChecklistRepository::findById(const std::string& id) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          " WHERE id = ? AND deleted_at IS NULL");
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToItem(st);
}

std::vector<ChecklistItem> ChecklistRepository::listForTask(const std::string& taskId) const {
    auto st = db_.prepare(std::string(kSelectColumns) +
                          " WHERE task_id = ? AND deleted_at IS NULL ORDER BY sort_order, id");
    st.bind(1, taskId);
    std::vector<ChecklistItem> out;
    while (st.step()) {
        out.push_back(rowToItem(st));
    }
    return out;
}

ChecklistItem ChecklistRepository::update(domain::ChecklistItem item) const {
    auto st = db_.prepare(std::string(kSelectColumns) + " WHERE id = ?");
    st.bind(1, item.id);
    if (!st.step()) {
        throw EquoraError(ErrorCode::NotFound, "checklist item not found: '" + item.id + "'");
    }
    const ChecklistItem existing = rowToItem(st);
    if (existing.deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "checklist item deleted: '" + item.id + "'");
    }
    if (item.revision != existing.revision) {
        throw EquoraError(ErrorCode::Conflict,
                          "stale revision " + std::to_string(item.revision) + ", expected " +
                              std::to_string(existing.revision) + " for item " + item.id);
    }
    if (isBlank(item.content)) {
        throw EquoraError(ErrorCode::InvalidArgument, "checklist content must not be blank");
    }

    item.updatedAt = now();
    item.revision += 1;
    item.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto up = db_.prepare(
            "UPDATE checklist_items SET content = ?, is_checked = ?, sort_order = ?, "
            "updated_at = ?, revision = ?, last_device_id = ? WHERE id = ? AND revision = ?");
        up.bind(1, item.content)
            .bind(2, static_cast<std::int64_t>(item.isChecked ? 1 : 0))
            .bind(3, static_cast<std::int64_t>(item.sortOrder))
            .bind(4, item.updatedAt)
            .bind(5, item.revision)
            .bind(6, item.lastDeviceId)
            .bind(7, item.id)
            .bind(8, item.revision - 1);
        up.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for item " + item.id);
        }
    }
    tx.commit();
    return item;
}

void ChecklistRepository::remove(const std::string& id) const {
    auto st = db_.prepare(std::string(kSelectColumns) + " WHERE id = ?");
    st.bind(1, id);
    if (!st.step()) {
        throw EquoraError(ErrorCode::NotFound, "checklist item not found: '" + id + "'");
    }
    const ChecklistItem existing = rowToItem(st);
    if (existing.deletedAt.has_value()) return; // 幂等

    Transaction tx = db_.beginTransaction();
    {
        auto up = db_.prepare(
            "UPDATE checklist_items SET deleted_at = ?, updated_at = ?, revision = ?, "
            "last_device_id = ? WHERE id = ? AND revision = ?");
        up.bind(1, now())
            .bind(2, now())
            .bind(3, existing.revision + 1)
            .bind(4, deviceId_)
            .bind(5, id)
            .bind(6, existing.revision);
        up.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict,
                              "concurrent update detected for item " + id);
        }
    }
    tx.commit();
}

void ChecklistRepository::reorder(const std::string& taskId,
                                  const std::vector<std::string>& orderedIds) const {
    const std::vector<ChecklistItem> current = listForTask(taskId);
    if (orderedIds.size() != current.size()) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "reorder list must cover exactly the active items of the task");
    }
    for (const auto& wanted : orderedIds) {
        if (std::none_of(current.begin(), current.end(),
                         [&](const ChecklistItem& it) { return it.id == wanted; })) {
            throw EquoraError(ErrorCode::InvalidArgument,
                              "reorder list contains unknown item: '" + wanted + "'");
        }
    }

    Transaction tx = db_.beginTransaction();
    for (std::size_t i = 0; i < orderedIds.size(); ++i) {
        auto up = db_.prepare(
            "UPDATE checklist_items SET sort_order = ?, updated_at = ?, revision = revision + 1,"
            " last_device_id = ? WHERE id = ? AND deleted_at IS NULL");
        up.bind(1, static_cast<std::int64_t>(i + 1))
            .bind(2, now())
            .bind(3, deviceId_)
            .bind(4, orderedIds[i]);
        up.step();
    }
    tx.commit();
}

} // namespace equora::core
