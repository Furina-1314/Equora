#include <equora/capi/equora_capi.h>

#include <gtest/gtest.h>

#include <cstring>
#include <set>
#include <string>

namespace {

constexpr int32_t kOk = 0;

EqTaskInput makeInput(const char* title) {
    EqTaskInput in{};
    in.id = nullptr;
    in.title = title;
    in.note = nullptr;
    in.status = 0;
    in.priority = 2;
    in.importance = 0;
    in.due_at = 0;
    in.has_due = 0;
    in.estimate_minutes = 0;
    in.has_estimate = 0;
    in.actual_minutes = 0;
    in.project_id = nullptr;
    in.has_project = 0;
    in.revision = 0;
    return in;
}

class CApiTest : public ::testing::Test {
protected:
    void SetUp() override {
        EqError err{};
        core_ = eq_core_create(":memory:", "device-test", &err);
        ASSERT_NE(core_, nullptr);
        ASSERT_EQ(err.code, kOk);
    }
    void TearDown() override {
        eq_core_destroy(core_);
        core_ = nullptr;
    }
    EqCore* core_ = nullptr;
};

TEST(CApiBasics, VersionAndPing) {
    EXPECT_EQ(eq_api_version(), 2); // capi v2
    EXPECT_EQ(eq_ping(0), 1);
    EXPECT_EQ(eq_ping(41), 42);
    EXPECT_EQ(eq_ping(-2), -1);
    EXPECT_STRNE(eq_version_string(), "");
}

TEST(CApiBasics, NullSafety) {
    eq_core_destroy(nullptr);          // 不得崩溃
    eq_task_handle_destroy(nullptr);
    eq_task_list_destroy(nullptr);
    EXPECT_EQ(eq_task_view(nullptr), nullptr);
    EXPECT_EQ(eq_task_list_get(nullptr, 0), nullptr);
    EXPECT_EQ(eq_task_list_count(nullptr), 0);
}

TEST_F(CApiTest, SchemaVersionAfterCreate) {
    EqError err{};
    int32_t version = 0;
    ASSERT_EQ(eq_core_schema_version(core_, &version, &err), kOk);
    EXPECT_EQ(version, 1);
}

TEST_F(CApiTest, SchemaVersionNullCoreFillsError) {
    EqError err{};
    err.code = 999;
    int32_t version = 0;
    const int32_t rc = eq_core_schema_version(nullptr, &version, &err);
    EXPECT_NE(rc, kOk);
    EXPECT_EQ(err.code, static_cast<int32_t>(2)); // InvalidArgument
    EXPECT_GT(std::strlen(err.message), 0);
}

TEST_F(CApiTest, TaskCreateReturnsHandleAndView) {
    EqError err{};
    EqTaskInput in = makeInput("写季度总结");
    in.note = "含数据回顾";
    in.importance = 4;
    in.due_at = 1'800'000'000'000;
    in.has_due = 1;
    in.estimate_minutes = 60;
    in.has_estimate = 1;

    EqTaskHandle* h = nullptr;
    ASSERT_EQ(eq_task_create(core_, &in, &h, &err), kOk);
    const EqTaskView* v = eq_task_view(h);
    ASSERT_NE(v, nullptr);
    EXPECT_STREQ(v->title, "写季度总结");
    EXPECT_STREQ(v->note, "含数据回顾");
    EXPECT_EQ(v->importance, 4);
    EXPECT_EQ(v->has_due, 1);
    EXPECT_EQ(v->due_at, 1'800'000'000'000);
    EXPECT_EQ(v->has_estimate, 1);
    EXPECT_EQ(v->estimate_minutes, 60);
    EXPECT_EQ(v->revision, 1);
    EXPECT_EQ(v->has_deleted, 0);
    EXPECT_EQ(v->has_project, 0);
    EXPECT_EQ(v->project_id, nullptr);
    EXPECT_STREQ(v->last_device_id, "device-test");
    EXPECT_NE(v->id, nullptr);
    EXPECT_GT(std::strlen(v->id), 30); // UUID 文本长度 36

    eq_task_handle_destroy(h);
}

TEST_F(CApiTest, TaskCreateBlankTitleFillsError) {
    EqError err{};
    EqTaskInput in = makeInput("   ");
    EqTaskHandle* h = reinterpret_cast<EqTaskHandle*>(0x1);
    const int32_t rc = eq_task_create(core_, &in, &h, &err);
    EXPECT_EQ(rc, static_cast<int32_t>(2)); // InvalidArgument
    EXPECT_EQ(err.code, rc);
    EXPECT_GT(std::strlen(err.message), 0);
    // 出错时不覆写调用方的句柄指针(保持可识别的哨兵)。
    EXPECT_EQ(h, reinterpret_cast<EqTaskHandle*>(0x1));
}

TEST_F(CApiTest, TaskGetUpdateDeleteRoundtrip) {
    EqError err{};
    EqTaskInput in = makeInput("初稿");
    EqTaskHandle* h = nullptr;
    ASSERT_EQ(eq_task_create(core_, &in, &h, &err), kOk);
    const std::string id = eq_task_view(h)->id;
    eq_task_handle_destroy(h);

    // 读取。
    h = nullptr;
    ASSERT_EQ(eq_task_get(core_, id.c_str(), 0, &h, &err), kOk);
    ASSERT_NE(h, nullptr);
    EXPECT_STREQ(eq_task_view(h)->title, "初稿");

    // 更新:携带当前 revision。
    EqTaskInput upd = makeInput("二稿");
    upd.id = id.c_str();
    upd.revision = eq_task_view(h)->revision;
    eq_task_handle_destroy(h);
    h = nullptr;
    ASSERT_EQ(eq_task_update(core_, &upd, &h, &err), kOk);
    EXPECT_STREQ(eq_task_view(h)->title, "二稿");
    EXPECT_EQ(eq_task_view(h)->revision, 2);
    eq_task_handle_destroy(h);

    // 软删除 → 默认 get NotFound,include_deleted 可见墓碑。
    h = nullptr;
    ASSERT_EQ(eq_task_set_deleted(core_, id.c_str(), 1, &h, &err), kOk);
    EXPECT_EQ(eq_task_view(h)->has_deleted, 1);
    eq_task_handle_destroy(h);

    h = reinterpret_cast<EqTaskHandle*>(0x1);
    EXPECT_EQ(eq_task_get(core_, id.c_str(), 0, &h, &err), static_cast<int32_t>(3)); // NotFound
    h = nullptr;
    ASSERT_EQ(eq_task_get(core_, id.c_str(), 1, &h, &err), kOk);
    EXPECT_EQ(eq_task_view(h)->has_deleted, 1);
    eq_task_handle_destroy(h);
}

TEST_F(CApiTest, TaskUpdateStaleRevisionConflicts) {
    EqError err{};
    EqTaskInput in = makeInput("并发测试");
    EqTaskHandle* h = nullptr;
    ASSERT_EQ(eq_task_create(core_, &in, &h, &err), kOk);
    const std::string id = eq_task_view(h)->id;
    const int64_t staleRevision = eq_task_view(h)->revision;
    eq_task_handle_destroy(h);

    // 第一次更新成功。
    EqTaskInput upd = makeInput("已修改");
    upd.id = id.c_str();
    upd.revision = staleRevision;
    h = nullptr;
    ASSERT_EQ(eq_task_update(core_, &upd, &h, &err), kOk);
    eq_task_handle_destroy(h);

    // 用旧 revision 再改 → Conflict(4),不返回句柄。
    h = reinterpret_cast<EqTaskHandle*>(0x1);
    EXPECT_EQ(eq_task_update(core_, &upd, &h, &err), static_cast<int32_t>(4));
    EXPECT_EQ(err.code, 4);
}

TEST_F(CApiTest, TaskUpdateRequiresId) {
    EqError err{};
    EqTaskInput in = makeInput("无 id");
    EqTaskHandle* h = nullptr;
    EXPECT_EQ(eq_task_update(core_, &in, &h, &err), static_cast<int32_t>(2));
}

TEST_F(CApiTest, ListAllBatchInterface) {
    EqError err{};
    int created = 0;
    for (int i = 0; i < 3; ++i) {
        const std::string title = "批量 " + std::to_string(i);
        EqTaskInput in = makeInput(title.c_str());
        EqTaskHandle* h = nullptr;
        ASSERT_EQ(eq_task_create(core_, &in, &h, &err), kOk) << title;
        eq_task_handle_destroy(h);
        ++created;
    }

    EqTaskList* list = nullptr;
    ASSERT_EQ(eq_task_list_all(core_, 0, &list, &err), kOk);
    ASSERT_NE(list, nullptr);
    ASSERT_EQ(eq_task_list_count(list), created);
    std::set<std::string> titles; // 同毫秒创建时顺序按 UUID,不假设插入序
    for (int32_t i = 0; i < created; ++i) {
        const EqTaskView* v = eq_task_list_get(list, i);
        ASSERT_NE(v, nullptr);
        titles.insert(v->title);
    }
    for (int i = 0; i < created; ++i) {
        EXPECT_TRUE(titles.count("批量 " + std::to_string(i)) > 0);
    }
    EXPECT_EQ(eq_task_list_get(list, created), nullptr); // 越界
    EXPECT_EQ(eq_task_list_get(list, -1), nullptr);
    eq_task_list_destroy(list);
}

TEST_F(CApiTest, CreateWithUtf8ChineseTitleRoundtrip) {
    EqError err{};
    EqTaskInput in = makeInput("专注 45 分钟 — 复习《模拟电子技术》§3.2 ✅");
    EqTaskHandle* h = nullptr;
    ASSERT_EQ(eq_task_create(core_, &in, &h, &err), kOk);
    EXPECT_STREQ(eq_task_view(h)->title, "专注 45 分钟 — 复习《模拟电子技术》§3.2 ✅");
    eq_task_handle_destroy(h);
}

TEST_F(CApiTest, NullArgumentsRejected) {
    EqError err{};
    EqTaskHandle* h = nullptr;
    EXPECT_EQ(eq_task_create(nullptr, nullptr, &h, &err), static_cast<int32_t>(2));
    EXPECT_EQ(eq_task_get(core_, nullptr, 0, &h, &err), static_cast<int32_t>(2));
    EqTaskList* list = nullptr;
    EXPECT_EQ(eq_task_list_all(nullptr, 0, &list, &err), static_cast<int32_t>(2));
}

} // namespace
