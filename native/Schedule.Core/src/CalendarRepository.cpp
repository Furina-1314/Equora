#include <equora/core/CalendarRepository.h>

#include <algorithm>

#include <equora/domain/Error.h>
#include <equora/domain/Uuid.h>
#include <equora/scheduling/Recurrence.h>

namespace equora::core {

using domain::ErrorCode;
using domain::EquoraError;
using domain::Uuid;
using domain::utc::now;
using scheduling::Span;
using storage::Statement;
using storage::Transaction;

namespace {

constexpr auto kBlockColumns =
    "SELECT id, task_id, calendar_id, start_at, end_at, prepare_minutes, buffer_minutes, "
    "actual_minutes, note, created_at, updated_at, revision, deleted_at, last_device_id, title, batch_id "
    "FROM time_blocks";
constexpr auto kEventColumns =
    "SELECT id, title, location, note, calendar_id, start_at, end_at, is_all_day, "
    "created_at, updated_at, revision, deleted_at, last_device_id FROM events";
constexpr auto kRuleColumns =
    "SELECT id, host_type, host_id, freq, interval, by_weekday, month_mode, month_nth, "
    "month_weekday, until_utc, max_count, complete_recur_days, excluded_dates, "
    "created_at, updated_at, revision, deleted_at, last_device_id FROM recurrence_rules";

[[nodiscard]] std::vector<domain::Weekday> parseWeekdayCsv(const std::string& csv) {
    std::vector<domain::Weekday> out;
    std::size_t pos = 0;
    while (pos < csv.size()) {
        const auto next = csv.find(',', pos);
        const auto part = csv.substr(pos, next == std::string::npos ? csv.size() - pos : next - pos);
        if (!part.empty()) out.push_back(static_cast<domain::Weekday>(atoi(part.c_str())));
        if (next == std::string::npos) break;
        pos = next + 1;
    }
    return out;
}

[[nodiscard]] std::string weekdaysToCsv(const std::vector<domain::Weekday>& wds) {
    std::string out;
    for (std::size_t i = 0; i < wds.size(); ++i) {
        if (i > 0) out.push_back(',');
        out += std::to_string(static_cast<int>(wds[i]));
    }
    return out;
}

[[nodiscard]] std::vector<std::string> parseDateCsv(const std::string& csv) {
    std::vector<std::string> out;
    std::size_t pos = 0;
    while (pos < csv.size()) {
        const auto next = csv.find(',', pos);
        const auto part = csv.substr(pos, next == std::string::npos ? csv.size() - pos : next - pos);
        if (!part.empty()) out.push_back(part);
        if (next == std::string::npos) break;
        pos = next + 1;
    }
    return out;
}

[[nodiscard]] std::string datesToCsv(const std::vector<std::string>& dates) {
    std::string out;
    for (std::size_t i = 0; i < dates.size(); ++i) {
        if (i > 0) out.push_back(',');
        out += dates[i];
    }
    return out;
}

template <typename T>
void bumpAndCheck(const T& existing, const T& incoming, const char* what) {
    if (incoming.revision != existing.revision) {
        throw EquoraError(ErrorCode::Conflict,
                          std::string("stale revision for ") + what);
    }
}

} // namespace

CalendarRepository::CalendarRepository(const storage::Database& db, std::string deviceId)
    : db_(db), deviceId_(std::move(deviceId)) {}

// ---- 日历 ----

domain::Calendar CalendarRepository::createCalendar(domain::Calendar draft) const {
    if (draft.name.empty()) {
        throw EquoraError(ErrorCode::InvalidArgument, "calendar name must not be empty");
    }
    draft.id = Uuid::random().toString();
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO calendars (id, name, color, source, is_visible, created_at, "
            "updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.name)
            .bind(3, draft.color)
            .bind(4, draft.source)
            .bind(5, draft.isVisible ? 1 : 0)
            .bind(6, draft.createdAt)
            .bind(7, draft.updatedAt)
            .bind(8, draft.revision)
            .bind(9, draft.deletedAt)
            .bind(10, deviceId_);
        st.step();
    }
    tx.commit();
    draft.lastDeviceId = deviceId_;
    return draft;
}

std::vector<domain::Calendar> CalendarRepository::listCalendars(bool includeDeleted) const {
    auto st = db_.prepare(std::string("SELECT id, name, color, source, is_visible, "
                                      "created_at, updated_at, revision, deleted_at, "
                                      "last_device_id FROM calendars") +
                          (includeDeleted ? "" : " WHERE deleted_at IS NULL") +
                          " ORDER BY name");
    std::vector<domain::Calendar> out;
    while (st.step()) {
        domain::Calendar c;
        c.id = st.columnText(0);
        c.name = st.columnText(1);
        c.color = st.columnText(2);
        c.source = st.columnText(3);
        c.isVisible = st.columnInt(4) != 0;
        c.createdAt = st.columnInt(5);
        c.updatedAt = st.columnInt(6);
        c.revision = st.columnInt(7);
        c.deletedAt = st.isNull(8) ? std::nullopt
                                   : std::optional<domain::UtcMillis>(st.columnInt(8));
        c.lastDeviceId = st.columnText(9);
        out.push_back(std::move(c));
    }
    return out;
}

// ---- 时间块 ----

domain::TimeBlock CalendarRepository::rowToBlock(const storage::Statement& st) {
    domain::TimeBlock b;
    b.id = st.columnText(0);
    b.taskId = st.isNull(1) ? std::nullopt : std::optional<std::string>(st.columnText(1));
    b.calendarId = st.columnText(2);
    b.startAt = st.columnInt(3);
    b.endAt = st.columnInt(4);
    b.prepareMinutes = static_cast<std::int32_t>(st.columnInt(5));
    b.bufferMinutes = static_cast<std::int32_t>(st.columnInt(6));
    b.actualMinutes = static_cast<std::int32_t>(st.columnInt(7));
    b.note = st.columnText(8);
    b.createdAt = st.columnInt(9);
    b.updatedAt = st.columnInt(10);
    b.revision = st.columnInt(11);
    b.deletedAt = st.isNull(12) ? std::nullopt
                                : std::optional<domain::UtcMillis>(st.columnInt(12));
    b.lastDeviceId = st.columnText(13);
    b.title = st.columnText(14);
    b.batchId = st.columnText(15);
    return b;
}

domain::TimeBlock CalendarRepository::createBlock(domain::TimeBlock draft) const {
    if (draft.endAt <= draft.startAt) {
        throw EquoraError(ErrorCode::InvalidArgument, "time block end must be after start");
    }
    draft.id = Uuid::random().toString();
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;
    draft.deletedAt = std::nullopt;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO time_blocks (id, task_id, calendar_id, start_at, end_at, "
            "prepare_minutes, buffer_minutes, actual_minutes, note, created_at, "
            "updated_at, revision, deleted_at, last_device_id, title, batch_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.taskId)
            .bind(3, draft.calendarId)
            .bind(4, draft.startAt)
            .bind(5, draft.endAt)
            .bind(6, draft.prepareMinutes)
            .bind(7, draft.bufferMinutes)
            .bind(8, draft.actualMinutes)
            .bind(9, draft.note)
            .bind(10, draft.createdAt)
            .bind(11, draft.updatedAt)
            .bind(12, draft.revision)
            .bind(13, draft.deletedAt)
            .bind(14, deviceId_)
            .bind(15, draft.title)
            .bind(16, draft.batchId);
        st.step();
    }
    tx.commit();
    draft.lastDeviceId = deviceId_;
    return draft;
}

std::optional<domain::TimeBlock> CalendarRepository::findBlock(const std::string& id,
                                                               bool includeDeleted) const {
    auto st = db_.prepare(std::string(kBlockColumns) + " WHERE id = ?" +
                          (includeDeleted ? "" : " AND deleted_at IS NULL"));
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToBlock(st);
}

domain::TimeBlock CalendarRepository::update(domain::TimeBlock block, bool ownTransaction) const {
    auto existing = findBlock(block.id, /*includeDeleted=*/true);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "time block not found: " + block.id);
    }
    bumpAndCheck(*existing, block, "time block");
    if (block.endAt <= block.startAt) {
        throw EquoraError(ErrorCode::InvalidArgument, "time block end must be after start");
    }

    block.updatedAt = now();
    block.revision += 1;
    block.lastDeviceId = deviceId_;

    Transaction tx = ownTransaction ? db_.beginTransaction() : Transaction{};
    {
        auto st = db_.prepare(
            "UPDATE time_blocks SET task_id = ?, calendar_id = ?, start_at = ?, end_at = ?, "
            "prepare_minutes = ?, buffer_minutes = ?, actual_minutes = ?, note = ?, "
            "updated_at = ?, revision = ?, last_device_id = ?, title = ?, batch_id = ? "
            "WHERE id = ? AND revision = ?");
        st.bind(1, block.taskId)
            .bind(2, block.calendarId)
            .bind(3, block.startAt)
            .bind(4, block.endAt)
            .bind(5, block.prepareMinutes)
            .bind(6, block.bufferMinutes)
            .bind(7, block.actualMinutes)
            .bind(8, block.note)
            .bind(9, block.updatedAt)
            .bind(10, block.revision)
            .bind(11, block.lastDeviceId)
            .bind(12, block.title)
            .bind(13, block.batchId)
            .bind(14, block.id)
            .bind(15, block.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of time block");
        }
    }
    if (ownTransaction) tx.commit();
    return block;
}

