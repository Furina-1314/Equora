# Equora 开发阶段规划(Phase Plan)

本文档将需求文档(见 `docs/requirements.md`)中的里程碑 M0–M10 细化为可独立交付的开发阶段。
每个 Phase 以「可运行成果 + Git 提交」为断点:代码可编译、测试可通过、不引入占位实现。
进度状态同步记录在 `docs/progress.md`。

**状态图例**:✅ 已完成 · 🔨 进行中 · ⬜ 未开始

| Phase | 内容 | 对应里程碑 | 状态 |
|---|---|---|---|
| P0 | 仓库初始化:GitHub 仓库、Git、README、LICENSE、.gitignore、阶段规划与进度文档 | M0 前置 | ✅ |
| P1 | C++ 原生核心骨架:CMake 超级构建、vcpkg 清单、Schedule.Domain 基础类型(UUID/UTC 时间/错误码)、Schedule.Storage(SQLite RAII 封装、版本化迁移框架、事务)、GoogleTest 单元测试 | M0 | 🔨 |
| P2 | Schedule.CApi 稳定 C ABI(不透明句柄、UTF-8、错误对象、显式释放)+ 架构文档 architecture.md、数据模型文档 data-model.md | M0 | 🔨 |
| P3 | WinUI 3 应用外壳:Schedule.App / ViewModels / Services / NativeInterop 四工程,窗口、导航、深浅色主题、P/Invoke 打通 C++ 测试接口 | M0 | 🔨 |
| P4 | 单机任务核心:Task/Project/Tag/ChecklistItem 存储与仓库、软删除、修订号、智能清单查询(今天/近期/逾期/已完成)、一致性备份 | M1 | ✅ |
| P5 | 任务管理界面:三栏布局、任务列表与详情栏、搜索、撤销栈、JSON/CSV 导入导出;M1 验收 | M1 | ✅ |
| P6 | 日历与时间块核心:TimeBlock/Event 存储、重复规则(RRULE 子集)展开与例外、冲突检测、空闲搜索、ICS 导入导出 | M2 | ⬜ |
| P7 | 日历界面:自定义高性能周视图(虚拟化)、拖拽创建/移动/缩放、当前时间线、工作时间与过载警告;M2 验收 | M2 | ⬜ |
| P8 | 四象限与快速收集:矩阵视图、今日三件要事、全局快捷输入窗口、自然语言日期解析(可预览可编辑)、命令面板;M3 验收 | M3 | ⬜ |
| P9 | 专注会话核心:计时状态机(番茄钟/深度/Flowtime/正计时)、FocusSession 持久化与崩溃恢复、分心捕获箱、休息提醒 | M4 | ✅ |
| P10 | 专注执行界面与温和限制:专注界面、迷你置顶窗、Focus Profile、前台应用检测、温和提醒、本地活动统计与隐私开关;M4 验收 | M4 | ⬜ |
| P11 | 网站限制:MV3 扩展、declarativeNetRequest 动态/会话规则、白名单与预算、Native Messaging 带版本协议、异常恢复;M5 验收 | M5 | ✅ |
| P12 | 智能规划与复盘:确定性排程引擎、排程差异预览与一次性撤销、估时校正、任务拆分建议、每日/每周复盘、自动化规则与执行日志;M6 验收 | M6 | ✅ |
| P13 | 本机同步服务器:Drogon REST API、PostgreSQL 迁移、账号/设备/令牌、Push/Pull/游标/墓碑、Docker 编排 | M7 | ⬜ |
| P14 | 同步客户端与冲突处理:Outbox 同事务写入、指数退避、字段级合并、冲突中心、双客户端离线/并发测试;M7–M8 验收 | M7–M8 | ⬜ |
| P15 | Windows 深度集成与严格限制:通知操作、小组件、开机启动、可选 Windows Service + 命名管道、严格应用限制的安全白名单与紧急解锁;M9 验收 | M9 | ⬜ |
| P16 | 发布候选:性能与内存优化、无障碍与多 DPI、MSIX 安装升级、备份/恢复演练、故障注入测试、用户指南与隐私说明、签名发布;M10 验收 | M10 | ⬜ |

## 阶段纪律

1. 每个 Phase 开始前,先在 `docs/progress.md` 登记目标、依赖与验收条件。
2. 每个 Phase 完成后:运行构建与测试并记录真实结果 → 更新 `docs/progress.md` → Git 提交(消息前缀 `P<n>:`)。
3. 不伪造运行结果;未验证的功能不声称完成。
4. 破坏性变更必须提供数据迁移;数据库结构变更必须新增迁移脚本,禁止改旧迁移。
5. 遇到产品决策不明确时,选择最安全、可逆、符合本地优先原则的默认值,并记录假设。
