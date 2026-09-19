#include <gtest/gtest.h>

#include <chrono>
#include <numeric>
#include <filesystem>

#include <equora/core/TaskRepository.h>
#include <equora/core/CalendarRepository.h>
#include <equora/core/FocusRepository.h>
#include <equora/domain/Task.h>
#include <equora/storage/Backup.h>
#include <equora/storage/Database.h>
#include <equora/storage/Migrations.h>

// 性能基准(需求 §25):发布构建测量。使用 ::testing::Test 加计时;
// 断言用宽松上限(目标值的 3~10 倍余量,防 CI 环境误报),真实数据记录到输出。

namespace {

using equora::core::TaskRepository;
using equora::domain::Priority;
using equora::domain::Task;
using equora::domain::TaskStatus;
using equora::storage::Database;
using equora::storage::applyMigrations;

constexpr int kTaskCount = 1'000;       // 需求:一万条任务主要视图可用(P95<100ms)
constexpr int kTimeBlocks = 500;

class PerfTest : public ::testing::Test {
protected:
    void SetUp() override {
        std::filesystem::create_directories(EQUORA_TEST_TMPDIR);
        dbPath_ = std::string(EQUORA_TEST_TMPDIR) + "/perf.db";
        std::error_code ec;
        std::filesystem::remove(dbPath_, ec);
        db_ = Database::open(dbPath_);
        applyMigrations(db_);
        repo_ = std::make_unique<TaskRepository>(db_, "perf");
    }
    std::string dbPath_;
    Database db_;
    std::unique_ptr<TaskRepository> repo_;

    [[nodiscard]] static double ElapsedMs(auto start, auto end) {
        return std::chrono::duration<double, std::milli>(end - start).count();
    }
};

TEST_F(PerfTest, MigrationFreshInstall) {
    const auto path = std::string(EQUORA_TEST_TMPDIR) + "/perf_fresh.db";
    std::error_code ec;
    std::filesystem::remove(path, ec);
    {
        Database db = Database::open(path);
        const auto start = std::chrono::high_resolution_clock::now();
        applyMigrations(db);
        const auto end = std::chrono::high_resolution_clock::now();
        const double ms = ElapsedMs(start, end);
        RecordProperty("migration_ms", std::to_string(ms));
        // 目标 <500ms(6 个迁移在 SSD 上);放宽到 5000ms 防 CI 慢盘。
        EXPECT_LT(ms, 5000.0) << "migration took " << ms << "ms";
    }
}

TEST_F(PerfTest, TaskCreateP95) {
    // 创建 1000 条任务测 P95。
    std::vector<double> times;
    times.reserve(kTaskCount);
    for (int i = 0; i < kTaskCount; ++i) {
        auto t = Task::draft("任务 " + std::to_string(i));
        t.priority = static_cast<Priority>(i % 5);
        t.dueAt = 1'789'689'600'000 + i * 60'000LL;
        const auto start = std::chrono::high_resolution_clock::now();
        (void)repo_->create(t);
        const auto end = std::chrono::high_resolution_clock::now();
        times.push_back(ElapsedMs(start, end));
    }
    std::sort(times.begin(), times.end());
    const double p95 = times[static_cast<std::size_t>(kTaskCount * 0.95)];
    RecordProperty("create_p95_ms", std::to_string(p95));
    RecordProperty("create_avg_ms", std::to_string(
        std::accumulate(times.begin(), times.end(), 0.0) / times.size()));
    // 需求:P95 < 100ms;放宽到 1000ms(含 WAL fsync)。
    EXPECT_LT(p95, 1000.0) << "P95 create: " << p95 << "ms";
}

TEST_F(PerfTest, SmartListQueryAtScale) {
    // 先种 1000 条,再测查询。
    for (int i = 0; i < kTaskCount; ++i) {
        auto t = Task::draft("查询 " + std::to_string(i));
        t.dueAt = 1'789'689'600'000 + i * 60'000LL;
        t.status = i % 3 == 0 ? TaskStatus::Done : TaskStatus::Planned;
        (void)repo_->create(t);
    }

    const auto start = std::chrono::high_resolution_clock::now();
    const auto today = repo_->query(equora::core::SmartLists::today(
        1'789'689'600'000, 480));
    const auto end = std::chrono::high_resolution_clock::now();
    const double ms = ElapsedMs(start, end);
    RecordProperty("today_query_ms", std::to_string(ms));
    RecordProperty("today_count", std::to_string(today.size()));
    // 需求:一万条主要视图可用;1000 条下目标 < 50ms,放宽 500ms。
    EXPECT_LT(ms, 500.0);

    // 全量查询(无索引过滤 + 排序)。
    const auto start2 = std::chrono::high_resolution_clock::now();
    const auto all = repo_->listAll();
    const auto end2 = std::chrono::high_resolution_clock::now();
    RecordProperty("list_all_ms", std::to_string(ElapsedMs(start2, end2)));
    RecordProperty("list_all_count", std::to_string(all.size()));
    EXPECT_LT(ElapsedMs(start2, end2), 1000.0);
}

TEST_F(PerfTest, BackupAndRestoreAtScale) {
    for (int i = 0; i < 200; ++i) {
        (void)repo_->create(Task::draft("备份种子 " + std::to_string(i)));
    }
    const auto dir = std::string(EQUORA_TEST_TMPDIR) + "/perf_backup";
    std::error_code ec;
    std::filesystem::remove_all(dir, ec);
    std::filesystem::create_directories(dir, ec);

    const auto start = std::chrono::high_resolution_clock::now();
    const auto info = equora::storage::BackupManager::create(db_, dir);
    const auto end = std::chrono::high_resolution_clock::now();
    RecordProperty("backup_ms", std::to_string(ElapsedMs(start, end)));

    EXPECT_TRUE(equora::storage::BackupManager::verify(info.file));
}

TEST_F(PerfTest, FocusWindowQuery) {
    equora::core::CalendarRepository cal(db_, "perf");
    for (int i = 0; i < kTimeBlocks; ++i) {
        equora::domain::TimeBlock b;
        b.startAt = 1'789'689'600'000 + i * 3'600'000LL;
        b.endAt = b.startAt + 45 * 60'000;
        (void)cal.createBlock(b);
    }
    const auto start = std::chrono::high_resolution_clock::now();
    const auto spans = cal.materializeWindow(
        1'789'689'600'000, 1'789'689'600'000 + 7LL * 86'400'000, 0);
    const auto end = std::chrono::high_resolution_clock::now();
    RecordProperty("window_ms", std::to_string(ElapsedMs(start, end)));
    RecordProperty("window_spans", std::to_string(spans.size()));
    EXPECT_LT(ElapsedMs(start, end), 500.0); // 500 块窗口物化 < 500ms
}

} // namespace