void CalendarRepository::deleteBlock(const std::string& id) const {
    auto existing = findBlock(id, /*includeDeleted=*/true);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "time block not found: " + id);
    }
    if (existing->deletedAt.has_value()) return;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE time_blocks SET deleted_at = ?, updated_at = ?, revision = ?, "
            "last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, now())
            .bind(2, now())
            .bind(3, existing->revision + 1)
            .bind(4, deviceId_)
            .bind(5, id)
            .bind(6, existing->revision);
        st.step();
    }
    tx.commit();
}

std::vector<domain::TimeBlock> CalendarRepository::blocksInRange(domain::UtcMillis from,
                                                                 domain::UtcMillis to) const {
    auto st = db_.prepare(std::string(kBlockColumns) +
                          " WHERE deleted_at IS NULL AND start_at < ? AND end_at > ?"
                          " ORDER BY start_at");
    st.bind(1, to).bind(2, from);
    std::vector<domain::TimeBlock> out;
    while (st.step()) out.push_back(rowToBlock(st));
    return out;
}

std::vector<domain::TimeBlock> CalendarRepository::blocksForTask(
    const std::string& taskId) const {
    auto st = db_.prepare(std::string(kBlockColumns) +
                          " WHERE deleted_at IS NULL AND task_id = ? ORDER BY start_at");
    st.bind(1, taskId);
    std::vector<domain::TimeBlock> out;
    while (st.step()) out.push_back(rowToBlock(st));
    return out;
}

