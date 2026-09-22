#include <equora/storage/Migrations.h>

#include <algorithm>
#include <string>

#include <equora/common/Logger.h>
#include <equora/domain/Error.h>
#include <equora/domain/Time.h>

namespace equora::storage {

namespace {

// 迁移登记表。框架自身负责创建,不属于任何业务迁移。
constexpr std::string_view kCreateRegistrySql = R"SQL(
CREATE TABLE IF NOT EXISTS schema_migrations (
    version    INTEGER PRIMARY KEY,
    name       TEXT    NOT NULL,
    applied_at INTEGER NOT NULL
);
)SQL";

[[nodiscard]] bool isApplied(const Database& db, int version) {
    auto st = db.prepare("SELECT COUNT(*) FROM schema_migrations WHERE version = ?");
    st.bind(1, static_cast<std::int64_t>(version));
    st.step();
    return st.columnInt(0) > 0;
}

// ---- v1:任务表(同步就绪的列布局) ----
constexpr std::string_view kV1Name = "tasks core table";

constexpr std::string_view kV1Statements[] = {
    R"SQL(CREATE TABLE tasks (
    id               TEXT PRIMARY KEY,
    title            TEXT    NOT NULL,
    note             TEXT    NOT NULL DEFAULT '',
    status           INTEGER NOT NULL DEFAULT 0,
    priority         INTEGER NOT NULL DEFAULT 2,
    importance       INTEGER NOT NULL DEFAULT 0,
    due_at           INTEGER,
    estimate_minutes INTEGER,
    actual_minutes   INTEGER NOT NULL DEFAULT 0,
    project_id       TEXT,
    created_at       INTEGER NOT NULL,
    updated_at       INTEGER NOT NULL,
    revision         INTEGER NOT NULL,
    deleted_at       INTEGER,
    last_device_id   TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_tasks_status ON tasks (status);)SQL",
    R"SQL(CREATE INDEX idx_tasks_active_due ON tasks (due_at) WHERE deleted_at IS NULL;)SQL",
};

// ---- v3:日历、时间块、日程与重复规则 ----
constexpr std::string_view kV3Name = "calendars time_blocks events recurrence_rules";

constexpr std::string_view kV3Statements[] = {
    R"SQL(CREATE TABLE calendars (
    id             TEXT PRIMARY KEY,
    name           TEXT    NOT NULL,
    color          TEXT    NOT NULL DEFAULT '',
    source         TEXT    NOT NULL DEFAULT 'local',
    is_visible     INTEGER NOT NULL DEFAULT 1,
    created_at     INTEGER NOT NULL,
    updated_at     INTEGER NOT NULL,
    revision       INTEGER NOT NULL,
    deleted_at     INTEGER,
    last_device_id TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE TABLE time_blocks (
    id              TEXT PRIMARY KEY,
    task_id         TEXT REFERENCES tasks (id) ON DELETE CASCADE,
    calendar_id     TEXT    NOT NULL DEFAULT '',
    start_at        INTEGER NOT NULL,
    end_at          INTEGER NOT NULL,
    prepare_minutes INTEGER NOT NULL DEFAULT 0,
    buffer_minutes  INTEGER NOT NULL DEFAULT 0,
    actual_minutes  INTEGER NOT NULL DEFAULT 0,
    note            TEXT    NOT NULL DEFAULT '',
    created_at      INTEGER NOT NULL,
    updated_at      INTEGER NOT NULL,
    revision        INTEGER NOT NULL,
    deleted_at      INTEGER,
    last_device_id  TEXT    NOT NULL DEFAULT '',
    CHECK (end_at > start_at)
);)SQL",
    R"SQL(CREATE INDEX idx_time_blocks_window ON time_blocks (start_at, end_at) WHERE deleted_at IS NULL;)SQL",
    R"SQL(CREATE INDEX idx_time_blocks_task ON time_blocks (task_id) WHERE deleted_at IS NULL;)SQL",
    R"SQL(CREATE TABLE events (
    id          TEXT PRIMARY KEY,
    title       TEXT    NOT NULL,
    location    TEXT    NOT NULL DEFAULT '',
    note        TEXT    NOT NULL DEFAULT '',
    calendar_id TEXT    NOT NULL DEFAULT '',
    start_at    INTEGER NOT NULL,
    end_at      INTEGER NOT NULL,
    is_all_day  INTEGER NOT NULL DEFAULT 0,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL,
    revision    INTEGER NOT NULL,
    deleted_at  INTEGER,
    last_device_id TEXT NOT NULL DEFAULT '',
    CHECK (end_at > start_at)
);)SQL",
    R"SQL(CREATE INDEX idx_events_window ON events (start_at, end_at) WHERE deleted_at IS NULL;)SQL",
    R"SQL(CREATE TABLE recurrence_rules (
    id                  TEXT PRIMARY KEY,
    host_type           TEXT    NOT NULL CHECK (host_type IN ('task','event')),
    host_id             TEXT    NOT NULL,
    freq                INTEGER NOT NULL,
    interval            INTEGER NOT NULL DEFAULT 1,
    by_weekday          TEXT    NOT NULL DEFAULT '',
    month_mode          INTEGER NOT NULL DEFAULT 0,
    month_nth           INTEGER NOT NULL DEFAULT 1,
    month_weekday       INTEGER NOT NULL DEFAULT 0,
    until_utc           INTEGER,
    max_count           INTEGER,
    complete_recur_days INTEGER NOT NULL DEFAULT 0,
    excluded_dates      TEXT    NOT NULL DEFAULT '',
    created_at          INTEGER NOT NULL,
    updated_at          INTEGER NOT NULL,
    revision            INTEGER NOT NULL,
    deleted_at          INTEGER,
    last_device_id      TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_recurrence_host ON recurrence_rules (host_type, host_id);)SQL",
};

