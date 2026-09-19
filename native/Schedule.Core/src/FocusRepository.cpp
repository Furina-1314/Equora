#include <equora/core/FocusRepository.h>

#include <algorithm>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>

namespace equora::core {

using domain::DistractionItem;
using domain::ErrorCode;
using domain::EquoraError;
using domain::FocusMode;
using domain::FocusProfile;
using domain::FocusSession;
using domain::Interruption;
using domain::SessionState;
using domain::Uuid;
using storage::Statement;
using storage::Transaction;

namespace {

constexpr auto kSessionColumns =
    "SELECT id, task_id, block_id, mode, planned_start, planned_end, actual_start, "
    "actual_end, paused_ms, state, goal, completion_note, completion_level, created_at, "
    "updated_at, revision, deleted_at, last_device_id FROM focus_sessions";

[[nodiscard]] bool isBlank(std::string_view s) {
    return std::all_of(s.begin(), s.end(), [](char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    });
}

} // namespace

FocusRepository::FocusRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

domain::FocusSession FocusRepository::rowToSession(const storage::Statement& st) {
    FocusSession s;
    s.id = st.columnText(0);
    s.taskId = st.isNull(1) ? std::nullopt : std::optional<std::string>(st.columnText(1));
    s.blockId = st.isNull(2) ? std::nullopt : std::optional<std::string>(st.columnText(2));
    s.mode = static_cast<FocusMode>(st.columnInt(3));
    s.plannedStart = st.columnInt(4);
    s.plannedEnd = st.isNull(5) ? std::nullopt
                                : std::optional<domain::UtcMillis>(st.columnInt(5));
    s.actualStart = st.columnInt(6);
    s.actualEnd = st.isNull(7) ? std::nullopt
                               : std::optional<domain::UtcMillis>(st.columnInt(7));
    s.pausedMs = st.columnInt(8);
    s.state = static_cast<SessionState>(st.columnInt(9));
    s.goal = st.columnText(10);
    s.completionNote = st.columnText(11);
    s.completionLevel = static_cast<std::int32_t>(st.columnInt(12));
    s.createdAt = st.columnInt(13);
    s.updatedAt = st.columnInt(14);
    s.revision = st.columnInt(15);
    s.deletedAt = st.isNull(16) ? std::nullopt
                                : std::optional<domain::UtcMillis>(st.columnInt(16));
    s.lastDeviceId = st.columnText(17);
    return s;
}

domain::FocusSession FocusRepository::start(domain::FocusMode mode,
                                            std::int32_t plannedMinutes,
                                            const std::string& goal,
                                            const std::optional<std::string>& taskId,
                                            const std::optional<std::string>& blockId,
                                            domain::UtcMillis now) const {
    if (openSession().has_value()) {
        throw EquoraError(ErrorCode::Conflict, "a focus session is already open");
    }
    if (plannedMinutes <= 0 &&
        (mode == FocusMode::Pomodoro || mode == FocusMode::Deep)) {
        plannedMinutes = mode == FocusMode::Pomodoro ? 25 : 90;
    }

    FocusSession s;
    s.id = Uuid::random().toString();
    s.taskId = taskId;
    s.blockId = blockId;
    s.mode = mode;
    s.plannedStart = now;
    // Flowtime/Untimed 无计划结束;Stopwatch 用 planned 作为显示目标(可无)。
    s.plannedEnd = mode == FocusMode::Flowtime || mode == FocusMode::Untimed
                       ? std::nullopt
                       : std::optional<domain::UtcMillis>(now + plannedMinutes * 60'000LL);
    s.actualStart = now;
    s.pausedMs = 0;
    s.state = SessionState::Running;
    s.goal = goal;
    s.completionLevel = -1;
    s.createdAt = now;
    s.updatedAt = now;
    s.revision = 1;
    s.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO focus_sessions (id, task_id, block_id, mode, planned_start, "
            "planned_end, actual_start, actual_end, paused_ms, state, goal, completion_note, "
            "completion_level, created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, s.id)
            .bind(2, s.taskId)
            .bind(3, s.blockId)
            .bind(4, static_cast<std::int64_t>(s.mode))
            .bind(5, s.plannedStart)
            .bind(6, s.plannedEnd)
            .bind(7, s.actualStart)
            .bind(8, s.actualEnd)
            .bind(9, s.pausedMs)
            .bind(10, static_cast<std::int64_t>(s.state))
            .bind(11, s.goal)
            .bind(12, s.completionNote)
            .bind(13, s.completionLevel)
            .bind(14, s.createdAt)
            .bind(15, s.updatedAt)
            .bind(16, s.revision)
            .bind(17, s.deletedAt)
            .bind(18, s.lastDeviceId);
        st.step();
    }
    tx.commit();
    return s;
}

std::optional<FocusSession> FocusRepository::find(const std::string& id) const {
    auto st = db_.prepare(std::string(kSessionColumns) + " WHERE id = ?");
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToSession(st);
}

std::optional<FocusSession> FocusRepository::openSession() const {
    auto st = db_.prepare(std::string(kSessionColumns) +
                          " WHERE actual_end IS NULL AND deleted_at IS NULL "
                          "ORDER BY actual_start DESC LIMIT 1");
    if (!st.step()) return std::nullopt;
    return rowToSession(st);
}

std::vector<FocusSession> FocusRepository::history(domain::UtcMillis from,
                                                   domain::UtcMillis to,
                                                   std::int32_t limit) const {
    std::string sql = std::string(kSessionColumns) +
                      " WHERE actual_start >= ? AND actual_start < ?"
                      " ORDER BY actual_start DESC";
    if (limit > 0) sql += " LIMIT " + std::to_string(limit);
    auto st = db_.prepare(sql);
    st.bind(1, from).bind(2, to);
    std::vector<FocusSession> out;
    while (st.step()) out.push_back(rowToSession(st));
    return out;
}

domain::FocusSession FocusRepository::loadForTransition(const std::string& id) const {
    auto existing = find(id);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "focus session not found: " + id);
    }
    return *existing;
}

