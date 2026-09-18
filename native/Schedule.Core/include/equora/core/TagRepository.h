#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/domain/Tag.h>
#include <equora/storage/Database.h>

namespace equora::core {

// 标签仓库,同时承担任务-标签关系的维护。
// 名称唯一约束覆盖软删除标签(墓碑占用名称,防止离线重名冲突)。
class TagRepository {
public:
    TagRepository(const storage::Database& db, std::string deviceId);

    // 创建标签:名称非空白;重名(含已删除)抛 Conflict。
    [[nodiscard]] domain::Tag create(domain::Tag draft) const;

    [[nodiscard]] std::optional<domain::Tag> findById(const std::string& id,
                                                      bool includeDeleted = false) const;
    [[nodiscard]] std::optional<domain::Tag> findByName(const std::string& name,
                                                        bool includeDeleted = true) const;

    // 乐观并发更新(改名仍受唯一约束保护)。
    [[nodiscard]] domain::Tag update(domain::Tag tag) const;

    // 软删除/恢复;删除不清除 task_tags 关系(同步需要墓碑),查询侧过滤。
    [[nodiscard]] domain::Tag setDeleted(const std::string& id, bool deleted) const;

    [[nodiscard]] std::vector<domain::Tag> list(bool includeDeleted = false) const;

    // 为任务打标签/移除标签:双方必须存在且未软删除;已处于目标状态时幂等。
    void assignToTask(const std::string& taskId, const std::string& tagId) const;
    void unassignFromTask(const std::string& taskId, const std::string& tagId) const;

    // 任务的活动标签(排除已删除标签),按名称序。
    [[nodiscard]] std::vector<domain::Tag> forTask(const std::string& taskId) const;

private:
    [[nodiscard]] static domain::Tag rowToTag(const storage::Statement& st);

    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