// ---- v2:项目、标签、任务-标签关系、检查项与应用元数据 ----
constexpr std::string_view kV2Name = "projects tags checklists app_meta";

constexpr std::string_view kV2Statements[] = {
    R"SQL(CREATE TABLE app_meta (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);)SQL",
    R"SQL(CREATE TABLE projects (
    id             TEXT PRIMARY KEY,
    name           TEXT    NOT NULL,
    color          TEXT    NOT NULL DEFAULT '',
    goal           TEXT    NOT NULL DEFAULT '',
    status         INTEGER NOT NULL DEFAULT 0,
    archived_at    INTEGER,
    created_at     INTEGER NOT NULL,
    updated_at     INTEGER NOT NULL,
    revision       INTEGER NOT NULL,
    deleted_at     INTEGER,
    last_device_id TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_projects_status ON projects (status);)SQL",
    R"SQL(CREATE TABLE tags (
    id             TEXT PRIMARY KEY,
    name           TEXT    NOT NULL COLLATE NOCASE UNIQUE,
    color          TEXT    NOT NULL DEFAULT '',
    created_at     INTEGER NOT NULL,
    updated_at     INTEGER NOT NULL,
    revision       INTEGER NOT NULL,
    deleted_at     INTEGER,
    last_device_id TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE TABLE task_tags (
    task_id    TEXT NOT NULL REFERENCES tasks (id) ON DELETE CASCADE,
    tag_id     TEXT NOT NULL REFERENCES tags  (id) ON DELETE CASCADE,
    created_at INTEGER NOT NULL,
    PRIMARY KEY (task_id, tag_id)
);)SQL",
    R"SQL(CREATE INDEX idx_task_tags_tag ON task_tags (tag_id);)SQL",
    R"SQL(CREATE TABLE checklist_items (
    id             TEXT PRIMARY KEY,
    task_id        TEXT    NOT NULL REFERENCES tasks (id) ON DELETE CASCADE,
    content        TEXT    NOT NULL,
    is_checked     INTEGER NOT NULL DEFAULT 0,
    sort_order     INTEGER NOT NULL DEFAULT 0,
    created_at     INTEGER NOT NULL,
    updated_at     INTEGER NOT NULL,
    revision       INTEGER NOT NULL,
    deleted_at     INTEGER,
    last_device_id TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_checklist_task ON checklist_items (task_id, sort_order);)SQL",
    R"SQL(CREATE INDEX idx_tasks_project ON tasks (project_id) WHERE deleted_at IS NULL;)SQL",
};

