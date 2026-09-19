#include "TestDb.h"

#include <gtest/gtest.h>

#include <equora/core/FocusRepository.h>
#include <equora/domain/Error.h>
#include <equora/domain/Focus.h>

namespace {

using equora::core::FocusRepository;
using equora::domain::EquoraError;
using equora::domain::ErrorCode;
using equora::domain::FocusMode;
using equora::domain::FocusProfile;
using equora::domain::SessionState;

FocusProfile with_name(const FocusProfile& p, const char* name) {
    FocusProfile copy = p;
    copy.name = name;
    return copy;
}

constexpr equora::domain::UtcMillis T0 = 1'789'689'600'000; // 2026-09-18T00:00Z

class FocusTest : public equora::testing::CoreDbTest {
protected:
    void SetUp() override {
        equora::testing::CoreDbTest::SetUp();
        focus_ = std::make_unique<FocusRepository>(db_, "device-test");
    }
    std::unique_ptr<FocusRepository> focus_;
};

TEST_F(FocusTest, StartPauseResumeCompleteLifecycle) {
    auto s = focus_->start(FocusMode::Pomodoro, 25, "写方案", std::nullopt, std::nullopt, T0);
    EXPECT_EQ(s.state, SessionState::Running);
    ASSERT_TRUE(s.plannedEnd.has_value());
    EXPECT_EQ(*s.plannedEnd - s.plannedStart, 25 * 60'000);

    // 同一时刻只允许一个开放会话。
    try {
        (void)focus_->start(FocusMode::Deep, 60, "", std::nullopt, std::nullopt, T0 + 1000);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }

    // 10 分钟后暂停 5 分钟再恢复。
    const auto paused = focus_->pause(s.id, T0 + 10 * 60'000);
    EXPECT_EQ(paused.state, SessionState::Paused);
    const auto resumed = focus_->resume(s.id, T0 + 15 * 60'000);
    EXPECT_EQ(resumed.pausedMs, 5 * 60'000);

    // 状态不符的操作被拒绝:运行中再次 resume。
    try {
        (void)focus_->resume(s.id, T0 + 16 * 60'000);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }

    // 再暂停 5 分钟后完成:有效时长 = 40 - 10 暂停 = 30 分钟。
    (void)focus_->pause(s.id, T0 + 35 * 60'000);
    const auto done = focus_->complete(s.id, T0 + 40 * 60'000, "完成初稿", 80);
    EXPECT_EQ(done.state, SessionState::Completed);
    ASSERT_TRUE(done.actualEnd.has_value());
    EXPECT_EQ(done.effectiveMs(T0 + 40 * 60'000), 30 * 60'000);
    EXPECT_EQ(done.completionLevel, 80);
    EXPECT_EQ(done.pausedMs, 10 * 60'000);

    // 完成后再操作被拒绝。
    try {
        (void)focus_->abandon(s.id, T0 + 41 * 60'000);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }
}

TEST_F(FocusTest, FlowtimeHasNoPlannedEnd) {
    auto s = focus_->start(FocusMode::Flowtime, 0, "自由写", std::nullopt, std::nullopt, T0);
    EXPECT_FALSE(s.plannedEnd.has_value());
    const auto done = focus_->complete(s.id, T0 + 73 * 60'000, "", -1);
    EXPECT_EQ(done.effectiveMs(T0), 73 * 60'000);
}

TEST_F(FocusTest, LinkedToTaskAndHistory) {
    const auto task = tasks_->create(equora::domain::Task::draft("专注对象"));
    auto s = focus_->start(FocusMode::Deep, 90, "", task.id, std::nullopt, T0);
    ASSERT_TRUE(s.taskId.has_value());
    (void)focus_->complete(s.id, T0 + 30 * 60'000, "", 60);

    const auto history = focus_->history(T0, T0 + 86'400'000);
    ASSERT_EQ(history.size(), 1U);
    EXPECT_EQ(history[0].taskId, task.id);
    EXPECT_FALSE(focus_->openSession().has_value());
}

TEST_F(FocusTest, InterruptionsRecorded) {
    auto s = focus_->start(FocusMode::Pomodoro, 25, "", std::nullopt, std::nullopt, T0);
    (void)focus_->addInterruption(s.id, T0 + 5 * 60'000, 60'000, "查消息", "manual", "返回");
    (void)focus_->addInterruption(s.id, T0 + 12 * 60'000, 2 * 60'000, "被叫走", "app-switch", "其他");

    const auto list = focus_->interruptionsOf(s.id);
    ASSERT_EQ(list.size(), 2U);
    EXPECT_EQ(list[0].reason, "查消息");
    EXPECT_EQ(list[1].source, "app-switch");
}

TEST_F(FocusTest, DistractionCaptureImmediatePersistAndResolve) {
    auto s = focus_->start(FocusMode::Deep, 90, "", std::nullopt, std::nullopt, T0);
    const auto d1 = focus_->capture("查一下 XPath 语法", s.id, T0 + 60'000);
    const auto d2 = focus_->capture("周末买礼物", std::nullopt, T0 + 65'000);

    // 空白内容拒绝。
    try {
        (void)focus_->capture("   ", std::nullopt, T0);
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::InvalidArgument);
    }

    auto pending = focus_->pendingDistractions();
    ASSERT_EQ(pending.size(), 2U);
    EXPECT_EQ(pending[0].content, "查一下 XPath 语法");

    // 整理为任务:resolution=1;重复整理被拒。
    const auto resolved = focus_->resolveDistraction(d1.id, 1, "new-task-id");
    EXPECT_EQ(resolved.resolution, 1);
    try {
        (void)focus_->resolveDistraction(d1.id, 5, "");
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }

    pending = focus_->pendingDistractions();
    ASSERT_EQ(pending.size(), 1U);
    EXPECT_EQ(pending[0].content, "周末买礼物");
}

TEST_F(FocusTest, CrashRecoveryClosesOrphansAtLastActivity) {
    // 会话 A:运行中"崩溃"(直接留库,无闭合)。
    auto a = focus_->start(FocusMode::Pomodoro, 25, "", std::nullopt, std::nullopt, T0);
    // 20 分钟时记录了一次中断(推进 updated_at),随后崩溃。
    (void)focus_->addInterruption(a.id, T0 + 20 * 60'000, 30'000, "走神", "manual", "返回");

    const int recovered = focus_->recoverInterrupted(T0 + 3 * 86'400'000);
    EXPECT_EQ(recovered, 1);

    const auto closedA = focus_->find(a.id).value();
    ASSERT_TRUE(closedA.actualEnd.has_value());
    EXPECT_EQ(*closedA.actualEnd, T0 + 20 * 60'000); // 以最后活动收尾
    EXPECT_EQ(closedA.state, SessionState::Abandoned);

    // 恢复后可开新会话;分心记录不丢。
    EXPECT_FALSE(focus_->openSession().has_value());
    auto c = focus_->start(FocusMode::Pomodoro, 25, "", std::nullopt, std::nullopt,
                           T0 + 4 * 86'400'000);
    EXPECT_EQ(c.state, SessionState::Running);
}

TEST_F(FocusTest, ProfileCrudAndUniqueName) {
    auto p = FocusProfile::draft("编程");
    p.mode = FocusMode::Deep;
    p.plannedMinutes = 120;
    p.allowedApps = "[\"devenv.exe\",\"windows_terminal.exe\"]";
    const auto created = focus_->createProfile(p);
    EXPECT_EQ(created.revision, 1);

    try {
        (void)focus_->createProfile(FocusProfile::draft("编程")); // 重名
        FAIL();
    } catch (const EquoraError& e) {
        EXPECT_EQ(e.code(), ErrorCode::Conflict);
    }

    auto fetched = focus_->findProfile(created.id).value();
    auto updated = focus_->updateProfile(with_name(fetched, "编程深度"));
    EXPECT_EQ(updated.revision, 2);
    EXPECT_EQ(updated.name, "编程深度");
    EXPECT_EQ(updated.mode, FocusMode::Deep);

    focus_->deleteProfile(created.id);
    EXPECT_FALSE(focus_->findProfile(created.id).has_value());
    EXPECT_TRUE(focus_->listProfiles().empty());
}

namespace {
FocusProfile with_name(const FocusProfile& p, const char* name) {
    FocusProfile copy = p;
    copy.name = name;
    return copy;
}
} // namespace

} // namespace
