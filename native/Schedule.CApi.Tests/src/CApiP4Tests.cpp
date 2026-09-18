#include <equora/capi/equora_capi.h>

#include <gtest/gtest.h>

#include <cstring>
#include <filesystem>
#include <fstream>
#include <string>

namespace {

constexpr int32_t kOk = 0;

class CApiP4Test : public ::testing::Test {
protected:
    void SetUp() override {
        core_ = eq_core_create(":memory:", "device-p4", &err_);
        ASSERT_NE(core_, nullptr);
    }
    void TearDown() override { eq_core_destroy(core_); }

    EqCore* core_ = nullptr;
    EqError err_{};
};

EqProjectInput* projectInput(const char* name) {
    static EqProjectInput in{};
    in.id = nullptr;
    in.name = name;
    in.color = nullptr;
    in.goal = nullptr;
    in.status = 0;
    in.revision = 0;
    return &in;
}

EqTaskInput* taskInput(const char* title) {
    static EqTaskInput in{};
    in.title = title;
    in.priority = 2;
    return &in;
}

EqTagInput* tagInput(const char* name) {
    static EqTagInput in{};
    in.name = name;
    in.revision = 0;
    return &in;
}

TEST_F(CApiP4Test, ProjectCrudThroughAbi) {
    EqProjectHandle* h = nullptr;
    ASSERT_EQ(eq_project_create(core_, projectInput("写作"), &h, &err_), kOk);
    EXPECT_STREQ(eq_project_view(h)->name, "写作");
    EXPECT_EQ(eq_project_view(h)->status, 0);
    const std::string id = eq_project_view(h)->id;
    eq_project_handle_destroy(h);

    // 归档 → 视图带时间戳。
    h = nullptr;
    ASSERT_EQ(eq_project_set_archived(core_, id.c_str(), 1, &h, &err_), kOk);
    EXPECT_EQ(eq_project_view(h)->status, 1);
    EXPECT_EQ(eq_project_view(h)->has_archived, 1);
    eq_project_handle_destroy(h);

    EqProjectList* list = nullptr;
    ASSERT_EQ(eq_project_list(core_, 0, 0, &list, &err_), kOk);
    EXPECT_EQ(eq_project_list_count(list), 0); // 排除归档
    eq_project_list_destroy(list);

    ASSERT_EQ(eq_project_list(core_, 1, 0, &list, &err_), kOk);
    EXPECT_EQ(eq_project_list_count(list), 1);
    eq_project_list_destroy(list);
}

TEST_F(CApiP4Test, TagAssignAndTaskTags) {
    EqTaskHandle* task = nullptr;
    ASSERT_EQ(eq_task_create(core_, taskInput("任务"), &task, &err_), kOk);
    const std::string taskId = eq_task_view(task)->id;
    eq_task_handle_destroy(task);

    EqTagHandle* tag = nullptr;
    ASSERT_EQ(eq_tag_create(core_, tagInput("focus"), &tag, &err_), kOk);
    const std::string tagId = eq_tag_view(tag)->id;
    eq_tag_handle_destroy(tag);

    // 重名 → Conflict(4)。
    EqTagHandle* dup = nullptr;
    EXPECT_EQ(eq_tag_create(core_, tagInput("focus"), &dup, &err_), 4);

    ASSERT_EQ(eq_task_add_tag(core_, taskId.c_str(), tagId.c_str(), &err_), kOk);

    EqTagList* tags = nullptr;
    ASSERT_EQ(eq_task_tags(core_, taskId.c_str(), &tags, &err_), kOk);
    ASSERT_EQ(eq_tag_list_count(tags), 1);
    EXPECT_STREQ(eq_tag_list_get(tags, 0)->name, "focus");
    eq_tag_list_destroy(tags);

    ASSERT_EQ(eq_task_remove_tag(core_, taskId.c_str(), tagId.c_str(), &err_), kOk);
    ASSERT_EQ(eq_task_tags(core_, taskId.c_str(), &tags, &err_), kOk);
    EXPECT_EQ(eq_tag_list_count(tags), 0);
    eq_tag_list_destroy(tags);
}

TEST_F(CApiP4Test, ChecklistThroughAbi) {
    EqTaskHandle* task = nullptr;
    ASSERT_EQ(eq_task_create(core_, taskInput("复习"), &task, &err_), kOk);
    const std::string taskId = eq_task_view(task)->id;
    eq_task_handle_destroy(task);

    EqChecklistHandle* item = nullptr;
    ASSERT_EQ(eq_checklist_add(core_, taskId.c_str(), "第三章", &item, &err_), kOk);
    EXPECT_EQ(eq_checklist_view(item)->sort_order, 1);
    const std::string itemId = eq_checklist_view(item)->id;
    const int64_t revision = eq_checklist_view(item)->revision;
    eq_checklist_handle_destroy(item);

    // 勾选(更新)。
    EqChecklistInput upd{};
    upd.id = itemId.c_str();
    upd.task_id = taskId.c_str();
    upd.content = "第三章(完成)";
    upd.is_checked = 1;
    upd.sort_order = 1;
    upd.revision = revision;
    item = nullptr;
    ASSERT_EQ(eq_checklist_update(core_, &upd, &item, &err_), kOk);
    EXPECT_EQ(eq_checklist_view(item)->is_checked, 1);
    eq_checklist_handle_destroy(item);

    EqChecklistList* list = nullptr;
    ASSERT_EQ(eq_checklist_list(core_, taskId.c_str(), &list, &err_), kOk);
    EXPECT_EQ(eq_checklist_list_count(list), 1);
    eq_checklist_list_destroy(list);

    ASSERT_EQ(eq_checklist_remove(core_, itemId.c_str(), &err_), kOk);
    ASSERT_EQ(eq_checklist_list(core_, taskId.c_str(), &list, &err_), kOk);
    EXPECT_EQ(eq_checklist_list_count(list), 0);
    eq_checklist_list_destroy(list);
}

TEST_F(CApiP4Test, TaskQueryFilter) {
    EqTaskHandle* h = nullptr;
    EqTaskInput withDue = *taskInput("有截止");
    withDue.due_at = 1'789'790'400'000;
    withDue.has_due = 1;
    ASSERT_EQ(eq_task_create(core_, &withDue, &h, &err_), kOk);
    eq_task_handle_destroy(h);
    ASSERT_EQ(eq_task_create(core_, taskInput("无截止"), &h, &err_), kOk);
    eq_task_handle_destroy(h);

    EqTaskFilter filter{};
    filter.due_state = 2; // 无截止
    EqTaskList* list = nullptr;
    ASSERT_EQ(eq_task_query(core_, &filter, &list, &err_), kOk);
    ASSERT_EQ(eq_task_list_count(list), 1);
    EXPECT_STREQ(eq_task_list_get(list, 0)->title, "无截止");
    eq_task_list_destroy(list);

    // due 区间。
    EqTaskFilter range{};
    range.due_state = 1;
    range.due_after = 1'789'776'000'000;
    range.due_before = 1'789'862'400'000;
    ASSERT_EQ(eq_task_query(core_, &range, &list, &err_), kOk);
    EXPECT_EQ(eq_task_list_count(list), 1);
    EXPECT_STREQ(eq_task_list_get(list, 0)->title, "有截止");
    eq_task_list_destroy(list);

    // 搜索。
    EqTaskFilter search{};
    search.search = "截止";
    ASSERT_EQ(eq_task_query(core_, &search, &list, &err_), kOk);
    EXPECT_EQ(eq_task_list_count(list), 2);
    eq_task_list_destroy(list);
}

TEST_F(CApiP4Test, DeviceIdStable) {
    EqStringHandle* a = nullptr;
    ASSERT_EQ(eq_core_device_id(core_, &a, &err_), kOk);
    const std::string first = eq_string_data(a);
    eq_string_destroy(a);
    ASSERT_EQ(first.size(), 36U);

    EqStringHandle* b = nullptr;
    ASSERT_EQ(eq_core_device_id(core_, &b, &err_), kOk);
    EXPECT_STREQ(eq_string_data(b), first.c_str());
    eq_string_destroy(b);
}

TEST_F(CApiP4Test, BackupAndExportImportThroughAbi) {
    // 备份恢复要求文件库::memory: 无路径可替换,故用临时文件库。
    std::filesystem::path dir = std::filesystem::temp_directory_path() /
        ("equora-capi-p4-" + std::to_string(::testing::UnitTest::GetInstance()
                                                ->current_test_info()->line()));
    std::error_code ec;
    std::filesystem::remove_all(dir, ec);
    std::filesystem::create_directories(dir, ec);

    EqCore* fileCore = eq_core_create((dir / "main.db").string().c_str(), "device-p4", &err_);
    ASSERT_NE(fileCore, nullptr);

    EqTaskHandle* h = nullptr;
    ASSERT_EQ(eq_task_create(fileCore, taskInput("备份源任务"), &h, &err_), kOk);
    eq_task_handle_destroy(h);

    // 备份 + 校验。
    EqStringHandle* path = nullptr;
    ASSERT_EQ(eq_backup_create(fileCore, dir.string().c_str(), &path, &err_), kOk);
    const std::string backupPath = eq_string_data(path);
    eq_string_destroy(path);
    int32_t ok = 0;
    ASSERT_EQ(eq_backup_verify(backupPath.c_str(), &ok, &err_), kOk);
    EXPECT_EQ(ok, 1);

    // 导出 JSON / CSV。
    const auto jsonPath = (dir / "tasks.json").string();
    const auto csvPath = (dir / "tasks.csv").string();
    int32_t count = 0;
    ASSERT_EQ(eq_export_tasks_json(fileCore, jsonPath.c_str(), &count, &err_), kOk);
    EXPECT_EQ(count, 1);
    ASSERT_EQ(eq_export_tasks_csv(fileCore, csvPath.c_str(), &count, &err_), kOk);
    EXPECT_EQ(count, 1);

    // 再建一条任务后恢复备份 → 任务数回到 1。
    ASSERT_EQ(eq_task_create(fileCore, taskInput("备份后新增"), &h, &err_), kOk);
    eq_task_handle_destroy(h);
    ASSERT_EQ(eq_backup_restore(fileCore, backupPath.c_str(), &err_), kOk);
    EqTaskList* list = nullptr;
    ASSERT_EQ(eq_task_list_all(fileCore, 0, &list, &err_), kOk);
    EXPECT_EQ(eq_task_list_count(list), 1);
    eq_task_list_destroy(list);

    // 导入同一 JSON:幂等跳过。
    int32_t imported = -1, skipped = -1;
    ASSERT_EQ(eq_import_tasks_json(fileCore, jsonPath.c_str(), &imported, &skipped, &err_),
              kOk);
    EXPECT_EQ(imported, 0);
    EXPECT_EQ(skipped, 1);

    eq_core_destroy(fileCore);
    std::filesystem::remove_all(dir, ec);
}

} // namespace