// ---- v4:专注会话、中断、分心捕获与专注预设 ----
constexpr std::string_view kV4Name = "focus_sessions interruptions distraction_inbox focus_profiles";

constexpr std::string_view kV4Statements[] = {
    R"SQL(CREATE TABLE focus_sessions (
    id              TEXT PRIMARY KEY,
    task_id         TEXT REFERENCES tasks (id) ON DELETE SET NULL,
    block_id        TEXT,
    mode            INTEGER NOT NULL,
    planned_start   INTEGER NOT NULL,
    planned_end     INTEGER,
    actual_start    INTEGER NOT NULL,
    actual_end      INTEGER,
    paused_ms       INTEGER NOT NULL DEFAULT 0,
    state           INTEGER NOT NULL DEFAULT 0,
    goal            TEXT    NOT NULL DEFAULT '',
    completion_note TEXT    NOT NULL DEFAULT '',
    completion_level INTEGER NOT NULL DEFAULT -1,
    created_at      INTEGER NOT NULL,
    updated_at      INTEGER NOT NULL,
    revision        INTEGER NOT NULL,
    deleted_at      INTEGER,
    last_device_id  TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_focus_open ON focus_sessions (actual_start) WHERE actual_end IS NULL;)SQL",
    R"SQL(CREATE INDEX idx_focus_time ON focus_sessions (actual_start, actual_end);)SQL",
    R"SQL(CREATE TABLE interruptions (
    id          TEXT PRIMARY KEY,
    session_id  TEXT NOT NULL REFERENCES focus_sessions (id) ON DELETE CASCADE,
    occurred_at INTEGER NOT NULL,
    duration_ms INTEGER NOT NULL DEFAULT 0,
    reason      TEXT    NOT NULL DEFAULT '',
    source      TEXT    NOT NULL DEFAULT 'manual',
    handling    TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_interruptions_session ON interruptions (session_id);)SQL",
    R"SQL(CREATE TABLE distraction_inbox_items (
    id             TEXT PRIMARY KEY,
    session_id     TEXT REFERENCES focus_sessions (id) ON DELETE SET NULL,
    content        TEXT    NOT NULL,
    captured_at    INTEGER NOT NULL,
    resolution     INTEGER NOT NULL DEFAULT 0,
    resolved_ref   TEXT    NOT NULL DEFAULT '',
    created_at     INTEGER NOT NULL,
    updated_at     INTEGER NOT NULL,
    revision       INTEGER NOT NULL,
    deleted_at     INTEGER,
    last_device_id TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_distraction_pending ON distraction_inbox_items (captured_at)
    WHERE resolution = 0 AND deleted_at IS NULL;)SQL",
    R"SQL(CREATE TABLE focus_profiles (
    id             TEXT PRIMARY KEY,
    name           TEXT NOT NULL COLLATE NOCASE UNIQUE,
    mode           INTEGER NOT NULL DEFAULT 0,
    planned_minutes INTEGER NOT NULL DEFAULT 25,
    break_minutes  INTEGER NOT NULL DEFAULT 5,
    allowed_apps   TEXT    NOT NULL DEFAULT '[]',
    blocked_apps   TEXT    NOT NULL DEFAULT '[]',
    allowed_sites  TEXT    NOT NULL DEFAULT '[]',
    blocked_sites  TEXT    NOT NULL DEFAULT '[]',
    notify_policy  INTEGER NOT NULL DEFAULT 0,
    is_default     INTEGER NOT NULL DEFAULT 0,
    created_at     INTEGER NOT NULL,
    updated_at     INTEGER NOT NULL,
    revision       INTEGER NOT NULL,
    deleted_at     INTEGER,
    last_device_id TEXT    NOT NULL DEFAULT ''
);)SQL",
};

