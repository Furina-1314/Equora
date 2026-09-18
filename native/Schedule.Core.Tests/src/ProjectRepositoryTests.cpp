#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/domain/Error.h>

namespace {

using equora::domain::ErrorCode;
using equora::domain::EquoraError;
using equora::domain::Project;
using equora::domain::ProjectStatus;
using equora::testing::CoreDbTest;

TEST_F(CoreDbTest, ProjectCreateFillsSyncFields) {
    const Project p = projects_->create(Project::draft("产品重构"));
    EXPECT_FALSE(p.id.empty());
    EXPECT_EQ(p.status, ProjectStatus::Active);
    EXPECT_EQ(p.revision, 1);
    EXPECT_FALSE(p.archivedAt.has_value());
    EXPECT_EQ(p.lastDeviceId, "device-test");
}

TEST_F(CoreDbTest, ProjectBlankNameRejected) {
    try {
        (void)projects_->create(Project::draft("  "));
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::InvalidArgument);
    }
}

TEST_F(CoreDbTest, ProjectUpdateAndArchive) {
    Project p = projects_->create(Project::draft("写作"));
    p.name = "技术写作";
    p.color = "#3366CC";
    const Project updated = projects_->update(p);
    EXPECT_EQ(updated.revision, 2);
    EXPECT_EQ(updated.name, "技术写作");

    const Project archived = projects_->setArchived(p.id, true);
    EXPECT_EQ(archived.status, ProjectStatus::Archived);
    EXPECT_TRUE(archived.archivedAt.has_value());
    EXPECT_EQ(projects_->setArchived(p.id, true).revision, archived.revision); // 幂等

    const Project restored = projects_->setArchived(p.id, false);
    EXPECT_EQ(restored.status, ProjectStatus::Active);
    EXPECT_FALSE(restored.archivedAt.has_value());
}

TEST_F(CoreDbTest, ProjectListFiltersAndOrders) {
    (void)projects_->create(Project::draft("banana"));
    (void)projects_->create(Project::draft("Apple"));
    const Project zombie = projects_->create(Project::draft("Cherry"));
    (void)projects_->setArchived(projects_->create(Project::draft("归档项")).id, true);

    auto names = [](const std::vector<Project>& v) {
        std::vector<std::string> out;
        for (const auto& p : v) out.push_back(p.name);
        return out;
    };

    EXPECT_EQ(names(projects_->list()),
              (std::vector<std::string>{"Apple", "banana", "Cherry", "归档项"}));
    EXPECT_EQ(names(projects_->list(/*includeArchived=*/false)),
              (std::vector<std::string>{"Apple", "banana", "Cherry"}));

    (void)projects_->setDeleted(zombie.id, true);
    EXPECT_EQ(projects_->list().size(), 3U); // 归档项仍在(归档不是删除)
    EXPECT_TRUE(projects_->findById(zombie.id, /*includeDeleted=*/true).has_value());
}

TEST_F(CoreDbTest, ProjectUpdateStaleRevisionConflicts) {
    const Project p = projects_->create(Project::draft("并发"));
    Project stale = p;
    Project fresh = p;
    (void)projects_->update(fresh);
    try {
        (void)projects_->update(stale);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }
}

} // namespace
