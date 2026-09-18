#pragma once

#include <gtest/gtest.h>

#include <memory>

#include <equora/core/ChecklistRepository.h>
#include <equora/core/ProjectRepository.h>
#include <equora/core/TagRepository.h>
#include <equora/core/TaskRepository.h>
#include <equora/storage/Database.h>
#include <equora/storage/Migrations.h>

namespace equora::testing {

// 内存库 + 全部仓库的共享夹具;device id 恒为 "device-test"。
class CoreDbTest : public ::testing::Test {
protected:
    void SetUp() override {
        db_ = storage::Database::open(":memory:");
        storage::applyMigrations(db_);
        tasks_ = std::make_unique<core::TaskRepository>(db_, "device-test");
        projects_ = std::make_unique<core::ProjectRepository>(db_, "device-test");
        tags_ = std::make_unique<core::TagRepository>(db_, "device-test");
        checklist_ = std::make_unique<core::ChecklistRepository>(db_, "device-test");
    }

    storage::Database db_;
    std::unique_ptr<core::TaskRepository> tasks_;
    std::unique_ptr<core::ProjectRepository> projects_;
    std::unique_ptr<core::TagRepository> tags_;
    std::unique_ptr<core::ChecklistRepository> checklist_;
};

} // namespace equora::testing
