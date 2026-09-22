#pragma once

#include <optional>
#include <string>
#include <vector>

#include <equora/core/TaskQuery.h>
#include <equora/domain/Task.h>
#include <equora/storage/Database.h>

namespace equora::core {

// 任务的持久化用例:创建、读取、乐观并发更新、软删除与列表。
// 仓库不拥有 Database 连接(由更上层管理连接生命周期与迁移时机)。
class TaskRepository {
public:
    TaskRepository(const storage::Database& db, std::string deviceId);

    // 创建任务:draft.id 为空时生成 UUID;填充 createdAt/updatedAt/revision=1。
    // 标题为空或全空白时抛 InvalidArgument;显式提供的 id 必须是合法 UUID。
    domain::Task create(domain::Task draft) const;

    // 按 id 查找;includeDeleted=false 时软删除任务视为不存在。
    [[nodiscard]] std::optional<domain::Task> findById(const std::string& id,
                                                       bool includeDeleted = false) const;

    // 乐观并发更新:task.revision 必须等于库中当前值,否则抛 Conflict。
    // 成功后 revision+1、updatedAt 刷新、lastDeviceId 更新,并返回更新后的实体。
    domain::Task update(domain::Task task, bool ownTransaction = true) const;

    // 软删除/恢复(墓碑语义);返回最新实体。
    domain::Task setDeleted(const std::string& id, bool deleted, bool ownTransaction = true) const;
    void permanentlyDelete(const std::string& id) const;

    // 列出任务(默认排除软删除,按创建时间升序)。
    [[nodiscard]] std::vector<domain::Task> listAll(bool includeDeleted = false) const;

    // 按过滤条件查询:智能清单/搜索/分页的统一入口。
    [[nodiscard]] std::vector<domain::Task> query(const TaskFilter& filter) const;

    // 导入用插入:保留调用方提供的 id/时间戳/revision(校验合法后原样落库)。
    // 幂等性由调用方先查 id 是否存在;重复 id 触发唯一约束 -> Conflict。
    [[nodiscard]] domain::Task importTask(domain::Task preserved) const;

private:
    [[nodiscard]] static domain::Task rowToTask(const storage::Statement& st);

    const storage::Database& db_;
    std::string deviceId_;
};

} // namespace equora::core