void FocusRepository::writeSession(const FocusSession& s) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE focus_sessions SET task_id = ?, block_id = ?, mode = ?, planned_start = ?, "
            "planned_end = ?, actual_start = ?, actual_end = ?, paused_ms = ?, state = ?, "
            "goal = ?, completion_note = ?, completion_level = ?, updated_at = ?, "
            "revision = ?, last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, s.taskId)
            .bind(2, s.blockId)
            .bind(3, static_cast<std::int64_t>(s.mode))
            .bind(4, s.plannedStart)
            .bind(5, s.plannedEnd)
            .bind(6, s.actualStart)
            .bind(7, s.actualEnd)
            .bind(8, s.pausedMs)
            .bind(9, static_cast<std::int64_t>(s.state))
            .bind(10, s.goal)
            .bind(11, s.completionNote)
            .bind(12, s.completionLevel)
            .bind(13, s.updatedAt)
            .bind(14, s.revision)
            .bind(15, s.lastDeviceId)
            .bind(16, s.id)
            .bind(17, s.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of focus session");
        }
    }
    tx.commit();
}

domain::FocusSession FocusRepository::pause(const std::string& id, domain::UtcMillis now) const {
    FocusSession s = loadForTransition(id);
    if (s.actualEnd.has_value() || s.state != SessionState::Running) {
        throw EquoraError(ErrorCode::Conflict, "session is not running");
    }
    s.state = SessionState::Paused;
    s.updatedAt = now;
    s.revision += 1;
    s.lastDeviceId = deviceId_;
    writeSession(s);
    return s;
}

