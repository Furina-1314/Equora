# Equora 数据模型

> 状态:P2(2026-09-18)。记录当前 SQLite 模式(v1)与既定的跨设备字段约定。
> 结构变更只能通过新增迁移,禁止修改历史迁移。

## 1. 全局约定

- **主键**:所有可同步实体使用 UUID v4 文本(小写 8-4-4-4-12),生成于创建端。
- **时间**:Unix 纪元 UTC 毫秒(`int64`)。全天事项的本地日期语义在 P6 引入
  (`due_is_date` 类列 + 用户时区快照),避免跨时区漂移。
- **可同步实体公共列**(P1 起落地在 tasks,后续实体沿用):

| 列 | 类型 | 语义 |
|---|---|---|
| `id` | TEXT PK | UUID |
| `created_at` | INTEGER | 创建时间(UTC ms) |
| `updated_at` | INTEGER | 最后修改时间(UTC ms) |
| `revision` | INTEGER | 单调递增;乐观并发与同步 `base_revision` 检查 |
| `deleted_at` | INTEGER NULL | 软删除墓碑;NULL=未删除 |
| `last_device_id` | TEXT | 最后修改设备 |

- **迁移框架**:`schema_migrations(version, name, applied_at)` 登记表;
  每个迁移单事务执行,失败整体回滚;版本必须严格递增。

## 2. 当前模式(v1)

### tasks — 任务

| 列 | 类型 | 约束/默认 | 说明 |
|---|---|---|---|
| `id` | TEXT | PK | UUID |
| `title` | TEXT | NOT NULL | 标题 |
| `note` | TEXT | NOT NULL DEFAULT '' | 备注 |
| `status` | INTEGER | NOT NULL DEFAULT 0 | 0 Inbox / 1 Planned / 2 InProgress / 3 Waiting / 4 Done / 5 Cancelled |
| `priority` | INTEGER | NOT NULL DEFAULT 2 | 0 None / 1 Low / 2 Normal / 3 High / 4 Urgent |
| `importance` | INTEGER | NOT NULL DEFAULT 0 | 0 未设置;1..5(四象限「重要」轴) |
| `due_at` | INTEGER | NULL | 截止时间(UTC ms) |
| `estimate_minutes` | INTEGER | NULL | 预计时长(分钟) |
| `actual_minutes` | INTEGER | NOT NULL DEFAULT 0 | 实际累计时长 |
| `project_id` | TEXT | NULL | → projects.id(P4 启用,暂存) |
| `created_at` / `updated_at` / `revision` / `deleted_at` / `last_device_id` | — | — | 公共同步列 |

索引:`idx_tasks_status(status)`;部分索引 `idx_tasks_active_due(due_at) WHERE deleted_at IS NULL`。

## 3. 计划中的实体(按阶段落地)

### P4:v2 迁移

- **projects**:id、名称、颜色、状态(active/archived)、目标、归档时间 + 公共列。
- **tags**:id、名称(唯一)、颜色 + 公共列。
- **task_tags**(task_id, tag_id) 联合主键。
- **checklist_items**:id、task_id、内容、勾选状态、排序 + 公共列。
- 智能清单查询(今天/近期/逾期/已安排/无日期/等待中/已完成)以 SQL 视图或参数化查询实现,不另建表。

### P6:v3 迁移

- **time_blocks**:id、task_id(可空,与 event 二选一)、开始/结束时间、
  计划时长、实际时长、缓冲(前/后)、日历归属 + 公共列。
- **events**:非任务日程/会议/全天事项。
- **recurrence_rules**:宿主类型(task/event)、RRULE 子集
  (FREQ=DAILY/WEEKLY/MONTHLY/YEARLY;BYDAY;每月第 N 个/最后一个工作日;
  完成后 N 天)、例外日期集(`excluded_dates`)。
- **calendars**:名称、颜色、来源(local/ics/caldav)、可见性。

### P9:v4 迁移

- **focus_sessions**:id、task_id、模式(番茄/深度/Flowtime/正计时/无计时)、
  计划开始/结束、实际开始/结束、暂停累计、状态机终态、目标与完成记录。
- **interruptions**:会话 id、时间、原因、来源、处理方式。
- **distraction_inbox_items**:即时写入(先持久化后整理),会话 id、内容、整理去向。
- **focus_profiles**:允许/限制应用与网站、通知策略、计时参数、强度等级。

### P12:v5 迁移

- **app_usage_records**:前台应用标识、分类、开始/结束、窗口切换计数(仅本地、可关闭)。
- **daily_reviews / weekly_reviews**:复盘快照与重点。
- **automation_rules**:触发器/条件/动作 JSON、启用状态;**automation_logs** 执行日志。

### P13+:同步启用

- **sync_outbox**:待上传操作(与业务写同事务产生)。
- **sync_state**:服务端游标、上次成功同步时间、账号与设备信息。

## 4. 服务端对应(后续,PostgreSQL)

- 桌面端实体在服务端镜像存储,附加有序变更流 `change_log(seq, entity, op, payload)`。
- 游标 = `change_log.seq`;删除以墓碑形式进入变更流。
- 冲突策略详见 `docs/sync-protocol.md`(P13 撰写)。

## 5. 数据完整性与恢复

- 写入一律事务;`revision` 不匹配 → 拒绝(Conflict),调用方重读再试。
- 崩溃恢复:WAL + `synchronous=NORMAL`;启动时 `eq_core_create` 自动补迁移。
- 备份(P4):`sqlite3_backup` 一致性快照 + SHA-256 校验 + 版本元数据;
  恢复前先备份当前库。绝不直接复制使用中的库文件。

## v1.0.0：数据库 v7 与学期历史

`time_blocks` 新增 `title TEXT NOT NULL DEFAULT ''` 和 `batch_id TEXT NOT NULL DEFAULT ''`，并为未删除条目的批次建立索引。旧记录保持原备注，两个新字段默认为空，不推测旧批次。标题参与日历呈现和 ICS 导出。

每次学期批量创建使用唯一批次 ID，各条时间段仍可独立修改。批量编辑/删除使用单个事务和 revision 检查，并可原子地同步关联任务标题或软删除关联任务。C ABI 升级至 v3。

`preferences.json` 保存当前 `Semester` 和 `ArchivedSemesters`，以学期 ID 区分历史。结束日期次日归档，保留原有时间段，等待设置新学期。
