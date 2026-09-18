#pragma once

#include <cstdint>
#include <string_view>
#include <vector>

#include <equora/domain/Error.h>
#include <equora/storage/Database.h>

namespace equora::storage {

// 一次结构迁移:严格按 version 升序应用;statements 在同一事务内执行。
struct Migration {
    int version;
    std::string_view name;
    std::vector<std::string_view> statements;
};

// 迁移失败(单个迁移内任一语句失败)时整个迁移回滚并抛出。
class MigrationError : public domain::EquoraError {
public:
    MigrationError(int version, std::string_view name, std::string detail)
        : EquoraError(domain::ErrorCode::MigrationError,
                      "migration v" + std::to_string(version) + " '" +
                          std::string(name) + "' failed: " + std::move(detail)),
          version_(version) {}

    int version() const noexcept { return version_; }

private:
    int version_;
};

// 内置迁移列表(已应用的历史迁移只追加、不修改)。
[[nodiscard]] const std::vector<Migration>& builtInMigrations();

// 当前已应用到的最高版本;空库返回 0。
[[nodiscard]] int currentSchemaVersion(const Database& db);

// 按序应用 db 中尚未执行的迁移列表(内置列表的底层入口,亦供测试注入)。
void applyMigrations(const Database& db, const std::vector<Migration>& migrations);

// 按序应用 db 中尚未执行的内置迁移。
inline void applyMigrations(const Database& db) {
    applyMigrations(db, builtInMigrations());
}

} // namespace equora::storage
