#pragma once

#include <optional>
#include <string_view>

#include <equora/storage/Database.h>

namespace equora::storage {

// 应用元数据表(app_meta)的键值访问。
// 用于持久化设备 ID、导出信息等小型配置;不存放用户任务数据。
[[nodiscard]] std::optional<std::string> getAppMeta(const Database& db, std::string_view key);
void setAppMeta(const Database& db, std::string_view key, std::string_view value);

// 取本机稳定设备 ID(UUID v4);不存在时生成并写入。同步阶段的设备撤销依赖它。
[[nodiscard]] std::string getOrCreateDeviceId(const Database& db);

} // namespace equora::storage