// ---- v5:复盘存档与自动化规则 ----
constexpr std::string_view kV5Name = "reviews automation_rules automation_logs";

constexpr std::string_view kV5Statements[] = {
    R"SQL(CREATE TABLE daily_reviews (
    id              TEXT PRIMARY KEY,
    local_date      TEXT    NOT NULL UNIQUE,
    completed_count INTEGER NOT NULL,
    deferred_count  INTEGER NOT NULL,
    cancelled_count INTEGER NOT NULL,
    planned_minutes INTEGER NOT NULL,
    actual_minutes  INTEGER NOT NULL,
    big_three_done  INTEGER NOT NULL,
    distraction_count INTEGER NOT NULL,
    notes           TEXT    NOT NULL DEFAULT '',
    focus_tomorrow  TEXT    NOT NULL DEFAULT '',
    created_at      INTEGER NOT NULL,
    updated_at      INTEGER NOT NULL,
    revision        INTEGER NOT NULL,
    deleted_at      INTEGER,
    last_device_id  TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE TABLE weekly_reviews (
    id                TEXT PRIMARY KEY,
    week_start_date   TEXT    NOT NULL UNIQUE,
    deep_work_minutes INTEGER NOT NULL,
    focus_ratio       REAL    NOT NULL DEFAULT 0,
    estimate_accuracy REAL    NOT NULL DEFAULT 0,
    best_focus_hour   INTEGER NOT NULL DEFAULT -1,
    notes             TEXT    NOT NULL DEFAULT '',
    next_week_goals   TEXT    NOT NULL DEFAULT '',
    created_at        INTEGER NOT NULL,
    updated_at        INTEGER NOT NULL,
    revision          INTEGER NOT NULL,
    deleted_at        INTEGER,
    last_device_id    TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE TABLE automation_rules (
    id          TEXT PRIMARY KEY,
    name        TEXT    NOT NULL,
    trigger     TEXT    NOT NULL, -- JSON: {type: task_created|tag_added|due_soon|completed, ...}
    conditions  TEXT    NOT NULL DEFAULT '{}', -- JSON: {tag, priority_gte, estimate_gte}
    actions     TEXT    NOT NULL, -- JSON: [{type: add_tag|suggest_split, ...}]
    enabled     INTEGER NOT NULL DEFAULT 1,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL,
    revision    INTEGER NOT NULL,
    deleted_at  INTEGER,
    last_device_id TEXT NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_automation_enabled ON automation_rules (enabled) WHERE deleted_at IS NULL;)SQL",
    R"SQL(CREATE TABLE automation_logs (
    id          TEXT PRIMARY KEY,
    rule_id     TEXT NOT NULL,
    entity_id   TEXT NOT NULL,
    triggered_at INTEGER NOT NULL,
    success     INTEGER NOT NULL,
    error       TEXT    NOT NULL DEFAULT ''
);)SQL",
    R"SQL(CREATE INDEX idx_automation_logs_rule ON automation_logs (rule_id, triggered_at);)SQL",
};

// ---- v6:同步客户端(Outbox、状态、冲突中心) ----
constexpr std::string_view kV6Name = "sync_outbox sync_state sync_conflicts";

constexpr std::string_view kV6Statements[] = {
    R"SQL(CREATE TABLE sync_outbox (
    rowid            INTEGER PRIMARY KEY AUTOINCREMENT,
    operation_id     TEXT    NOT NULL UNIQUE,
    entity_type      TEXT    NOT NULL,
    entity_id        TEXT    NOT NULL,
    base_revision    INTEGER NOT NULL DEFAULT 0,
    kind             INTEGER NOT NULL DEFAULT 1,
    payload          TEXT    NOT NULL DEFAULT '{}',
    status           INTEGER NOT NULL DEFAULT 0,
    attempts         INTEGER NOT NULL DEFAULT 0,
    next_attempt_at  INTEGER NOT NULL DEFAULT 0,
    created_at       INTEGER NOT NULL
);)SQL",
    R"SQL(CREATE INDEX idx_outbox_due ON sync_outbox (status, next_attempt_at);)SQL",
    R"SQL(CREATE TABLE sync_state (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);)SQL",
    R"SQL(CREATE TABLE sync_conflicts (
    rowid           INTEGER PRIMARY KEY AUTOINCREMENT,
    operation_id    TEXT    NOT NULL,
    entity_type     TEXT    NOT NULL,
    entity_id       TEXT    NOT NULL,
    local_payload   TEXT    NOT NULL DEFAULT '{}',
    server_payload  TEXT    NOT NULL DEFAULT '{}',
    server_revision INTEGER NOT NULL DEFAULT 0,
    server_deleted  INTEGER NOT NULL DEFAULT 0,
    created_at      INTEGER NOT NULL,
    resolved        INTEGER NOT NULL DEFAULT 0,
    resolution      INTEGER NOT NULL DEFAULT 0
);)SQL",
    R"SQL(CREATE INDEX idx_conflicts_open ON sync_conflicts (resolved, created_at);)SQL",
};