// ---- 事件 ----

domain::CalendarEvent CalendarRepository::rowToEvent(const storage::Statement& st) {
    domain::CalendarEvent e;
    e.id = st.columnText(0);
    e.title = st.columnText(1);
    e.location = st.columnText(2);
    e.note = st.columnText(3);
    e.calendarId = st.columnText(4);
    e.startAt = st.columnInt(5);
    e.endAt = st.columnInt(6);
    e.isAllDay = st.columnInt(7) != 0;
    e.createdAt = st.columnInt(8);
    e.updatedAt = st.columnInt(9);
    e.revision = st.columnInt(10);
    e.deletedAt = st.isNull(11) ? std::nullopt
                                : std::optional<domain::UtcMillis>(st.columnInt(11));
    e.lastDeviceId = st.columnText(12);
    return e;
}

domain::CalendarEvent CalendarRepository::createEvent(domain::CalendarEvent draft) const {
    if (draft.title.empty() || draft.endAt <= draft.startAt) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "event requires title and end > start");
    }
    draft.id = draft.id.empty() ? Uuid::random().toString() : draft.id;
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;
    draft.deletedAt = std::nullopt;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO events (id, title, location, note, calendar_id, start_at, end_at, "
            "is_all_day, created_at, updated_at, revision, deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.title)
            .bind(3, draft.location)
            .bind(4, draft.note)
            .bind(5, draft.calendarId)
            .bind(6, draft.startAt)
            .bind(7, draft.endAt)
            .bind(8, draft.isAllDay ? 1 : 0)
            .bind(9, draft.createdAt)
            .bind(10, draft.updatedAt)
            .bind(11, draft.revision)
            .bind(12, draft.deletedAt)
            .bind(13, deviceId_);
        st.step();
    }
    tx.commit();
    draft.lastDeviceId = deviceId_;
    return draft;
}

std::optional<domain::CalendarEvent> CalendarRepository::findEvent(const std::string& id,
                                                                   bool includeDeleted) const {
    auto st = db_.prepare(std::string(kEventColumns) + " WHERE id = ?" +
                          (includeDeleted ? "" : " AND deleted_at IS NULL"));
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToEvent(st);
}

