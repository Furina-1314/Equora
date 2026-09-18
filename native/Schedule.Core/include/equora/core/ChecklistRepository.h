#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/domain/ChecklistItem.h>
#include <equora/storage/Database.h>

namespace equora::core {

// 任务检查项仓库:软删除;sortOrder 在任务内从 1 递增。
class ChecklistRepository {
public:
    ChecklistRepository(const storage::Database& db, std::string deviceId);

    // 添加检查项:任务必须存在且未软删除;追加到列表末尾。
    [[nodiscard]] domain::ChecklistItem add(const std::string& taskId,
                                            std::string content) const;

    [[nodiscard]] std::optional<domain::ChecklistItem> findById(const std::string& id) const;

    // 任务的活动检查项,按 sortOrder 升序。
    [[nodiscard]] std::vector<domain::ChecklistItem> listForTask(
        const std::string& taskId) const;

    // 乐观并发更新(内容/勾选/排序)。
    [[nodiscard]] domain::ChecklistItem update(domain::ChecklistItem item) const;

    // 软删除;幂等。
    void remove(const std::string& id) const;

    // 按给定顺序重排任务的活动检查项(1..n 重编号,每项 revision+1)。
    // orderedIds 必须与任务当前活动检查项集合完全一致,否则 InvalidArgument。
    void reorder(const std::string& taskId, const std::vector<std::string>& orderedIds) const;

private:
    [[nodiscard]] static domain::ChecklistItem rowToItem(const storage::Statement& st);

    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
