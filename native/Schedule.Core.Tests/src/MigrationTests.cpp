#include <equora/storage/Migrations.h>

#include <gtest/gtest.h>

#include <filesystem>
#include <fstream>

#include <equora/domain/Error.h>

namespace {

using equora::storage::Database;
using equora::storage::Migration;
using equora::storage::applyMigrations;
using equora::storage::builtInMigrations;
using equora::storage::currentSchemaVersion;

class TempFile {
public:
    TempFile() {
        static int counter = 0;
        std::filesystem::create_directories(EQUORA_TEST_TMPDIR);
        path_ = std::filesystem::path(EQUORA_TEST_TMPDIR) /
                ("migration_" + std::to_string(++counter) + "_" +
                 ::testing::UnitTest::GetInstance()->current_test_info()->name() + ".db");
        std::filesystem::remove(path_);
        std::filesystem::remove(path_.string() + "-wal");
        std::filesystem::remove(path_.string() + "-shm");
    }
    ~TempFile() {
        std::error_code ec;
        std::filesystem::remove(path_, ec);
        std::filesystem::remove(path_.string() + "-wal", ec);
        std::filesystem::remove(path_.string() + "-shm", ec);
    }
    [[nodiscard]] const std::filesystem::path& path() const { return path_; }

private:
    std::filesystem::path path_;
};

TEST(MigrationTest, FreshDatabaseReachesLatestVersion) {
    TempFile tmp;
    {
        const Database db = Database::open(tmp.path());
        EXPECT_EQ(currentSchemaVersion(db), 0);
        applyMigrations(db);
        EXPECT_EQ(currentSchemaVersion(db), builtInMigrations().back().version);
    }
}

TEST(MigrationTest, ReapplyIsIdempotent) {
    TempFile tmp;
    {
        const Database db = Database::open(tmp.path());
        applyMigrations(db);
        applyMigrations(db); // 重复调用不得报错或重复建表
        EXPECT_EQ(currentSchemaVersion(db), builtInMigrations().back().version);

        auto st = db.prepare("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' "
                             "AND name = 'tasks'");
        st.step();
        EXPECT_EQ(st.columnInt(0), 1);
    }
}

TEST(MigrationTest, FailedMigrationRollsBackCompletely) {
    // 注入列表:v1 正常,v2 含非法 SQL。v2 必须整体回滚,库停留在 v1 且可继续使用。
    const std::vector<Migration> custom = {
        Migration{1, builtInMigrations()[0].name, builtInMigrations()[0].statements},
        Migration{2, "broken",
                  {"CREATE TABLE will_be_rolled_back (id INTEGER PRIMARY KEY)",
                   "THIS IS NOT SQL;"}},
    };

    TempFile tmp;
    {
        const Database db = Database::open(tmp.path());
        // 全新库直接注入:v1 应用成功,v2 中途失败必须整体回滚。
        EXPECT_THROW(applyMigrations(db, custom), equora::storage::MigrationError);
        EXPECT_EQ(currentSchemaVersion(db), 1);

        // 回滚后表不存在,且 tasks 表仍可正常读写。
        auto st = db.prepare("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' "
                             "AND name = 'will_be_rolled_back'");
        st.step();
        EXPECT_EQ(st.columnInt(0), 0);

        db.exec("INSERT INTO tasks (id, title, created_at, updated_at, revision) "
                "VALUES ('x', 'still usable', 1, 1, 1)");

        // 恢复路径:转用正式迁移列表后可继续升级。
        applyMigrations(db);
        EXPECT_EQ(currentSchemaVersion(db), builtInMigrations().back().version);
    }
}

TEST(MigrationTest, RejectsNonIncreasingVersions) {
    const std::vector<Migration> bad = {
        Migration{2, "a", {"SELECT 1"}},
        Migration{2, "b", {"SELECT 1"}},
    };
    const Database db = Database::open(":memory:");
    EXPECT_THROW(applyMigrations(db, bad), equora::domain::EquoraError);
    EXPECT_EQ(currentSchemaVersion(db), 0);
}

} // namespace
