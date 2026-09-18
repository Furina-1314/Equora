#include <equora/storage/Backup.h>

#include <algorithm>
#include <cstdio>
#include <fstream>
#include <sqlite3.h>

#include <equora/common/Logger.h>
#include <equora/common/Sha256.h>
#include <equora/domain/Error.h>
#include <equora/domain/Time.h>
#include <equora/domain/Uuid.h>

namespace equora::storage {

namespace {

using domain::ErrorCode;
using domain::EquoraError;
using domain::UtcMillis;
using domain::utc::now;

[[nodiscard]] std::filesystem::path sidecarFor(const std::filesystem::path& dbFile) {
    return std::filesystem::path(dbFile.string() + ".sha256");
}

[[nodiscard]] std::string timestampName(UtcMillis t) {
    // 自 ISO-8601 构造 "YYYYMMDDTHHMMSSZ" —— 字典序即时间序。
    const std::string iso = domain::utc::toIso8601(t); // 2026-09-18T12:34:56.789Z
    return iso.substr(0, 4) + iso.substr(5, 2) + iso.substr(8, 2) + "T" + iso.substr(11, 2) +
           iso.substr(14, 2) + iso.substr(17, 2) + "Z";
}

void writeSidecar(const std::filesystem::path& file, std::string_view shaHex) {
    std::ofstream out(sidecarFor(file), std::ios::binary | std::ios::trunc);
    out << shaHex << "\n";
}

// 只读打开并执行 quick_check,校验库结构可读。
[[nodiscard]] bool quickCheckOk(const std::filesystem::path& file, std::string& why) {
    sqlite3* raw = nullptr;
    if (sqlite3_open_v2(file.string().c_str(), &raw, SQLITE_OPEN_READONLY, nullptr) !=
        SQLITE_OK) {
        why = "cannot open backup read-only";
        if (raw != nullptr) sqlite3_close(raw);
        return false;
    }
    sqlite3_stmt* st = nullptr;
    bool ok = false;
    if (sqlite3_prepare_v2(raw, "PRAGMA quick_check(1)", -1, &st, nullptr) == SQLITE_OK &&
        sqlite3_step(st) == SQLITE_ROW) {
        const char* text = reinterpret_cast<const char*>(sqlite3_column_text(st, 0));
        ok = text != nullptr && std::string(text) == "ok";
        if (!ok) why = "quick_check: " + std::string(text != nullptr ? text : "?");
    } else {
        why = "quick_check failed to run";
    }
    if (st != nullptr) sqlite3_finalize(st);
    sqlite3_close(raw);
    return ok;
}

void copyFileOrThrow(const std::filesystem::path& from, const std::filesystem::path& to,
                     const char* context) {
    std::error_code ec;
    std::filesystem::copy_file(from, to, std::filesystem::copy_options::overwrite_existing, ec);
    if (ec) {
        throw EquoraError(ErrorCode::IoError, std::string(context) + ": copy '" +
                                                  from.string() + "' -> '" + to.string() +
                                                  "': " + ec.message());
    }
}

} // namespace

BackupInfo BackupManager::create(const Database& db, const std::filesystem::path& dir) {
    std::error_code ec;
    std::filesystem::create_directories(dir, ec); // 已存在不算错误

    BackupInfo info;
    info.createdAtMs = now();
    info.file = dir / ("equora-" + timestampName(info.createdAtMs) + "-" +
                       domain::Uuid::random().toString().substr(0, 6) + ".db");

    sqlite3* dest = nullptr;
    if (sqlite3_open(info.file.string().c_str(), &dest) != SQLITE_OK) {
        const std::string msg = dest != nullptr ? sqlite3_errmsg(dest) : "open failed";
        if (dest != nullptr) sqlite3_close(dest);
        std::filesystem::remove(info.file, ec);
        throw EquoraError(ErrorCode::IoError, "backup open dest: " + msg);
    }
    // 方向:sqlite3_backup_init(目标连接, "main", 源连接, "main") —— 源 → 目标。
    sqlite3_backup* backup = sqlite3_backup_init(dest, "main", db.handle(), "main");
    if (backup == nullptr) {
        const std::string msg = sqlite3_errmsg(dest);
        sqlite3_close(dest);
        std::filesystem::remove(info.file, ec);
        throw EquoraError(ErrorCode::StorageError, "backup_init: " + msg);
    }

    int rc = SQLITE_OK;
    do {
        rc = sqlite3_backup_step(backup, 128); // 每步 128 页,便于未来接入进度回调
    } while (rc == SQLITE_OK || rc == SQLITE_BUSY || rc == SQLITE_LOCKED);
    const int finishRc = sqlite3_backup_finish(backup);
    if (rc != SQLITE_DONE || finishRc != SQLITE_OK) {
        sqlite3_close(dest);
        std::filesystem::remove(info.file, ec);
        throw EquoraError(ErrorCode::StorageError,
                          "backup_step: rc=" + std::to_string(rc) +
                              " finish=" + std::to_string(finishRc));
    }
    sqlite3_close(dest);

    info.sha256Hex = common::Sha256::hexOfFile(info.file.string());
    if (info.sha256Hex.empty()) {
        std::filesystem::remove(info.file, ec);
        throw EquoraError(ErrorCode::IoError, "backup hashing failed: " + info.file.string());
    }
    writeSidecar(info.file, info.sha256Hex);

    common::logInfo("storage.backup", "created " + info.file.filename().string());
    return info;
}

bool BackupManager::verify(const std::filesystem::path& backupFile, std::string* whyNot) {
    std::string why;
    bool ok = false;
    do {
        if (!std::filesystem::exists(backupFile)) {
            why = "backup file missing";
            break;
        }
        std::ifstream side(sidecarFor(backupFile), std::ios::binary);
        if (!side) {
            why = "checksum sidecar missing";
            break;
        }
        std::string expected((std::istreambuf_iterator<char>(side)),
                             std::istreambuf_iterator<char>());
        while (!expected.empty() && (expected.back() == '\n' || expected.back() == '\r')) {
            expected.pop_back();
        }
        const std::string actual = common::Sha256::hexOfFile(backupFile.string());
        if (actual.empty()) {
            why = "cannot read backup for hashing";
            break;
        }
        if (actual != expected) {
            why = "checksum mismatch";
            break;
        }
        if (!quickCheckOk(backupFile, why)) break;
        ok = true;
    } while (false);

    if (!ok) {
        common::logWarn("storage.backup",
                        "verify failed for " + backupFile.filename().string() + ": " + why);
        if (whyNot != nullptr) *whyNot = why;
    }
    return ok;
}

std::vector<BackupInfo> BackupManager::list(const std::filesystem::path& dir) {
    std::vector<BackupInfo> out;
    std::error_code ec;
    if (!std::filesystem::exists(dir, ec)) return out;

    for (const auto& entry : std::filesystem::directory_iterator(dir, ec)) {
        if (ec) break;
        if (!entry.is_regular_file()) continue;
        const auto& path = entry.path();
        if (path.extension() != ".db") continue;
        const std::string name = path.filename().string();
        if (name.rfind("equora-", 0) != 0) continue;

        std::ifstream side(sidecarFor(path), std::ios::binary);
        if (!side) continue; // 无校验文件的备份不展示

        BackupInfo info;
        info.file = path;
        std::string sha((std::istreambuf_iterator<char>(side)),
                        std::istreambuf_iterator<char>());
        while (!sha.empty() && (sha.back() == '\n' || sha.back() == '\r')) sha.pop_back();
        info.sha256Hex = std::move(sha);
        info.createdAtMs = static_cast<std::int64_t>(
            std::filesystem::last_write_time(path, ec).time_since_epoch().count());
        out.push_back(std::move(info));
    }

    std::sort(out.begin(), out.end(),
              [](const BackupInfo& a, const BackupInfo& b) { return a.file > b.file; });
    return out;
}

BackupInfo BackupManager::restore(Database& db, const std::filesystem::path& backupFile) {
    std::string why;
    if (!verify(backupFile, &why)) {
        throw EquoraError(ErrorCode::IoError, "refuse to restore: " + why);
    }

    const std::filesystem::path mainPath = db.path();
    if (mainPath.empty()) {
        throw EquoraError(ErrorCode::InvalidArgument, "database has no file path");
    }

    // 保护现场:当前库另存为恢复前快照。
    db.close();
    std::error_code ec;
    const std::filesystem::path preRestore = std::filesystem::path(
        mainPath.string() + ".pre-restore-" + timestampName(now()) + ".db");
    if (std::filesystem::exists(mainPath, ec)) {
        copyFileOrThrow(mainPath, preRestore, "preserve current db");
    }
    for (const char* suffix : {"-wal", "-shm"}) {
        std::filesystem::remove(std::filesystem::path(mainPath.string() + suffix), ec);
    }

    try {
        copyFileOrThrow(backupFile, mainPath, "restore backup");
    } catch (...) {
        // 尽力恢复现场:用快照回填,重开连接后重新抛出。
        std::error_code ignored;
        if (std::filesystem::exists(preRestore, ignored)) {
            std::filesystem::copy_file(preRestore, mainPath,
                                       std::filesystem::copy_options::overwrite_existing,
                                       ignored);
        }
        db.reopen(mainPath);
        throw;
    }

    db.reopen(mainPath);
    common::logInfo("storage.backup", "restored from " + backupFile.filename().string());

    BackupInfo info;
    info.file = preRestore;
    info.createdAtMs = now();
    return info;
}

void BackupManager::applyRetentionPolicy(const std::filesystem::path& dir, int keepCount) {
    if (keepCount < 1) keepCount = 1;
    std::vector<BackupInfo> all = list(dir);
    std::error_code ec;
    for (std::size_t i = static_cast<std::size_t>(keepCount); i < all.size(); ++i) {
        std::filesystem::remove(all[i].file, ec);
        std::filesystem::remove(sidecarFor(all[i].file), ec);
    }
}

} // namespace equora::storage
