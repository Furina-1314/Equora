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
