#pragma once

#include <filesystem>
#include <string>
#include <vector>

#include <equora/storage/Database.h>

namespace equora::storage {

struct BackupInfo {
    std::filesystem::path file;
    std::string sha256Hex; // 来自伴生 .sha256 文件
    std::int64_t createdAtMs = 0;
};

// SQLite 一致性备份:sqlite3_backup 在线快照 + SHA-256 伴生校验文件。
// 禁止直接复制使用中的数据库文件;一切恢复路径都先保护现场再替换。
class BackupManager {
public:
    // 创建备份到 dir(不存在则创建)。文件名 equora-<UTC时间戳>.db。
    // 成功写伴生 .sha256;失败抛 StorageError,不留半成品。
    [[nodiscard]] static BackupInfo create(const Database& db,
                                           const std::filesystem::path& dir);

    // 校验备份:伴生 SHA-256 匹配 + 只读打开通过 quick_check。
    // 通过返回 true;不匹配/损坏返回 false 并给出原因(不抛异常)。
    [[nodiscard]] static bool verify(const std::filesystem::path& backupFile,
                                     std::string* whyNot = nullptr);

    // 列出目录下的备份(按时间降序);跳过无伴生校验文件的孤儿。
    [[nodiscard]] static std::vector<BackupInfo> list(const std::filesystem::path& dir);

    // 恢复:校验备份 → 先把当前库另存为恢复前快照 → 替换主库文件 → 重开连接。
    // 任一步失败抛 StorageError,且主库保持可用。
    static BackupInfo restore(Database& db, const std::filesystem::path& backupFile);

    // 保留策略:按文件名时间戳降序保留前 keepCount 个,删除其余(含伴生文件)。
    static void applyRetentionPolicy(const std::filesystem::path& dir, int keepCount);
};

} // namespace equora::storage