domain::CalendarEvent CalendarRepository::update(domain::CalendarEvent ev) const {
    auto existing = findEvent(ev.id, /*includeDeleted=*/true);
    if (!existing.has_value() || existing->deletedAt.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "event not found: " + ev.id);
    }
    bumpAndCheck(*existing, ev, "event");
    if (ev.title.empty() || ev.endAt <= ev.startAt) {
        throw EquoraError(ErrorCode::InvalidArgument, "event requires title and end > start");
    }

    ev.updatedAt = now();
    ev.revision += 1;
    ev.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE events SET title = ?, location = ?, note = ?, calendar_id = ?, "
            "start_at = ?, end_at = ?, is_all_day = ?, updated_at = ?, revision = ?, "
            "last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, ev.title)
            .bind(2, ev.location)
            .bind(3, ev.note)
            .bind(4, ev.calendarId)
            .bind(5, ev.startAt)
            .bind(6, ev.endAt)
            .bind(7, ev.isAllDay ? 1 : 0)
            .bind(8, ev.updatedAt)
            .bind(9, ev.revision)
            .bind(10, ev.lastDeviceId)
            .bind(11, ev.id)
            .bind(12, ev.revision - 1);
        st.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of event");
        }
    }
    tx.commit();
    return ev;
}

void CalendarRepository::deleteEvent(const std::string& id) const {
    auto existing = findEvent(id, /*includeDeleted=*/true);
    if (!existing.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "event not found: " + id);
    }
    if (existing->deletedAt.has_value()) return;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE events SET deleted_at = ?, updated_at = ?, revision = ?, "
            "last_device_id = ? WHERE id = ? AND revision = ?");
        st.bind(1, now())
            .bind(2, now())
            .bind(3, existing->revision + 1)
            .bind(4, deviceId_)
            .bind(5, id)
            .bind(6, existing->revision);
        st.step();
    }
    tx.commit();
}

std::vector<domain::CalendarEvent> CalendarRepository::eventsInRange(domain::UtcMillis from,
                                                                     domain::UtcMillis to) const {
    auto st = db_.prepare(std::string(kEventColumns) +
                          " WHERE deleted_at IS NULL AND start_at < ? AND end_at > ?"
                          " ORDER BY start_at");
    st.bind(1, to).bind(2, from);
    std::vector<domain::CalendarEvent> out;
    while (st.step()) out.push_back(rowToEvent(st));
    return out;
}

// ---- 重复规则 ----

domain::RecurrenceRule CalendarRepository::rowToRule(const storage::Statement& st) {
    domain::RecurrenceRule r;
    r.id = st.columnText(0);
    r.hostType = st.columnText(1);
    r.hostId = st.columnText(2);
    r.freq = static_cast<domain::RecurFreq>(st.columnInt(3));
    r.interval = static_cast<std::int32_t>(st.columnInt(4));
    r.byWeekday = parseWeekdayCsv(st.columnText(5));
    r.monthMode = static_cast<domain::MonthMode>(st.columnInt(6));
    r.monthNth = static_cast<std::int32_t>(st.columnInt(7));
    r.monthWeekday = static_cast<domain::Weekday>(st.columnInt(8));
    r.untilUtc = st.isNull(9) ? std::nullopt
                              : std::optional<domain::UtcMillis>(st.columnInt(9));
    r.maxCount = st.isNull(10) ? std::nullopt
                               : std::optional<std::int64_t>(st.columnInt(10));
    r.completeRecurDays = static_cast<std::int32_t>(st.columnInt(11));
    r.excludedDates = parseDateCsv(st.columnText(12));
    r.createdAt = st.columnInt(13);
    r.updatedAt = st.columnInt(14);
    r.revision = st.columnInt(15);
    r.deletedAt = st.isNull(16) ? std::nullopt
                                : std::optional<domain::UtcMillis>(st.columnInt(16));
    r.lastDeviceId = st.columnText(17);
    return r;
}

