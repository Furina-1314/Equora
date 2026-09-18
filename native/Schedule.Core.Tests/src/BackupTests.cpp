#include <gtest/gtest.h>

#include <filesystem>
#include <fstream>
#include <memory>
#include <string>

#include <equora/core/TaskRepository.h>
#include <equora/domain/Error.h>
#include <equora/domain/Task.h>
#include <equora/storage/Backup.h>
#include <equora/storage/Database.h>
#include <equora/storage/Migrations.h>

namespace {

using equora::domain::Task;
using equora::storage::BackupManager;
using equora::storage::Database;
using equora::storage::applyMigrations;

class BackupTest : public ::testing::Test {
protected:
    void SetUp() override {
        dir_ = std::string(EQUORA_TEST_TMPDIR) + "/backup_" +
               ::testing::UnitTest::GetInstance()->current_test_info()->name();
        std::filesystem::remove_all(dir_, ec_);
        std::filesystem::create_directories(dir_, ec_);

        dbPath_ = dir_ + "/main.db";
        db_ = Database::open(dbPath_);
        applyMigrations(db_);
        repo_ = std::make_unique<equora::core::TaskRepository>(db_, "device-test");
    }

    void TearDown() override {
        repo_.reset();
        db_.close();
        std::error_code ec;
        std::filesystem::remove_all(dir_, ec);
    }

    std::error_code ec_;
    std::string dir_;
    std::string dbPath_;
    Database db_;
    std::unique_ptr<equora::core::TaskRepository> repo_;
};

TEST_F(BackupTest, CreateVerifyAndSidecar) {
    (void)repo_->create(Task::draft("备份前的任务"));

    const auto info = BackupManager::create(db_, dir_);
    EXPECT_TRUE(std::filesystem::exists(info.file));
    EXPECT_TRUE(std::filesystem::exists(std::filesystem::path(info.file.string() + ".sha256")));
    EXPECT_EQ(info.sha256Hex.size(), 64U);

    EXPECT_TRUE(BackupManager::verify(info.file));

    // 列表能发现它。
    EXPECT_EQ(BackupManager::list(dir_).size(), 1U);
}

TEST_F(BackupTest, VerifyDetectsTampering) {
    (void)repo_->create(Task::draft("任务"));
    const auto info = BackupManager::create(db_, dir_);

    // 篡改备份内容 → 校验失败。
    {
        std::ofstream out(info.file, std::ios::binary | std::ios::app);
        out << "tampered";
    }
    std::string why;
    EXPECT_FALSE(BackupManager::verify(info.file, &why));
    EXPECT_NE(why.find("checksum"), std::string::npos);
}

TEST_F(BackupTest, VerifyRejectsMissingSidecar) {
    (void)repo_->create(Task::draft("任务"));
    const auto info = BackupManager::create(db_, dir_);
    std::filesystem::remove(std::filesystem::path(info.file.string() + ".sha256"), ec_);

    std::string why;
    EXPECT_FALSE(BackupManager::verify(info.file, &why));
    EXPECT_NE(why.find("sidecar"), std::string::npos);
}

TEST_F(BackupTest, RestoreRollsBackToBackupState) {
    (void)repo_->create(Task::draft("保留任务"));
    const auto info = BackupManager::create(db_, dir_);

    (void)repo_->create(Task::draft("备份后新增"));
    ASSERT_EQ(repo_->listAll().size(), 2U);

    BackupManager::restore(db_, info.file);
    // 恢复后 repo 引用的连接已被替换,但句柄仍有效(同一 Database 对象)。
    ASSERT_EQ(repo_->listAll().size(), 1U);
    EXPECT_EQ(repo_->listAll()[0].title, "保留任务");

    // 恢复前现场已被保护(pre-restore 快照存在)。
    bool hasPreRestore = false;
    for (const auto& entry : std::filesystem::directory_iterator(dir_, ec_)) {
        if (entry.path().filename().string().find("pre-restore") != std::string::npos) {
            hasPreRestore = true;
        }
    }
    EXPECT_TRUE(hasPreRestore);
}

TEST_F(BackupTest, RestoreRefusesCorruptBackup) {
    (void)repo_->create(Task::draft("任务"));
    const auto info = BackupManager::create(db_, dir_);
    {
        std::ofstream out(info.file, std::ios::binary | std::ios::app);
        out << "broken";
    }

    try {
        BackupManager::restore(db_, info.file);
        FAIL() << "应当拒绝损坏的备份";
    } catch (const equora::domain::EquoraError& e) {
        EXPECT_EQ(e.code(), equora::domain::ErrorCode::IoError);
    }
    // 主库未受影响。
    EXPECT_EQ(repo_->listAll().size(), 1U);
}

TEST_F(BackupTest, RetentionPolicyKeepsNewest) {
    for (int i = 0; i < 3; ++i) {
        (void)BackupManager::create(db_, dir_);
    }
    ASSERT_EQ(BackupManager::list(dir_).size(), 3U);

    BackupManager::applyRetentionPolicy(dir_, 2);
    EXPECT_EQ(BackupManager::list(dir_).size(), 2U);
}

} // namespace