domain::FocusSession FocusRepository::resume(const std::string& id, domain::UtcMillis now) const {
    FocusSession s = loadForTransition(id);
    if (s.actualEnd.has_value() || s.state != SessionState::Paused) {
        throw EquoraError(ErrorCode::Conflict, "session is not paused");
    }
    // 暂停时长 = now - 上次更新(暂停时刻)。
    s.pausedMs += now - s.updatedAt;
    s.state = SessionState::Running;
    s.updatedAt = now;
    s.revision += 1;
    s.lastDeviceId = deviceId_;
    writeSession(s);
    return s;
}

domain::FocusSession FocusRepository::complete(const std::string& id, domain::UtcMillis now,
                                               const std::string& note,
                                               std::int32_t completionLevel) const {
    FocusSession s = loadForTransition(id);
    if (s.actualEnd.has_value()) {
        throw EquoraError(ErrorCode::Conflict, "session already closed");
    }
    if (s.state == SessionState::Paused) {
        s.pausedMs += now - s.updatedAt;
    }
    if (completionLevel < -1 || completionLevel > 100) {
        throw EquoraError(ErrorCode::InvalidArgument, "completion level must be -1..100");
    }
    s.actualEnd = now;
    s.state = SessionState::Completed;
    s.completionNote = note;
    s.completionLevel = completionLevel;
    s.updatedAt = now;
    s.revision += 1;
    s.lastDeviceId = deviceId_;
    writeSession(s);
    return s;
}

domain::FocusSession FocusRepository::abandon(const std::string& id, domain::UtcMillis now) const {
    FocusSession s = loadForTransition(id);
    if (s.actualEnd.has_value()) {
        throw EquoraError(ErrorCode::Conflict, "session already closed");
    }
    if (s.state == SessionState::Paused) {
        s.pausedMs += now - s.updatedAt;
    }
    s.actualEnd = now;
    s.state = SessionState::Abandoned;
    s.updatedAt = now;
    s.revision += 1;
    s.lastDeviceId = deviceId_;
    writeSession(s);
    return s;
}

int FocusRepository::recoverInterrupted(domain::UtcMillis now) const {
    // 未闭合会话 = 崩溃/断电遗留。以 updated_at(最后活动)收尾为 Abandoned。
    auto st = db_.prepare(std::string(kSessionColumns) +
                          " WHERE actual_end IS NULL AND deleted_at IS NULL");
    std::vector<FocusSession> orphans;
    while (st.step()) orphans.push_back(rowToSession(st));

    for (FocusSession& s : orphans) {
        const domain::UtcMillis lastActivity = s.updatedAt;
        if (s.state == SessionState::Paused) {
            // 暂停中崩溃:暂停时长计到崩溃时刻(即最后活动)。
            // lastActivity 即暂停时刻,无需再加。
        }
        s.actualEnd = lastActivity < s.actualStart ? s.actualStart : lastActivity;
        s.state = SessionState::Abandoned;
        s.revision += 1;
        s.updatedAt = now;
        s.lastDeviceId = deviceId_;
        writeSession(s);
    }
    return static_cast<int>(orphans.size());
}

domain::Interruption FocusRepository::addInterruption(const std::string& sessionId,
                                                      domain::UtcMillis occurredAt,
                                                      std::int64_t durationMs,
                                                      const std::string& reason,
                                                      const std::string& source,
                                                      const std::string& handling) const {
    auto session = find(sessionId);
    if (!session.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "focus session not found: " + sessionId);
    }

    Interruption it;
    it.id = Uuid::random().toString();
    it.sessionId = sessionId;
    it.occurredAt = occurredAt;
    it.durationMs = durationMs < 0 ? 0 : durationMs;
    it.reason = reason;
    it.source = source;
    it.handling = handling;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO interruptions (id, session_id, occurred_at, duration_ms, reason, "
            "source, handling) VALUES (?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, it.id)
            .bind(2, it.sessionId)
            .bind(3, it.occurredAt)
            .bind(4, it.durationMs)
            .bind(5, it.reason)
            .bind(6, it.source)
            .bind(7, it.handling);
        st.step();

        // 会话 updated_at 推进(崩溃恢复的"最后活动"锚点)。
        auto bump = db_.prepare(
            "UPDATE focus_sessions SET updated_at = ?, revision = revision + 1, "
            "last_device_id = ? WHERE id = ?");
        bump.bind(1, occurredAt).bind(2, deviceId_).bind(3, sessionId);
        bump.step();
    }
    tx.commit();
    return it;
}