domain::RecurrenceRule CalendarRepository::createRule(domain::RecurrenceRule draft) const {
    if (draft.hostId.empty() ||
        (draft.hostType != "task" && draft.hostType != "event")) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "rule requires host_type task|event and host_id");
    }
    draft.id = Uuid::random().toString();
    draft.createdAt = now();
    draft.updatedAt = draft.createdAt;
    draft.revision = 1;
    draft.deletedAt = std::nullopt;

    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "INSERT INTO recurrence_rules (id, host_type, host_id, freq, interval, "
            "by_weekday, month_mode, month_nth, month_weekday, until_utc, max_count, "
            "complete_recur_days, excluded_dates, created_at, updated_at, revision, "
            "deleted_at, last_device_id) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
        st.bind(1, draft.id)
            .bind(2, draft.hostType)
            .bind(3, draft.hostId)
            .bind(4, static_cast<std::int64_t>(draft.freq))
            .bind(5, draft.interval)
            .bind(6, weekdaysToCsv(draft.byWeekday))
            .bind(7, static_cast<std::int64_t>(draft.monthMode))
            .bind(8, draft.monthNth)
            .bind(9, static_cast<std::int64_t>(draft.monthWeekday))
            .bind(10, draft.untilUtc)
            .bind(11, draft.maxCount)
            .bind(12, draft.completeRecurDays)
            .bind(13, datesToCsv(draft.excludedDates))
            .bind(14, draft.createdAt)
            .bind(15, draft.updatedAt)
            .bind(16, draft.revision)
            .bind(17, draft.deletedAt)
            .bind(18, deviceId_);
        st.step();
    }
    tx.commit();
    draft.lastDeviceId = deviceId_;
    return draft;
}

std::optional<domain::RecurrenceRule> CalendarRepository::findRule(
    const std::string& id) const {
    auto st = db_.prepare(std::string(kRuleColumns) + " WHERE id = ? AND deleted_at IS NULL");
    st.bind(1, id);
    if (!st.step()) return std::nullopt;
    return rowToRule(st);
}

domain::RecurrenceRule CalendarRepository::update(domain::RecurrenceRule rule) const {
    auto st = db_.prepare(std::string(kRuleColumns) + " WHERE id = ?");
    st.bind(1, rule.id);
    if (!st.step()) {
        throw EquoraError(ErrorCode::NotFound, "rule not found: " + rule.id);
    }
    const domain::RecurrenceRule existing = rowToRule(st);
    bumpAndCheck(existing, rule, "recurrence rule");

    rule.updatedAt = now();
    rule.revision += 1;
    rule.lastDeviceId = deviceId_;

    Transaction tx = db_.beginTransaction();
    {
        auto up = db_.prepare(
            "UPDATE recurrence_rules SET freq = ?, interval = ?, by_weekday = ?, "
            "month_mode = ?, month_nth = ?, month_weekday = ?, until_utc = ?, "
            "max_count = ?, complete_recur_days = ?, excluded_dates = ?, updated_at = ?, "
            "revision = ?, last_device_id = ? WHERE id = ? AND revision = ?");
        up.bind(1, static_cast<std::int64_t>(rule.freq))
            .bind(2, rule.interval)
            .bind(3, weekdaysToCsv(rule.byWeekday))
            .bind(4, static_cast<std::int64_t>(rule.monthMode))
            .bind(5, rule.monthNth)
            .bind(6, static_cast<std::int64_t>(rule.monthWeekday))
            .bind(7, rule.untilUtc)
            .bind(8, rule.maxCount)
            .bind(9, rule.completeRecurDays)
            .bind(10, datesToCsv(rule.excludedDates))
            .bind(11, rule.updatedAt)
            .bind(12, rule.revision)
            .bind(13, rule.lastDeviceId)
            .bind(14, rule.id)
            .bind(15, rule.revision - 1);
        up.step();
        if (db_.changes() != 1) {
            throw EquoraError(ErrorCode::Conflict, "concurrent update of rule");
        }
    }
    tx.commit();
    return rule;
}

void CalendarRepository::deleteRule(const std::string& id) const {
    Transaction tx = db_.beginTransaction();
    {
        auto st = db_.prepare(
            "UPDATE recurrence_rules SET deleted_at = ?, updated_at = ?, "
            "revision = revision + 1, last_device_id = ? WHERE id = ? AND deleted_at IS NULL");
        st.bind(1, now()).bind(2, now()).bind(3, deviceId_).bind(4, id);
        st.step();
    }
    tx.commit();
}

std::vector<domain::RecurrenceRule> CalendarRepository::rulesForHost(
    std::string_view hostType, const std::string& hostId) const {
    auto st = db_.prepare(std::string(kRuleColumns) +
                          " WHERE host_type = ? AND host_id = ? AND deleted_at IS NULL");
    st.bind(1, hostType).bind(2, hostId);
    std::vector<domain::RecurrenceRule> out;
    while (st.step()) out.push_back(rowToRule(st));
    return out;
}

