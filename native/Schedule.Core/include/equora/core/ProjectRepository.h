#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Project.h>
#include <equora/storage/Database.h>

namespace equora::core {

class ProjectRepository {
public:
    ProjectRepository(const storage::Database& db, std::string deviceId);

    // 创建项目:名称非空白;id 自动生成。
    [[nodiscard]] domain::Project create(domain::Project draft) const;

    [[nodiscard]] std::optional<domain::Project> findById(const std::string& id,
                                                          bool includeDeleted = false) const;

    // 乐观并发更新:revision 必须等于库中当前值,否则 Conflict。
    [[nodiscard]] domain::Project update(domain::Project project) const;

    // 归档/取消归档(设置 archived_at);返回最新实体,幂等。
    [[nodiscard]] domain::Project setArchived(const std::string& id, bool archived) const;

    // 软删除/恢复。
    [[nodiscard]] domain::Project setDeleted(const std::string& id, bool deleted) const;

    // 列出项目(默认含归档、排除软删除,按名称序)。
    [[nodiscard]] std::vector<domain::Project> list(bool includeArchived = true,
                                                    bool includeDeleted = false) const;

private:
    [[nodiscard]] static domain::Project rowToProject(const storage::Statement& st);

    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