std::vector<Interruption> FocusRepository::interruptionsOf(const std::string& sessionId) const {
    auto st = db_.prepare(
        "SELECT id, session_id, occurred_at, duration_ms, reason, source, handling "
        "FROM interruptions WHERE session_id = ? ORDER BY occurred_at");
    st.bind(1, sessionId);
    std::vector<Interruption> out;
    while (st.step()) {
        Interruption it;
        it.id = st.columnText(0);
        it.sessionId = st.columnText(1);
        it.occurredAt = st.columnInt(2);
        it.durationMs = st.columnInt(3);
        it.reason = st.columnText(4);
        it.source = st.columnText(5);
        it.handling = st.columnText(6);
        out.push_back(std::move(it));
    }
    return out;
}

// ---- 分心捕获 ----

domain::DistractionItem FocusRepository::rowToDistraction(const storage::Statement& st) {
    DistractionItem d;
    d.id = st.columnText(0);
    d.sessionId = st.isNull(1) ? std::nullopt : std::optional<std::string>(st.columnText(1));
    d.content = st.columnText(2);
    d.capturedAt = st.columnInt(3);
    d.resolution = static_cast<std::int32_t>(st.columnInt(4));
    d.resolvedRef = st.columnText(5);
    d.createdAt = st.columnInt(6);
    d.updatedAt = st.columnInt(7);
    d.revision = st.columnInt(8);
    d.deletedAt = st.isNull(9) ? std::nullopt
                               : std::optional<domain::UtcMillis>(st.columnInt(9));
    d.lastDeviceId = st.columnText(10);
    return d;
}

domain::DistractionItem FocusRepository::capture(const std::string& content,
                                                 const std::optional<std::string>& sessionId,
                                                 domain::UtcMillis now) const {
    if (isBlank(content)) {
        throw EquoraError(ErrorCode::InvalidArgument, "distraction content must not be blank");
    }
    DistractionItem d;
    d.id = Uuid::random().toString();
    d.sessionId = sessionId;
    d.content = content;
    d.capturedAt = now;
    d.resolution = 0;
    d.createdAt = now;
    d.updatedAt = now;
    d.revision = 1;
    d.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO distraction_inbox_items (id, session_id, content, captured_at, "
            "resolution, resolved_ref, created_at, updated_at, revision, deleted_at, "
            "last_device_id) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, d.id)
            .bind(2, d.sessionId)
            .bind(3, d.content)
            .bind(4, d.capturedAt)
            .bind(5, d.resolution)
            .bind(6, d.resolvedRef)
            .bind(7, d.createdAt)
            .bind(8, d.updatedAt)
            .bind(9, d.revision)
            .bind(10, d.deletedAt)
            .bind(11, d.lastDeviceId);
        st.step();
    }
    tx.commit();
    return d;
}

std::vector<DistractionItem> FocusRepository::pendingDistractions(std::int32_t limit) const {
    auto st = db_.prepare(
        std::string("SELECT id, session_id, content, captured_at, resolution, resolved_ref, "
                    "created_at, updated_at, revision, deleted_at, last_device_id "
                    "FROM distraction_inbox_items") +
        " WHERE resolution = 0 AND deleted_at IS NULL ORDER BY captured_at" +
        (limit > 0 ? " LIMIT " + std::to_string(limit) : ""));
    std::vector<DistractionItem> out;
    while (st.step()) out.push_back(rowToDistraction(st));
    return out;
}

