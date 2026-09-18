#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/core/Transfer.h>
#include <equora/domain/Error.h>

namespace {

using namespace equora;

using equora::core::importTasksJson;
using equora::core::exportTasksCsv;
using equora::core::exportTasksJson;
using equora::domain::EquoraError;
using equora::domain::Task;
using equora::testing::CoreDbTest;

TEST_F(CoreDbTest, JsonExportShape) {
    Task t = Task::draft("导出任务");
    t.note = "说明";
    t.estimateMinutes = 30;
    (void)tasks_->create(t);

    const std::string json = exportTasksJson(*tasks_);
    EXPECT_NE(json.find("\"format\": \"equora.tasks\""), std::string::npos);
    EXPECT_NE(json.find("\"version\": 1"), std::string::npos);
    EXPECT_NE(json.find("导出任务"), std::string::npos);
    EXPECT_NE(json.find("estimate_minutes"), std::string::npos);
}

TEST_F(CoreDbTest, JsonExportImportRoundtrip) {
    Task a = Task::draft("任务甲");
    a.dueAt = 1'789'790'400'000;
    (void)tasks_->create(a);
    (void)tasks_->create(Task::draft("任务乙"));

    const std::string json = exportTasksJson(*tasks_);

    // 导入到全新数据库。
    auto db2 = storage::Database::open(":memory:");
    storage::applyMigrations(db2);
    core::TaskRepository repo2(db2, "device-import");

    const auto first = importTasksJson(repo2, json);
    EXPECT_EQ(first.imported, 2);
    EXPECT_EQ(first.skipped, 0);

    const auto round = exportTasksJson(repo2);
    const auto again = importTasksJson(repo2, json); // 幂等:全部跳过
    EXPECT_EQ(again.imported, 0);
    EXPECT_EQ(again.skipped, 2);

    // 关键字段往返一致。
    const auto imported = repo2.query(core::TaskFilter{});
    ASSERT_EQ(imported.size(), 2U);
    bool foundDue = false;
    for (const auto& t : imported) {
        if (t.title == "任务甲") {
            foundDue = t.dueAt.has_value() && *t.dueAt == 1'789'790'400'000;
        }
    }
    EXPECT_TRUE(foundDue);
    (void)round;
}

TEST_F(CoreDbTest, JsonImportSkipsInvalidEntries) {
    const std::string json = R"JSON({
      "format": "equora.tasks", "version": 1,
      "tasks": [
        {"id": "not-a-uuid", "title": "坏id", "created_at": "2026-09-18T00:00:00.000Z",
         "updated_at": "2026-09-18T00:00:00.000Z"},
        {"id": "01234567-89ab-cdef-0123-456789abcdef", "title": "   ",
         "created_at": "2026-09-18T00:00:00.000Z", "updated_at": "2026-09-18T00:00:00.000Z"},
        {"id": "01234567-89ab-cdef-0123-456789abcdee", "title": "正常导入",
         "created_at": "2026-09-18T00:00:00.000Z", "updated_at": "2026-09-18T00:00:00.000Z"}
      ]
    })JSON";

    const auto result = importTasksJson(*tasks_, json);
    EXPECT_EQ(result.imported, 1);
    EXPECT_EQ(result.skipped, 2);
}

TEST_F(CoreDbTest, JsonImportRejectsMalformedDocument) {
    try {
        (void)importTasksJson(*tasks_, "{ not json");
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), equora::domain::ErrorCode::InvalidArgument);
    }
    try {
        (void)importTasksJson(*tasks_, "{\"tasks\": 42}");
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), equora::domain::ErrorCode::InvalidArgument);
    }
}

TEST_F(CoreDbTest, CsvExportFormat) {
    Task tricky = Task::draft("带,逗号 \"引号\"\n换行");
    tricky.note = "备注";
    (void)tasks_->create(tricky);
    (void)tasks_->create(Task::draft("普通任务"));

    const std::string csv = exportTasksCsv(*tasks_);
    EXPECT_EQ(csv.substr(0, 3), "\xEF\xBB\xBF"); // UTF-8 BOM
    EXPECT_NE(csv.find("id,title,note,status,priority"), std::string::npos);
    EXPECT_NE(csv.find("\"带,逗号 \"\"引号\"\"\n换行\""), std::string::npos);
    EXPECT_NE(csv.find("普通任务"), std::string::npos);
}

} // namespace
