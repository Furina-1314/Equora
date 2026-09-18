#include <equora/storage/Database.h>

#include <sqlite3.h>

#include <equora/domain/Error.h>

namespace equora::storage {

namespace {

[[noreturn]] void throwSqlite(sqlite3* db, int rc, const char* context) {
    throw domain::EquoraError(
        domain::ErrorCode::StorageError,
        std::string("sqlite error ") + std::to_string(rc) + " (" + context + "): " +
            (db != nullptr ? sqlite3_errmsg(db) : "no connection"));
}

int execRaw(sqlite3* db, const char* sql) {
    return sqlite3_exec(db, sql, nullptr, nullptr, nullptr);
}

// PRAGMA 在打开时失败视为不可用连接,直接抛出。
void execOrThrow(sqlite3* db, const char* sql) {
    const int rc = execRaw(db, sql);
    if (rc != SQLITE_OK) throwSqlite(db, rc, sql);
}

} // namespace

// ---------- Database ----------

Database::Database(sqlite3* db, std::filesystem::path path)
    : db_(db), path_(std::move(path)) {}

Database::~Database() {
    close();
}

Database::Database(Database&& other) noexcept
    : db_(other.db_), path_(std::move(other.path_)) {
    other.db_ = nullptr;
    other.path_.clear();
}

Database& Database::operator=(Database&& other) noexcept {
    if (this != &other) {
        close();
        db_ = other.db_;
        path_ = std::move(other.path_);
        other.db_ = nullptr;
        other.path_.clear();
    }
    return *this;
}

Database Database::open(const std::filesystem::path& file) {
    sqlite3* raw = nullptr;
    const int rc = sqlite3_open_v2(
        file.string().c_str(), &raw,
        SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nullptr);
    if (rc != SQLITE_OK) {
        const std::string msg =
            raw != nullptr ? sqlite3_errmsg(raw) : "cannot open database file";
        if (raw != nullptr) sqlite3_close(raw);
        throw domain::EquoraError(domain::ErrorCode::StorageError,
                                  "open '" + file.string() + "' failed: " + msg);
    }

    // 连接级设置:崩溃安全与并发等待。WAL 对 :memory: 库自动退化为 MEMORY。
    execOrThrow(raw, "PRAGMA journal_mode=WAL;");
    execOrThrow(raw, "PRAGMA synchronous=NORMAL;");
    execOrThrow(raw, "PRAGMA foreign_keys=ON;");
    sqlite3_busy_timeout(raw, 5000);

    return Database(raw, file);
}

Statement Database::prepare(std::string_view sql) const {
    sqlite3_stmt* st = nullptr;
    const int rc = sqlite3_prepare_v2(db_, sql.data(), static_cast<int>(sql.size()), &st, nullptr);
    if (rc != SQLITE_OK) {
        if (st != nullptr) sqlite3_finalize(st);
        throwSqlite(db_, rc, "prepare");
    }
    return Statement(st);
}

void Database::exec(std::string_view sql) const {
    const int rc = execRaw(db_, sql.data());
    if (rc != SQLITE_OK) throwSqlite(db_, rc, "exec");
}

Transaction Database::beginTransaction() const {
    return Transaction(db_);
}

std::int64_t Database::lastInsertRowId() const {
    return sqlite3_last_insert_rowid(db_);
}

std::int64_t Database::changes() const {
    return sqlite3_changes64(db_);
}

void Database::close() noexcept {
    if (db_ != nullptr) {
        sqlite3_close_v2(db_);
        db_ = nullptr;
    }
    path_.clear();
}

// ---------- Statement ----------

Statement::Statement(sqlite3_stmt* st) : st_(st) {}

Statement::~Statement() {
    if (st_ != nullptr) sqlite3_finalize(st_);
}

Statement::Statement(Statement&& other) noexcept : st_(other.st_) {
    other.st_ = nullptr;
}

Statement& Statement::operator=(Statement&& other) noexcept {
    if (this != &other) {
        if (st_ != nullptr) sqlite3_finalize(st_);
        st_ = other.st_;
        other.st_ = nullptr;
    }
    return *this;
}

Statement& Statement::bind(int index, std::nullptr_t) {
    const int rc = sqlite3_bind_null(st_, index);
    if (rc != SQLITE_OK) throwSqlite(sqlite3_db_handle(st_), rc, "bind null");
    return *this;
}

Statement& Statement::bind(int index, std::int32_t value) {
    return bind(index, static_cast<std::int64_t>(value));
}

Statement& Statement::bind(int index, std::uint32_t value) {
    return bind(index, static_cast<std::int64_t>(value));
}

Statement& Statement::bind(int index, std::int64_t value) {
    const int rc = sqlite3_bind_int64(st_, index, value);
    if (rc != SQLITE_OK) throwSqlite(sqlite3_db_handle(st_), rc, "bind int");
    return *this;
}

Statement& Statement::bind(int index, double value) {
    const int rc = sqlite3_bind_double(st_, index, value);
    if (rc != SQLITE_OK) throwSqlite(sqlite3_db_handle(st_), rc, "bind double");
    return *this;
}

Statement& Statement::bind(int index, std::string_view utf8Text) {
    // SQLITE_TRANSIENT:sqlite 立即复制,调用方字符串可提前释放。
    const int rc = sqlite3_bind_text(st_, index, utf8Text.data(),
                                     static_cast<int>(utf8Text.size()), SQLITE_TRANSIENT);
    if (rc != SQLITE_OK) throwSqlite(sqlite3_db_handle(st_), rc, "bind text");
    return *this;
}

bool Statement::step() {
    const int rc = sqlite3_step(st_);
    if (rc == SQLITE_ROW) return true;
    if (rc == SQLITE_DONE) return false;
    throwSqlite(sqlite3_db_handle(st_), rc, "step");
}

void Statement::reset() {
    sqlite3_reset(st_);
    sqlite3_clear_bindings(st_);
}

bool Statement::isNull(int column) const {
    return sqlite3_column_type(st_, column) == SQLITE_NULL;
}

std::int64_t Statement::columnInt(int column) const {
    return sqlite3_column_int64(st_, column);
}

double Statement::columnDouble(int column) const {
    return sqlite3_column_double(st_, column);
}

std::string Statement::columnText(int column) const {
    const unsigned char* text = sqlite3_column_text(st_, column);
    if (text == nullptr) return {};
    return std::string(reinterpret_cast<const char*>(text),
                       static_cast<std::size_t>(sqlite3_column_bytes(st_, column)));
}

int Statement::columnCount() const {
    return sqlite3_column_count(st_);
}

// ---------- Transaction ----------

Transaction::Transaction(sqlite3* db) : db_(db) {
    const int rc = execRaw(db_, "BEGIN IMMEDIATE;");
    if (rc != SQLITE_OK) {
        db_ = nullptr;
        throwSqlite(db, rc, "BEGIN IMMEDIATE");
    }
}

Transaction::~Transaction() {
    if (db_ != nullptr) execRaw(db_, "ROLLBACK;");
}

Transaction::Transaction(Transaction&& other) noexcept : db_(other.db_) {
    other.db_ = nullptr;
}

Transaction& Transaction::operator=(Transaction&& other) noexcept {
    if (this != &other) {
        if (db_ != nullptr) execRaw(db_, "ROLLBACK;");
        db_ = other.db_;
        other.db_ = nullptr;
    }
    return *this;
}

void Transaction::commit() {
    if (db_ == nullptr) return;
    sqlite3* db = db_;
    db_ = nullptr;
    const int rc = execRaw(db, "COMMIT;");
    if (rc != SQLITE_OK) throwSqlite(db, rc, "COMMIT");
}

void Transaction::rollback() {
    if (db_ == nullptr) return;
    execRaw(db_, "ROLLBACK;");
    db_ = nullptr;
}

} // namespace equora::storage
