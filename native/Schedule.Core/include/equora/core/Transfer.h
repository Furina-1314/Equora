#pragma once

#include <string>
#include <string_view>

#include <equora/core/TaskRepository.h>

namespace equora::core {

// JSON 导入结果:imported 为新插入条数;skipped 为已存在(按 id)或非法条目数。
struct ImportResult {
    int imported = 0;
    int skipped = 0;
};

// 导出全部未删除任务为 JSON 文本(UTF-8)。格式见 docs/data-model.md。
// 注意:导出文件本身不含项目/标签展开(P4 范围为任务字段)。
[[nodiscard]] std::string exportTasksJson(const TaskRepository& repo);

// 导出全部未删除任务为 CSV 文本(带 UTF-8 BOM,便于 Excel 直接打开中文)。
[[nodiscard]] std::string exportTasksCsv(const TaskRepository& repo);

// 从 JSON 文本导入任务:按 id 幂等(已存在跳过),保留原始时间戳与 revision。
// 整体格式非法抛 InvalidArgument;单条记录非法计入 skipped,不中断导入。
// 中途中断后再导入安全(已插入条目按 id 跳过)。
[[nodiscard]] ImportResult importTasksJson(const TaskRepository& repo,
                                           std::string_view jsonText);

} // namespace equora::core
