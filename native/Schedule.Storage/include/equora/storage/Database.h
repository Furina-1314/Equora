#pragma once

#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <string_view>

struct sqlite3;
struct sqlite3_stmt;

namespace equora::storage {

class Statement;
class Transaction;

// SQLite 连接的 RAII 封装。
// 打开时启用:WAL 日志、外键约束、busy_timeout、synchronous=NORMAL。
class Database {
public:
    Database() = default;
    ~Database();

    Database(const Database&) = delete;
    Database& operator=(const Database&) = delete;
    Database(Database&& other) noexcept;
    Database& operator=(Database&& other) noexcept;

    // 打开(必要时创建)数据库文件;file 可为 ":memory:"。
    // 打开失败(路径不可写、文件损坏非库等)抛 StorageError。
    [[nodiscard]] static Database open(const std::filesystem::path& file);

    [[nodiscard]] Statement prepare(std::string_view sql) const;
    void exec(std::string_view sql) const; // 仅用于无参数 SQL(迁移、PRAGMA)

    [[nodiscard]] Transaction beginTransaction() const; // BEGIN IMMEDIATE

    [[nodiscard]] std::int64_t lastInsertRowId() const;
    [[nodiscard]] std::int64_t changes() const;
    [[nodiscard]] const std::filesystem::path& path() const noexcept { return path_; }
    [[nodiscard]] bool is_open() const noexcept { return db_ != nullptr; }
    [[nodiscard]] sqlite3* handle() const noexcept { return db_; }

    void close() noexcept;

private:
    Database(sqlite3* db, std::filesystem::path path);

    sqlite3* db_ = nullptr;
    std::filesystem::path path_;
};

// sqlite3_stmt 的 RAII 封装,移动专用。绑定下标从 1 开始。
class Statement {
public:
    Statement() = default;
    ~Statement();

    Statement(const Statement&) = delete;
    Statement& operator=(const Statement&) = delete;
    Statement(Statement&& other) noexcept;
    Statement& operator=(Statement&& other) noexcept;

    Statement& bind(int index, std::nullptr_t);
    Statement& bind(int index, std::int32_t value);
    Statement& bind(int index, std::uint32_t value);
    Statement& bind(int index, std::int64_t value);
    Statement& bind(int index, double value);
    Statement& bind(int index, std::string_view utf8Text);

    // 可空列:空 optional 绑定为 SQL NULL。
    template <typename T>
    Statement& bind(int index, std::optional<T> value) {
        return value.has_value() ? bind(index, *value) : bind(index, nullptr);
    }

    // 前进一步;返回 true 表示有数据行可读,false 表示语句完成。
    // 任何 SQLite 错误抛 StorageError。
    bool step();

    void reset(); // 复用语句(清除绑定)

    [[nodiscard]] bool isNull(int column) const;
    [[nodiscard]] std::int64_t columnInt(int column) const;
    [[nodiscard]] double columnDouble(int column) const;
    [[nodiscard]] std::string columnText(int column) const;
    [[nodiscard]] int columnCount() const;

    [[nodiscard]] bool is_valid() const noexcept { return st_ != nullptr; }

private:
    friend class Database;
    explicit Statement(sqlite3_stmt* st);

    sqlite3_stmt* st_ = nullptr;
};

// 事务 RAII:构造执行 BEGIN,显式 commit();析构时若未提交则 ROLLBACK。
// 提交前必须让所有相关 Statement 离开作用域(或 reset)。
class Transaction {
public:
    Transaction() = default;
    ~Transaction();

    Transaction(const Transaction&) = delete;
    Transaction& operator=(const Transaction&) = delete;
    Transaction(Transaction&& other) noexcept;
    Transaction& operator=(Transaction&& other) noexcept;

    void commit();    // 提交;失败抛 StorageError
    void rollback();  // 显式回滚,幂等

    [[nodiscard]] bool active() const noexcept { return db_ != nullptr; }

private:
    friend class Database;
    Transaction(sqlite3* db);

    sqlite3* db_ = nullptr;
};

} // namespace equora::storage