domain::DistractionItem FocusRepository::resolveDistraction(const std::string& id,
                                                            std::int32_t resolution,
                                                            const std::string& resolvedRef) const {
    auto st = db_.prepare(
        "SELECT id, session_id, content, captured_at, resolution, resolved_ref, created_at, "
        "updated_at, revision, deleted_at, last_device_id FROM distraction_inbox_items "
        "WHERE id = ?");
    st.bind(1, id);
    if (!st.step()) {
        throw EquoraError(ErrorCode::NotFound, "distraction item not found: " + id);
    }
    DistractionItem d = rowToDistraction(st);
    if (d.resolution != 0) {
        throw EquoraError(ErrorCode::Conflict, "distraction already resolved");
    }
    if (resolution < 1 || resolution > 5) {
        throw EquoraError(ErrorCode::InvalidArgument, "resolution must be 1..5");
    }

    d.resolution = resolution;
    d.resolvedRef = resolvedRef;
    d.updatedAt = domain::utc::now();
    d.revision += 1;
    d.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto up = db_.prepare(
            "UPDATE distraction_inbox_items SET resolution = ?, resolved_ref = ?, "
            "updated_at = ?, revision = ?, last_device_id = ? WHERE id = ? AND revision = ?");
        up.bind(1, d.resolution)
            .bind(2, d.resolvedRef)
            .bind(3, d.updatedAt)
            .bind(4, d.revision)
            .bind(5, d.lastDeviceId)
            .bind(6, id)
            .bind(7, d.revision - 1);
        up.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of distraction");
        }
    }
    tx.commit();
    return d;
}

// ---- 专注预设 ----

domain::FocusProfile FocusRepository::rowToProfile(const storage::Statement& st) {
    FocusProfile p;
    p.id = st.columnText(0);
    p.name = st.columnText(1);
    p.mode = static_cast<FocusMode>(st.columnInt(2));
    p.plannedMinutes = static_cast<std::int32_t>(st.columnInt(3));
    p.breakMinutes = static_cast<std::int32_t>(st.columnInt(4));
    p.allowedApps = st.columnText(5);
    p.blockedApps = st.columnText(6);
    p.allowedSites = st.columnText(7);
    p.blockedSites = st.columnText(8);
    p.notifyPolicy = static_cast<std::int32_t>(st.columnInt(9));
    p.isDefault = st.columnInt(10) != 0;
    p.createdAt = st.columnInt(11);
    p.updatedAt = st.columnInt(12);
    p.revision = st.columnInt(13);
    p.deletedAt = st.isNull(14) ? std::nullopt
                                : std::optional<domain::UtcMillis>(st.columnInt(14));
    p.lastDeviceId = st.columnText(15);
    return p;
}

domain::FocusProfile FocusRepository::createProfile(domain::FocusProfile draft) const {
    if (isBlank(draft.name)) {
        throw EquoraError(ErrorCode::InvalidArgument, "profile name must not be blank");
    }
    if (findProfileByName(draft.name).has_value()) {
        throw EquoraError(ErrorCode::Conflict,
                          "profile name already exists: " + draft.name);
    }
    draft.id = Uuid::random().toString();
    const auto now = domain::utc::now();
    draft.createdAt = now;
    draft.updatedAt = now;
    draft.revision = 1;
    draft.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO focus_profiles (id, name, mode, planned_minutes, break_minutes, "
            "allowed_apps, blocked_apps, allowed_sites, blocked_sites, notify_policy, "
            "is_default, created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.name)
            .bind(3, static_cast<std::int64_t>(draft.mode))
            .bind(4, draft.plannedMinutes)
            .bind(5, draft.breakMinutes)
            .bind(6, draft.allowedApps.empty() ? "[]" : draft.allowedApps)
            .bind(7, draft.blockedApps.empty() ? "[]" : draft.blockedApps)
            .bind(8, draft.allowedSites.empty() ? "[]" : draft.allowedSites)
            .bind(9, draft.blockedSites.empty() ? "[]" : draft.blockedSites)
            .bind(10, draft.notifyPolicy)
            .bind(11, draft.isDefault ? 1 : 0)
            .bind(12, draft.createdAt)
            .bind(13, draft.updatedAt)
            .bind(14, draft.revision)
            .bind(15, draft.deletedAt)
            .bind(16, draft.lastDeviceId);
        st.step();
    }
    tx.commit();
    return draft;
}