// ---- 窗口物化与例外编辑 ----

std::vector<Span> CalendarRepository::materializeWindow(domain::UtcMillis from,
                                                        domain::UtcMillis to,
                                                        int tzOffsetMinutes) const {
    std::vector<Span> spans;

    for (const auto& b : blocksInRange(from, to)) {
        Span s;
        s.sourceId = b.id;
        s.sourceType = "block";
        s.title = b.title.empty() ? b.note : b.title;
        s.taskId = b.taskId.value_or("");
        // 冲突计算考虑准备与缓冲(有效区间外扩)。
        s.start = b.startAt - static_cast<std::int64_t>(b.prepareMinutes) * 60'000;
        s.end = b.endAt + static_cast<std::int64_t>(b.bufferMinutes) * 60'000;
        spans.push_back(std::move(s));
    }

    for (const auto& e : eventsInRange(from, to)) {
        Span s;
        s.sourceId = e.id;
        s.sourceType = "event";
        s.title = e.title;
        s.start = e.startAt;
        s.end = e.endAt;
        spans.push_back(std::move(s));
    }

    // 事件规则展开(种子 = 事件本身;事件记录自身不再重复计入)。
    for (const auto& r : [&] {
             auto st = db_.prepare(std::string(kRuleColumns) +
                                   " WHERE deleted_at IS NULL AND host_type = 'event'");
             std::vector<domain::RecurrenceRule> rules;
             while (st.step()) rules.push_back(rowToRule(st));
             return rules;
         }()) {
        auto ev = findEvent(r.hostId, /*includeDeleted=*/true);
        if (!ev.has_value() || ev->deletedAt.has_value()) continue;
        for (const auto& inst : scheduling::expandRecurrence(r, ev->startAt, ev->endAt,
                                                             from, to, tzOffsetMinutes)) {
            // 宿主事件本身已计入窗口,跳过与其同时刻的种子实例。
            if (inst.startUtc == ev->startAt) continue;
            Span s;
            s.sourceId = r.id + ":" + std::to_string(inst.startUtc);
            s.sourceType = "recurring";
            s.title = ev->title;
            s.start = inst.startUtc;
            s.end = inst.endUtc;
            spans.push_back(std::move(s));
        }
    }

    std::sort(spans.begin(), spans.end(),
              [](const Span& a, const Span& b) { return a.start < b.start; });
    return spans;
}

domain::CalendarEvent CalendarRepository::detachOccurrence(const domain::RecurrenceRule& rule,
                                                           domain::UtcMillis occurrenceStartUtc,
                                                           int tzOffsetMinutes) const {
    auto ev = findEvent(rule.hostId);
    if (!ev.has_value()) {
        throw EquoraError(ErrorCode::NotFound, "rule host event not found");
    }

    // 确认该实例确实存在(未被排除)。
    const auto instances = scheduling::expandRecurrence(
        rule, ev->startAt, ev->endAt, occurrenceStartUtc - 1, occurrenceStartUtc + 1,
        tzOffsetMinutes);
    const auto hit = std::find_if(instances.begin(), instances.end(), [&](const auto& i) {
        return i.startUtc == occurrenceStartUtc;
    });
    if (hit == instances.end()) {
        throw EquoraError(ErrorCode::NotFound, "occurrence not found in series");
    }

    // 物化:以指定 id 创建真实事件(副本),并把该日期加入例外。
    domain::CalendarEvent copy = *ev;
    copy.id.clear();
    copy.startAt = occurrenceStartUtc;
    copy.endAt = hit->endUtc;
    const auto materialized = createEvent(copy);

    domain::RecurrenceRule updated = rule;
    updated.excludedDates.push_back(
        scheduling::localDateString(occurrenceStartUtc, tzOffsetMinutes));
    (void)update(updated);
    return materialized;
}

domain::RecurrenceRule CalendarRepository::splitSeries(const domain::RecurrenceRule& rule,
                                                       domain::UtcMillis occurrenceStartUtc) const {
    domain::RecurrenceRule old = rule;
    old.untilUtc = occurrenceStartUtc; // 不含该实例
    (void)update(old);

    domain::RecurrenceRule tail = rule;
    tail.id.clear();
    tail.untilUtc = std::nullopt;
    tail.maxCount = std::nullopt;
    tail.excludedDates.clear();
    tail.revision = 0;
    return createRule(tail);
}

} // namespace equora::core
