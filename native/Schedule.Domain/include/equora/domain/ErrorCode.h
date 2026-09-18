#pragma once

namespace equora::domain {

// 稳定错误码。0 表示成功;该枚举是对外 C ABI 的一部分,只能追加,不得改值。
enum class ErrorCode : int {
    Ok                = 0,
    Unknown           = 1,
    InvalidArgument   = 2,
    NotFound          = 3,
    Conflict          = 4,   // 修订号不匹配、并发覆盖等
    StorageError      = 5,   // SQLite 层错误
    MigrationError    = 6,   // 数据库迁移失败(已回滚)
    IoError           = 7,
    NotSupported      = 8,
};

[[nodiscard]] const char* toString(ErrorCode code) noexcept;

} // namespace equora::domain