std::optional<FocusProfile> FocusRepository::findProfile(const std::string& id) const {
    auto st = db_.prepare(
        "SELECT id, name, mode, planned_minutes, break_minutes, allowed_apps, blocked_apps, "
        "allowed_sites, blocked_sites, notify_policy, is_default, created_at, updated_at, "
        "revision, deleted_at, last_device_id FROM focus_profiles "
        "WHERE id = ? AND deleted_at IS NULL");
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToProfile(st);
}

std::optional<FocusProfile> FocusRepository::findProfileByName(
    const std::string& name) const {
    auto st = db_.prepare(
        "SELECT id, name, mode, planned_minutes, break_minutes, allowed_apps, blocked_apps, "
        "allowed_sites, blocked_sites, notify_policy, is_default, created_at, updated_at, "
        "revision, deleted_at, last_device_id FROM focus_profiles "
        "WHERE name = ? COLLATE NOCASE AND deleted_at IS NULL");
    st.bind(1, name);
    if (!st.step()) return std::nullopt;
    return rowToProfile(st);
}

domain::FocusProfile FocusRepository::updateProfile(domain::FocusProfile p) const {
    auto existing = findProfile(p.id);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "profile not found: " + p.id);
    }
    if (p.revision != existing->revision) {
        throw EquoraError(ErrorCode::Conflict, "stale revision for focus profile");
    }
    if (isBlank(p.name)) {
        throw EquoraError(ErrorCode::InvalidArgument, "profile name must not be blank");
    }
    p.updatedAt = domain::utc::now();
    p.revision += 1;
    p.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto up = db_.prepare(
            "UPDATE focus_profiles SET name = ?, mode = ?, planned_minutes = ?, "
            "break_minutes = ?, allowed_apps = ?, blocked_apps = ?, allowed_sites = ?, "
            "blocked_sites = ?, notify_policy = ?, is_default = ?, updated_at = ?, "
            "revision = ?, last_device_id = ? WHERE id = ? AND revision = ?");
        up.bind(1, p.name)
            .bind(2, static_cast<std::int64_t>(p.mode))
            .bind(3, p.plannedMinutes)
            .bind(4, p.breakMinutes)
            .bind(5, p.allowedApps)
            .bind(6, p.blockedApps)
            .bind(7, p.allowedSites)
            .bind(8, p.blockedSites)
            .bind(9, p.notifyPolicy)
            .bind(10, p.isDefault ? 1 : 0)
            .bind(11, p.updatedAt)
            .bind(12, p.revision)
            .bind(13, p.lastDeviceId)
            .bind(14, p.id)
            .bind(15, p.revision - 1);
        up.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of focus profile");
        }
    }
    tx.commit();
    return p;
}

void FocusRepository::deleteProfile(const std::string& id) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE focus_profiles SET deleted_at = ?, updated_at = ?, "
            "revision = revision + 1, last_device_id = ? WHERE id = ? AND deleted_at IS NULL");
        st.bind(1, domain::utc::now())
            .bind(2, domain::utc::now())
            .bind(3, deviceId_)
            .bind(4, id);
        st.step();
    }
    tx.commit();
}

std::vector<FocusProfile> FocusRepository::listProfiles(bool includeDeleted) const {
    auto st = db_.prepare(
        "SELECT id, name, mode, planned_minutes, break_minutes, allowed_apps, blocked_apps, "
        "allowed_sites, blocked_sites, notify_policy, is_default, created_at, updated_at, "
        "revision, deleted_at, last_device_id FROM focus_profiles" +
        std::string(includeDeleted ? "" : " WHERE deleted_at IS NULL") +
        " ORDER BY is_default DESC, name COLLATE NOCASE");
    std::vector<FocusProfile> out;
    while (st.step()) out.push_back(rowToProfile(st));
    return out;
}

} // namespace equora::core
