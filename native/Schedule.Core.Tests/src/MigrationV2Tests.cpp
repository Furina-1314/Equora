// v1 → v2 迁移验证:旧库数据在升级后完整可用。
#include <gtest/gtest.h>

#include <filesystem>

#include <equora/core/TaskRepository.h>
#include <equora/domain/Task.h>
#include <equora/storage/Database.h>
#include <equora/storage/Migrations.h>

namespace {

using equora::domain::Task;
using equora::storage::Database;
using equora::storage::Migration;
using equora::storage::applyMigrations;
using equora::storage::builtInMigrations;
using equora::storage::currentSchemaVersion;

TEST(MigrationV2Test, UpgradesV1DatabaseWithData) {
    std::filesystem::create_directories(EQUORA_TEST_TMPDIR);
    const std::string path = std::string(EQUORA_TEST_TMPDIR) + "/v1_upgrade.db";
    std::error_code ec;
    std::filesystem::remove(path, ec);

    {
        Database db = Database::open(path);
        // 仅应用 v1,模拟旧版本用户。
        const auto& all = builtInMigrations();
        applyMigrations(db, std::vector<Migration>(all.begin(), all.begin() + 1));
        ASSERT_EQ(currentSchemaVersion(db), 1);

        equora::core::TaskRepository repo(db, "old-device");
        (void)repo.create(Task::draft("旧数据必须保留"));
    }

    {
        // 新版本打开:自动升级到 v2,旧数据可用,新表可写。
        Database db = Database::open(path);
        applyMigrations(db);
        EXPECT_EQ(currentSchemaVersion(db), builtInMigrations().back().version);

        equora::core::TaskRepository repo(db, "new-device");
        ASSERT_EQ(repo.listAll().size(), 1U);
        EXPECT_EQ(repo.listAll()[0].title, "旧数据必须保留");

        db.exec("INSERT INTO tags (id, name, created_at, updated_at, revision) "
                "VALUES ('t1', '新表可用', 1, 1, 1)");
        db.exec("INSERT INTO projects (id, name, created_at, updated_at, revision) "
                "VALUES ('p1', '项目', 1, 1, 1)");
    }
}

} // namespace