constexpr std::string_view kInsertRegistrySql =
    "INSERT INTO schema_migrations (version, name, applied_at) VALUES (?, ?, ?)";

} // namespace

const std::vector<Migration>& builtInMigrations() {
    static const std::vector<Migration> kMigrations = {
        Migration{1, kV1Name,
                  std::vector<std::string_view>(std::begin(kV1Statements),
                                                std::end(kV1Statements))},
        Migration{2, kV2Name,
                  std::vector<std::string_view>(std::begin(kV2Statements),
                                                std::end(kV2Statements))},
        Migration{3, kV3Name,
                  std::vector<std::string_view>(std::begin(kV3Statements),
                                                std::end(kV3Statements))},
        Migration{4, kV4Name,
                  std::vector<std::string_view>(std::begin(kV4Statements),
                                                std::end(kV4Statements))},
        Migration{5, kV5Name,
                  std::vector<std::string_view>(std::begin(kV5Statements),
                                                std::end(kV5Statements))},
        Migration{6, kV6Name,
                  std::vector<std::string_view>(std::begin(kV6Statements),
                                                std::end(kV6Statements))},
        Migration{7, "time block titles and batches", {
            "ALTER TABLE time_blocks ADD COLUMN title TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE time_blocks ADD COLUMN batch_id TEXT NOT NULL DEFAULT '';",
            "CREATE INDEX idx_time_blocks_batch ON time_blocks(batch_id) WHERE deleted_at IS NULL;"}},
    };
    return kMigrations;
}

int currentSchemaVersion(const Database& db) {
    db.exec(kCreateRegistrySql);
    auto st = db.prepare("SELECT COALESCE(MAX(version), 0) FROM schema_migrations");
    st.step();
    return static_cast<int>(st.columnInt(0));
}

void applyMigrations(const Database& db, const std::vector<Migration>& migrations) {
    db.exec(kCreateRegistrySql);

    // 版本必须严格递增,防止列表本身出错时乱序应用。
    int lastVersion = 0;
    for (const Migration& m : migrations) {
        if (m.version <= lastVersion) {
            throw domain::EquoraError(domain::ErrorCode::MigrationError,
                                      "migration list is not strictly increasing at v" +
                                          std::to_string(m.version));
        }
        lastVersion = m.version;
    }

    for (const Migration& m : migrations) {
        if (isApplied(db, m.version)) continue;

        Transaction tx = db.beginTransaction();
        try {
            for (const std::string_view sql : m.statements) {
                db.exec(sql);
            }
            {
                auto st = db.prepare(kInsertRegistrySql);
                st.bind(1, static_cast<std::int64_t>(m.version))
                    .bind(2, m.name)
                    .bind(3, domain::utc::now());
                st.step();
            }
            tx.commit();
            common::logInfo("storage.migration",
                            "applied v" + std::to_string(m.version) + " '" +
                                std::string(m.name) + "'");
        } catch (const domain::EquoraError& e) {
            // tx 析构时 ROLLBACK;数据库停留在上一个完整版本。
            throw MigrationError(m.version, m.name, e.what());
        }
    }
}

} // namespace equora::storage
