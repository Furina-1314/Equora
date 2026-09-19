# Equora 开发进度

> 阶段划分见 [PHASES.md](PHASES.md)。每完成一个阶段在此登记:已完成、未完成、已知问题、下一步。
> 本文档只记录真实结果,不预先声称未验证的功能。

## 当前状态

- **当前阶段**:P12b — 智能规划界面与 ABI(M6 收尾)
- **已完成**:P0–P6(M2 核心与服务层完成)
- **最新提交**:P8 四象限与快速收集

---

## P3 — WinUI 3 应用外壳 ✅(2026-09-18)

### 已完成

- **解决方案** `desktop/Equora.slnx`(SDK 10 新格式),五工程:
  - `Equora.App`:WinUI 3 入口(WindowsAppSDK 1.8,net10.0-windows10.0.19041.0,
    非打包运行 + 自包含 WASDK;MSIX 留待 P16)。NavigationView 三页外壳
    (首页/任务/设置)、PerMonitorV2 清单、深浅色主题切换(设置页,默认跟随系统)、
    未处理异常兜底。
  - `Equora.App.NativeInterop`:`LibraryImport`(UTF-8)P/Invoke 封装,
    结构体布局与 C ABI 严格对应;`EquoraCore : IDisposable` 安全句柄;
    `NativeInputScope` 保证 UTF-8 缓冲跨原生调用存活;错误 → `EquoraException`。
  - `Equora.App.Services`:`AppPaths`(LocalAppData\Equora)、`AppDataService`
    (持有原生上下文,实现 `ITaskService`)。
  - `Equora.App.ViewModels`:`HomeViewModel`(CommunityToolkit.Mvvm 8.4,
    诊断/建任务/完成/删除命令)。
  - `Equora.App.Tests`:xunit 8 例。
- **首页冒烟界面**:运行自检(eq_ping/版本/schema/任务数)、真实任务增删改查列表
  —— UI → C# → C ABI → C++ → SQLite 纵向打通。
- **CI**:`desktop-ci.yml`(构建原生 CApi → dotnet build → dotnet test)。

### 修复的缺陷

- **C ABI v1 设计缺陷**:`eq_core_schema_version` 原返回值混淆「版本号」与「错误码」
  (版本 2 无法与 InvalidArgument=2 区分)。改为错误码返回 + `out_version` 出参,
  **ABI 版本提升为 v2**(未发布前的库内修复,规则照常执行)。

### 构建与测试结果(本机实测)

- `dotnet build Equora.slnx -c Release`:5 工程全部成功,0 警告 0 错误。
- 原生:`ctest` 41/41;C#:`dotnet test` 8/8(P/Invoke CRUD、UTF-8 中文往返、
  乐观并发 Conflict、空白标题 InvalidArgument、VM 自检)。

### 已知问题 / 假设

- 本会话为无头环境,未实际启动 UI 窗口;XAML 编译验证通过,
  首次真机运行若有布局问题在 P4 修正。
- 日志系统尚未建立(当前 Debug.WriteLine),P4 与备份一起补齐。
- 主题选择当前不持久化,P4 随配置系统落地。

### CI 修复记录(P3 收尾)

- job 级 `env:` 不能引用 `env` 上下文 → vcpkg 路径改为步骤内运行时解析。
- runner 预装 vcpkg 快照缺少锁定 baseline → 配置前 `git fetch + checkout + 重新 bootstrap`。
- **CI 崩溃根因**:测试输出目录缺 `sqlite3.dll`(vcpkg applocal 未递归复制
  equora_capi.dll 的间接依赖),runner 从 PATH 加载了 `C:\Program Files\Amazon\AWSCLIV2\sqlite3.dll`
  导致访问冲突。用 cdb 抓栈定位后,以 `$<TARGET_RUNTIME_DLLS>` 确定性复制全部
  运行时 DLL 修复。**native-ci 与 desktop-ci 现已全绿**(runner 镜像 windows-2025-vs2026,
  含 VS 2026 与 `Visual Studio 18 2026` 生成器,与本地一致)。

### 下一步

- P4(M1):Project/Tag/ChecklistItem 存储与迁移 v2、智能清单查询、
  一致性备份 + 校验、日志、JSON/CSV 导出;C# 服务层同步扩展。

## P2 — 稳定 C ABI 互操作层 ✅(2026-09-18)

### 已完成

- **Schedule.CApi**(`equora_capi.dll`,capi v1):
  - 头文件 `equora_capi.h`:不透明句柄(EqCore/EqTaskHandle/EqTaskList)、
    固定布局 DTO(EqError/EqTaskInput/EqTaskView)、UTF-8 字符串、显式 destroy。
  - 全部导出函数经 `guard` 边界捕获异常 → 错误码 + EqError;NULL 参数安全;
    批量接口 `eq_task_list_all` 避免逐条跨 ABI。
  - `eq_core_create` 打开数据库并自动应用迁移;`eq_ping` 冒烟接口。
- **Schedule.CApi.Tests**:12 例(版本/冒烟、NULL 安全、句柄生命周期、
  创建/读取/更新/软删除、乐观并发 Conflict、批量列表、中文 UTF-8 往返、
  空参数拒绝、错误消息填充)。
- **文档**:`docs/architecture.md`(分层、依赖方向、ABI 规则、线程/错误/安全策略)、
  `docs/data-model.md`(公共同步列约定、v1 模式、P4–P13 实体路线、备份恢复策略)。

### 构建与测试结果(本机实测)

- 构建零错误零警告;`ctest`:41/41 通过(29 core + 12 capi,1.09s)。
- 已知 MSVC 约束:含指针返回类型的导出函数必须把 `EQUORA_API` 放行首
  (`__declspec` 不允许出现在指针返回类型之后),已在头文件统一。

### 已知问题 / 假设

- DLL 依赖 vcpkg 动态 sqlite3.dll;桌面端集成时统一布局分发(P3 处理)。
- `eq_task_get` 找不到任务时置空 `*out_handle` 并返回 NotFound——调用方必须检查返回码。

### 下一步

- P3:WinUI 3 外壳(Schedule.App/ViewModels/Services/NativeInterop),
  通过 P/Invoke 调用 `eq_ping` 与任务 CRUD,M0 验收。

## P1 — C++ 原生核心骨架 ✅(2026-09-18)

### 已完成

- **构建体系**:`native/CMakeLists.txt` 超级构建(模块依赖方向 Domain ← Storage ← Core);
  `CMakePresets.json`(win-x64-release/debug);vcpkg 清单 `native/vcpkg.json`
  (sqlite3 3.53.4、gtest 1.18.0,baseline `e6f9e70a` 锁定版本);`.clang-format` 与 `.editorconfig`。
- **Schedule.Domain**:`Uuid`(RFC 4122 v4,解析/格式化/nil)、`Time`(UtcMillis、ISO-8601
  解析/格式化、Hinnant 儒略日算法、闰年/月天数)、`ErrorCode` 稳定错误码、`Task` 实体
  (同步就绪:id/revision/deleted_at/last_device_id)。
- **Schedule.Storage**:`Database`(sqlite3 RAII,WAL/外键/busy_timeout)、`Statement`
  (含 optional 绑定)、`Transaction`(BEGIN IMMEDIATE + 异常安全回滚)、
  版本化迁移框架(`schema_migrations` 登记表、单迁移事务、失败回滚、乱序拒绝)、迁移 v1(tasks 表)。
- **Schedule.Core**:`TaskRepository`(创建/读取/乐观并发更新/软删除/列表,Conflict 检测)。
- **测试**:GoogleTest 29 例(UUID 布局与唯一性、时间往返与非法输入、迁移幂等与回滚、
  仓库 CRUD/并发/墓碑),`ctest --preset win-x64-release` 100% 通过,零编译警告(/W4)。
- **CI**:`.github/workflows/native-ci.yml`(windows-latest,MSVC + vcpkg 清单,configure/build/test)。
- README 构建命令修正为 preset 用法。

### 构建与测试结果(本机实测)

- 工具链:VS 2026 Insiders MSVC 19.51.36257(VS 18 2026 生成器),Windows SDK 10.0.26100/10.0.28000(F:\Windows Kits)。
- `cmake --build --preset win-x64-release`:0 error / 0 warning。
- `ctest --preset win-x64-release`:29/29 通过(0.74s)。

### 已知问题 / 假设

- 本机 vcpkg 位于 `E:\code\vcpkg`(通过 `VCPKG_ROOT` 注入 preset),文档已说明。
- CI 中 `Visual Studio 18 2026` 生成器依赖 runner 镜像提供 VS2026;若 runner 仍为 VS2022,
  需为 CI 增加 VS17 preset(待推送后根据 CI 结果处理)。
- `parseIso8601` 暂不支持 `+HH:MM` 偏移(仅 Z);本地时区转换属展示层职责,后续阶段引入。
- vcpkg 内置下载器经本机代理偶发 SSL 失败;已预置 pwsh/ninja/7zip 到工具缓存,后续新增依赖时如遇同类问题按同样方式预置。

### 下一步

- P2:Schedule.CApi 稳定 C ABI(不透明句柄、UTF-8、错误对象、显式释放)+ architecture.md、data-model.md。

## P0 — 仓库初始化 ✅(2026-09-18)

### 已完成

- 创建 GitHub 公共仓库 [Furina-1314/Equora](https://github.com/Furina-1314/Equora)。
- `git init`(main 分支)+ 远程 origin(SSH)。
- `.gitignore`(VS / CMake / vcpkg / NuGet / Node / 测试产物 / 本地数据库与密钥)。
- `README.md`(项目简介、技术栈、结构、构建说明)。
- `LICENSE`(MIT)。
- `docs/PHASES.md`(P0–P16 阶段规划,映射里程碑 M0–M10)。
- `docs/progress.md`(本文档)。
- `docs/requirements.md`(原始需求文档存档)。

### 已知问题 / 假设

- 许可证需求未在需求文档中指定,默认选择 MIT(宽松、兼容后续第三方依赖)。

### 下一步

- P1:CMake 超级构建 + vcpkg 清单 + Domain 基础类型 + SQLite 迁移框架 + GoogleTest。

---

## P4 — 单机任务核心(M1)✅(2026-09-18)

### 已完成(P4a 原生层 + P4b 互操作/服务层)

- **Schedule.Common**:SHA-256(FIPS 180-4,流式;修复过一处 finish 填充死循环缺陷)、
  文件日志(级别过滤、按大小轮转、未初始化安全)。修复过程中发现并解决:`finish()`
  先置位 `finished_` 再调 `update()` 导致填充永不写入 → 重写为直接缓冲操作。
- **迁移 v2**:app_meta(键值)、projects、tags(名称 NOCASE 唯一含墓碑)、
  task_tags(联合主键 + 级联)、checklist_items、tasks.project_id 部分索引;
  v1→v2 升级保留旧数据(MigrationV2Tests 验证)。
- **仓库**:ProjectRepository(归档/软删除/乐观并发)、TagRepository(唯一名/关系维护/
  任务标签视图,修复 JOIN 列名歧义)、ChecklistRepository(追加排序/勾选/重排)、
  TaskRepository 新增 `query(TaskFilter)`(due/updated 区间、项目、标签 EXISTS、
  子串搜索 instr+lower、状态 IN/NOT IN、6 种排序、分页)与 `importTask`(保留字段幂等导入)。
- **SmartLists**:收件箱/今天/近期/逾期/无日期/已安排/等待/已完成/当日完成 ——
  语义在核心层,日界由调用方传 UTC 偏移(本地日界换算 `localDayIndex/localDayStartUtc`)。
- **备份**:sqlite3_backup 在线快照 + SHA-256 伴生校验 + quick_check + 恢复前自动保护现场
  + 保留策略;`Database::reopen` 支持底层文件替换。
- **导入导出**:JSON(nlohmann-json,幂等导入、单条非法跳过)与 CSV(BOM、引号转义)。
- **C ABI(v2 追加,无破坏)**:查询 EqTaskFilter、项目/标签/检查项全套句柄、
  EqStringHandle、设备 ID(app_meta 持久化)、日志初始化、备份/导出/导入;
  实现拆分 CApi/CApiEntities/CApiSystem + 共享 CApiInternal.h(不透明类型必须在全局作用域补全)。
- **C#**:NativeInterop 新增结构与方法(含 QueryScope 过滤器封送与原生缓冲作用域)、
  IWorkspaceService + AppDataService 实现、App 启动接原生日志。
- **AppPaths.EnsureCreated** 建 logs/backups 目录。

### 构建与测试结果(本机实测)

- 原生:`ctest` 90/90(84 core + 6 capi;新增 43+6 例);/W4 零警告。
- C#:`dotnet test` 16/16;`dotnet build` 5 工程 0 警告。

### 修复的缺陷

- SHA-256 finish 死循环(见上);TagRepository.forTask JOIN `created_at` 列名歧义;
  C ABI 不透明类型命名空间二义性(定义必须补全全局声明);
  一处测试期发现的真实语义问题:「今天中午截止」的任务在午后确实属于逾期,
  实现正确、测试假设错误(改为边界无关断言)。

### 已知问题 / 假设

- JSON 导入仅覆盖任务字段(项目/标签展开导出留待 P5+);CSV 为单向导出。
- EqTaskFilter 的 due/updated 边界以 0 表示「未设置」(纪元 0 不会出现在真实过滤中)。
- 智能清单的「已委派」清单尚无数据字段支撑(委派字段在 P6+ 引入)。

### 下一步

- P5(M1 收尾):三栏布局任务界面、智能清单侧栏、详情编辑(状态/优先级/截止/标签/
  检查项)、搜索框、简单撤销栈、备份/导入导出入口;M1 验收(长期真实使用、
  异常退出数据完整、迁移可升级回滚——迁移部分已具备)。

---

## P5 — 任务管理界面(M1 收尾)✅(2026-09-19)

### 已完成

- **三栏布局**(TasksPage):左栏智能清单(收集箱/今天/近期 7 天/已逾期/无日期/
  等待中/已完成/全部/回收站)+ 项目 + 标签;中栏搜索框(回车/按钮)+ 新建 +
  撤销 + 任务列表(完成勾选、优先级色条、截止时间、删除/恢复);
  右栏 TaskDetailPanel 详情编辑。
- **TaskDetailViewModel**:标题/备注/状态/优先级即时保存(每次变更推进 revision
  并压入撤销栈)、截止时间(日期+时间+清除)、预计时长、标签勾选、检查项增删改。
- **UndoService**(IUndoService):100 步撤销栈;新建/删除/完成/字段编辑全部可撤销;
  Ctrl+Z 由中栏撤销按钮承接(全局快捷键在 P8 命令面板阶段统一)。
- **回收站**:IncludeDeleted 查询 + 客户端过滤仅显示已删除条目,支持恢复。
- **设置页数据区**:立即备份(含校验回执)、打开数据目录(显示模式版本与设备 ID)、
  导出 JSON/CSV(FileSavePicker)、导入 JSON(FileOpenPicker,幂等)。
- **转换器**:状态勾选/截止文本/优先级色条/布尔可见性等 7 个 IValueConverter
  (App.xaml 注册);TaskDto 改为可变属性以支持编辑流。
- **启动落点**:主窗口默认打开任务页。
- ITaskService 扩展 QueryTasks 与 GetTask(includeDeleted)。

### 构建与测试结果(本机实测)

- `dotnet build` 5 工程成功(XAML 编译通过);`dotnet test` 27/27(新增 11 例:
  侧栏构建、今天清单、搜索过滤、新建+撤销、删除撤销、完成往返、回收站恢复、
  详情编辑持久化+撤销、检查项与标签、截止生命周期、项目/标签侧栏过滤)。

### 修复的缺陷

- 回收站查询把未删除条目也返回(IncludeDeleted 语义是"包含"而非"仅"),
  在视图模型层过滤修正;原生 only_deleted 过滤器列入 P6。

### 已知问题 / 假设

- 无头环境未实际渲染窗口;XAML 编译 + VM 单测覆盖,真机首启若有布局细节问题
  在 P6 顺手修正。
- 撤销为会话内(Ctrl+Z 按钮);全局快捷键与重做栈随 P8 命令面板一起做。
- 标题/备注编辑每次击字符即保存(M1 可接受;防抖在 P6 视图打磨时处理)。
- ICS 导入导出未包含(M1 验收以 JSON/CSV 基础能力为准,ICS 归入 M2/P6)。

### 下一步

- P6(M2):迁移 v3 —— time_blocks/events/recurrence_rules/calendars 存储、
  重复规则展开与例外、冲突检测、空闲时间搜索、ICS 导入导出;原生智能排程地基。

---

## P6 — 日历与时间块核心(M2 数据/服务层)✅(2026-09-19)

### 已完成(P6a 原生 + P6b 互操作)

- **迁移 v3**:calendars、time_blocks(任务关联可空、准备/缓冲、CHECK end>start)、
  events(全天标志)、recurrence_rules(CHECK host_type、RRULE 字段集合)。
- **Schedule.Scheduling 模块**(纯函数,仅依赖 Domain):
  - `Recurrence`:RRULE 子集展开(DAILY/WEEKLY+BYDAY/MONTHLY date/nth/last-weekday/YEARLY、
    INTERVAL、UNTIL、COUNT、例外日期按本地时区过滤、4096 实例上限)、
    nthWeekdayOfMonth、localDateString。
  - `Conflict`:排序扫描 O(n log n) 重叠对检测。
  - `FreeSlot`:按工作时段(起止分钟 + 工作日掩码)扣除忙碌区间,过滤最短时长。
- **Domain**:`RecurrenceRule::toRruleString/parseRrule`(不支持部分明确拒绝)、
  Calendar/TimeBlock/CalendarEvent 实体。
- **CalendarRepository**:块/事件/规则 CRUD(乐观并发)、窗口查询、
  `materializeWindow`(块+事件+展开实例;准备/缓冲计入冲突区间)、
  例外编辑 `detachOccurrence`(物化+例外日期)与 `splitSeries`(截断+新规则)。
- **ICS**:RFC5545 子集导出(CRLF、转义、UID/DTSTAMP)与导入(续行展开、
  参数剥离、UID 幂等、RRULE 附着;缺时间字段计入 failed)。
- **C ABI(v2 追加)**:日历/块/事件/规则句柄与列表、EqSpanList/EqConflictList/
  EqSlotList、窗口物化/冲突/空闲、例外编辑、ICS 文件导入导出;EqCore 增挂 CalendarRepository。
- **C#**:NativeMethods.P6 + EquoraCore.P6(CalendarDto/TimeBlockDto/CalendarEventDto/
  RecurrenceRuleDto/SpanDto/ConflictDto/FreeSlotDto + 全套方法)、
  ICalendarService + AppDataService.P6 实现。

### 修复的缺陷

- **窗口物化种子重复计入**(真实缺陷,由 C# 冲突测试暴露):宿主事件与规则展开的
  种子实例同时入窗导致"自己与自己冲突 60 分钟"——展开时跳过与宿主同时刻的实例。
- 测试侧:多处 `N * 86'400'000` 整型溢出(int32 超界使窗口为负返回空)、
  日期常量差一天、同毫毫秒排序假设等(均为测试自身错误,实现经复核无误)。

### 构建与测试结果(本机实测)

- 原生:`ctest` 104/104(新增 14 例:重复/冲突/空闲/日历仓库/ICS/迁移 v3);/W4 零警告。
- C#:`dotnet test` 33/33(新增 6 例:日历/块 CRUD 关联任务/系列+冲突+例外/空闲/ICS 往返/非法宿主)。

### 已知问题 / 假设

- 全天事件以调用方计算的本地日界 UTC 区间存储(is_all_day 标记语义展示用);
  跨时区快照字段(存日序号)在同步阶段引入。
- 任务的 completeRecurDays(完成后 N 天)字段已存储,展开器尚未消费(归 P12 智能规划)。
- ICS 未做长行折叠(解析端支持续行,导出超长行在严格客户端可能被拒)。
- eq_slot_list_get 以 reinterpret_cast 提供 FreeSlot→EqSlotView 视图(两者布局一致,纯只读)。

### 下一步

- P7(M2 收尾):日历界面 —— 自定义周视图(虚拟化)、拖拽创建/移动/缩放时间块、
  当前时间线、工作时间底纹、冲突着色、月/议程视图、重复事件的例外编辑入口、ICS 导入导出 UI。

---

## P7 — 日历界面(M2 收尾)✅(2026-09-19)

### 已完成

- **CalendarViewModel**:周窗口物化 + 冲突标记(块 id / ruleId:时刻 双匹配)、
  周/日/议程三模式、周导航(周一锚定)、CreateBlockAt / MoveBlock / ResizeBlock
  (全部接撤销栈;待办拖入时任务转 Planned,撤销回滚)、未安排任务侧栏数据
  (无日期且无块)、ICS 导入导出入口。
- **SlotMath**:15 分钟吸附、本地日界、时间↔像素换算(周视图与测试共用)。
- **WeekView 自定义控件**:7 日 Canvas 定位;小时刻度轴;工作时间底纹(工作日);
  当前时间线(DispatcherTimer 每分钟);块配色(任务蓝/日程绿/重复紫,冲突红描边);
  同列重叠链横向均分;**交互** —— 空白处拖拽创建(吸附)、块整体拖动、
  底部 14px 手柄缩放、拖拽预览(半透明边框);**待办拖入**(ListView 拖放 →
  以落点时间为 60 分钟块)。
- **CalendarPage**:视图切换(周/日共用 WeekView,议程 ListView)、今天/前后导航、
  未安排任务拖放源、ICS 按钮;主窗口新增「日历」导航。
- 说明:周窗口数据量为一周期级,全实例化渲染即可;万级虚拟化按路线在 P16 处理
  (需求允许按里程碑推进)。

### 构建与测试结果(本机实测)

- `dotnet build` 成功(XAML 编译通过);`dotnet test` 40/40(新增 7 例:
  窗口物化+冲突标记、拖入建块+撤销回滚状态、移动/缩放乐观并发+撤销、
  未安排侧栏过滤、周一锚定导航、吸附与像素往返、重复实例不可删除)。

### 已知问题 / 假设

- 无头环境未实际渲染;XAML 编译 + VM 逻辑测试覆盖,交互细节待真机微调。
- 日/议程视图共用周窗口数据(日视图暂渲染整周画布,聚焦滚动在打磨阶段处理)。
- 重复实例在周视图只读(例外编辑入口 UI 随 P8 命令面板/详情完善;
  原生 detach/split 能力已就绪并有测试)。
- 块删除的撤销暂为空操作提示(软删除恢复 ABI 在 P8 冲突中心一并补)。

### 下一步

- P8(M3):四象限视图(拖拽换象限、今日三件要事)、全局快捷键快速输入窗、
  自然语言解析(日期/时长/重复/标签预览)、命令面板。

---

## P8 — 四象限与快速收集(M3)✅(2026-09-19)

### 已完成

- **QuickCaptureParser**(服务层,纯输入辅助无持久化 —— 放置决策已记录):
  相对日期(今天/明天/后天/今晚/明晚/下周X)、钟点(下午3点/17点/8点半/14:30,
  过点顺延次日,晚词传导)、时长(阿拉伯与中文数字、半小时/一个半)、
  简单重复(每周X/每天/工作日/每N天)、#标签(多个)、@项目、!优先级;
  剩余文本即标题,**识别失败绝不丢内容**;CaptureSpan 供预览高亮。
- **QuickCaptureFlyout**(Ctrl+Shift+Space / Ctrl+N / 命令面板):输入即预览
  (标题 + 解析字段摘要),回车提交(建任务/打标签/设优先级,重复文本暂存备注,
  任务级重复展开在 P12 接入)。
- **MatrixViewModel + MatrixPage(四象限)**:可解释归类(重要=importance≥3,
  紧急=3 天窗口内/逾期,规则常量公开);拖拽换象限(Q1/Q3 置重要/紧急并给近程
  截止;Q2/Q4 清除窗口内截止,语义"从容安排"而非顺延 —— 决策注释在代码);
  全部可撤销;今日三件要事(「今日要事」系统标签,Q1/Q2 来源,上限 3,
  双击切换);容量提示(Q1/Q2 计数与估时合计)。
- **CommandPalette**(Ctrl+K):统一入口(页面导航、快速收集、回到今天、立即备份),
  关键词过滤、回车/双击执行。
- **主窗口加速键**:Ctrl+N(任务页新建)、Ctrl+K(面板)、Ctrl+Shift+Space(快速收集)
  —— 挂内容根元素(Window 无 KeyboardAccelerators)。
- 导航新增「四象限」。

### 构建与测试结果(本机实测)

- `dotnet build` 成功;`dotnet test` 54/54(新增 14 例:解析器 10 例 + 四象限 4 例)。
- 解析器测试覆盖需求示例:明天下午3点开会一小时 / 每周五17点写周报 /
  今晚45分钟复习 / 下周三前完成 #产品 !高。

### 已知问题 / 假设

- 全局热键(RegisterHotKey)与托盘图标:App 内快捷键已就绪,系统级热键需窗口
  子类化,列入 M9(P15 Windows 深度集成)真机阶段;M3 验收的"不开主窗口记录"
  由快速收集对话框 + 命令面板承接 App 内场景。
- 四象限拖拽换象限是"建议式"字段修改(重要轴/紧急轴),象限本身不落库 ——
  与"四象限不是独立数据库,仅是查询与编辑视图"的需求一致。
- 命令面板为固定命令集;任务/页面模糊搜索扩展随 P12。

### 下一步

- P9(M4):专注会话核心 —— 迁移 v4(focus_sessions/interruptions/
  distraction_inbox_items/focus_profiles)、计时状态机(番茄/深度/Flowtime/
  正计时/无计时)、暂停/恢复/完成/放弃、崩溃恢复(启动时收尾未闭合会话)、
  分心捕获即时落库。

---

## P9a — 专注会话核心(原生层)✅(2026-09-19)

### 已完成

- **迁移 v4**:focus_sessions(计划/实际起止、paused_ms、state、goal/完成记录/自评,
  开放会话部分索引)、interruptions(级联会话)、distraction_inbox_items(待整理
  部分索引)、focus_profiles(名称唯一含墓碑,应用/站点 JSON 列)。
- **Domain**:FocusMode(番茄/深度/Flowtime/正计时/无计时)、SessionState、
  FocusSession(effectiveMs 扣除暂停)、Interruption、DistractionItem、FocusProfile。
- **FocusRepository**:
  - 状态机:start(唯一开放会话约束)、pause/resume(状态不符 Conflict;
    暂停时长按 updated_at 差累积)、complete(可携带备注与 0-100 自评)、
    abandon;乐观并发;计时滴答不落库,状态转换才写。
  - **崩溃恢复**:recoverInterrupted 把未闭合会话以 updated_at(最后活动,
    由中断记录推进)收尾为 Abandoned —— "崩溃后会话和分心记录可恢复"。
  - 中断记录(手动/应用切换来源)、分心捕获(即时落库,先持久化后整理,
    resolution 1-5 + 去向引用,重复整理拒绝)、专注预设 CRUD。
- 测试 7 例:生命周期(暂停累计/有效时长/闭合后拒绝)、Flowtime 无计划端、
  任务关联与历史、中断记录、分心捕获与整理、崩溃恢复、预设 CRUD。

### 构建与测试结果(本机实测)

- 原生 111/111(新增 7 例);C# 54/54(schema 断言同步 v4)。

### 已知问题 / 假设

- 会话状态机的时间参数由调用方注入(测试确定性);UI 时钟在 P10 接。
- focus_profiles 的应用/站点 JSON 为透传文本(P10 校验与执行)。

### 下一步

- P9b:C ABI(focus 面会话状态机/中断/捕获/预设)+ C# IFocusService
  + 启动时 recoverInterrupted 接线。

---

## P10 — 专注界面与温和限制(M4 收尾)✅(2026-09-19)—— M4 完成

### 已完成

- **FocusViewModel**:会话状态机 UI 绑定(开始/暂停/继续/完成/放弃)、
  计时显示(倒计时:番茄/深度;正计:Flowtime/正计时/无计时,暂停自动扣除)、
  任务关联(开始→InProgress,结束→Planned)、分心捕获(即时落库 + 转任务/丢弃)、
  FocusProfile 应用、今日专注统计(有效时长累计)、温和提醒文案与 app-switch 中断记录。
- **FocusPage**:大字号计时、模式/时长/任务/目标/预设配置区、进行中控制条、
  温和提醒横幅、分心捕获(回车即记,不切焦点)、待整理列表(转任务/丢弃)。
- **AppMonitor**(温和提醒级):SetWinEventHook 前台事件 → 进程名匹配,
  不采集窗口标题/内容;AppRuleMatcher 纯函数(白名单优先、精确名或无扩展名、
  大小写不敏感、不做子串误报)。
- **设置页隐私区**:前台监测开关(**默认关闭**,隐私优先)、受限应用名单编辑、
  明确的"仅进程名、不采集内容、关闭即停"说明。

### 构建与测试结果(本机实测)

- `dotnet build` 成功;`dotnet test` 68/68(新增 9 例:会话+统计、任务状态流转、
  捕获与转任务、受限应用提醒+中断记录、预设应用、匹配器 4 例)。

### 已知问题 / 假设

- AppMonitor 的 WinEvent 钩子需真机验证(无头环境仅测匹配逻辑);
  SetWinEventHook 在 UI 线程回调,与 VM 交互无跨线程问题。
- "允许 5 分钟"按钮(临时白名单)留 P11 与网站限制一起做。
- 迷你置顶窗、任务栏进度、托盘图标归 P15 Windows 深度集成。

### 下一步

- P11(M5):Edge/Chrome MV3 扩展(declarativeNetRequest 动态/会话规则、
  白名单优先、预算)、Native Messaging 带版本协议、限制页(返回任务/临时允许/紧急解锁)、
  异常恢复(扩展重启/主程序异常后状态一致)。

---

## P11 — 网站限制(M5)✅(2026-09-19)

### 已完成

- **MV3 扩展**(extension/Equora.BrowserExtension):
  - **会话规则设计(可恢复限制的关键)**:扩展不持久化限制;每 30s 从 Native Host
    拉取专注状态,用 declarativeNetRequest session rules 下发 —— 浏览器重启、
    扩展/主程序异常后规则自然为空,绝无永久阻断;host 掉线时主动清空规则。
  - background.js:连接管理(掉线 10s 重连)、白名单优先、临时允许 5 分钟、
    域名预算(仅可见 tab 按分钟累计,存 chrome.storage.local,按日)。
  - blocked.html/js 拦截页:当前任务、剩余时间倒数、返回任务 / 允许 5 分钟,
    明示"会话结束自动解除"。
  - README:开发安装全流程(host manifest 生成、注册表、解包加载)、协议表、
    隐私边界与已知限制。
- **Native Messaging 协议层**(NativeInterop,可测):Chrome 帧编解码
  (4 字节小端 + 1MB 上限)、FNV-1a 校验和、FocusGateState、FocusGate 映射
  (预设 JSON → 规范化域名:去协议/路径/端口/www、去重)、子域名匹配
  (白名单优先、无子串误报)。
- **Equora.NativeHost**(com.equora.nativehost.exe):stdin/stdout 循环、
  版本检查、校验和验证、nonce 严格递增(重放拒绝)、query → 打开同一数据库
  只读开放会话与默认预设 → FocusGateState;--print-manifest 生成 host manifest。
- **本机端到端冒烟实测**:hello 握手 ✓、query 返回状态 JSON ✓、
  版本不匹配 → error ✓、未知 type → error ✓。

### 构建与测试结果(本机实测)

- `dotnet build`(含 NativeHost)成功;`dotnet test` 77/77(新增 9 例:
  帧编解码 3、状态映射 3、域名匹配 3)。

### 已知问题 / 假设

- host 打开同一 SQLite(只读查询 + WAL):多进程短期可接受,
  P14 引入正式 IPC 后改为桌面端代理。
- 拦截重定向 regexSubstitution 为简化实现(README 已注明生产化替换方案)。
- 扩展行为需真机浏览器验证(无头环境覆盖协议层与纯函数)。
- 紧急解锁(系统级出口)与浏览器集成测试归 M9/P15。

### 下一步

- P12(M6):空闲时间搜索 + 过载识别 + 排程差异预览与一次性撤销、
  估时校正、任务拆分建议、每日/每周复盘、自动化规则与执行日志。

---

## P12a — 智能规划、复盘与自动化(原生层)✅(2026-09-19)

### 已完成

- **迁移 v5**:daily_reviews / weekly_reviews(按日/周唯一,覆盖更新 revision+1)、
  automation_rules(触发/条件/动作 JSON)、automation_logs(执行日志)。
- **Schedule.Scheduling::Planner(确定性规则引擎)**:
  - `planWeek`:候选排序(截止近→优先级高→估时长"大石先装"),在工作时段空闲
    顺次填充;估时超上限自动拆多块;每条建议附可解释理由;同输入结果确定。
    实现修正:剩余估时按下标而非任务 id(未入库任务 id 冲突)。
  - `dayLoads` 过载识别(按本地日聚合 vs 工作容量);
  - `suggestSplits` 拆分建议(块数与单块时长);
  - `correctEstimate` 估时校正(同任务历史有效时长中位数,≥2 样本才建议,
    异常样本过滤,保留原始估计)。
- **ReviewRepository**:每日复盘(完成/延期/取消/计划与实际时长/分心次数,
  从任务+时间块+专注会话聚合)、每周复盘(深度时长、完成率、估时准确度、
  最佳专注时段直方图);按日/周存档与重载;recentDaily。
- **AutomationRepository**:规则 CRUD(乐观并发、启用开关)、
  `evaluate`(触发类型 + 条件[优先级/估时],JSON 损坏跳过不扩散)、
  执行日志(含失败原因;排序加 rowid 决胜)。
- 测试 8 例:排程确定性/忙碌避让/maxBlock 拆分、过载、拆分建议、
  估时中位数(奇偶/异常过滤/样本不足)、每日聚合、每周比率与最佳时段、
  自动化 CRUD+评估+日志。

### 构建与测试结果(本机实测)

- 原生 119/119(新增 8);C# 77/77(schema 断言同步 v5)。

### 已知问题 / 假设

- planWeek 的跨任务填充是贪心的(先到先得);"如果今天只能完成三件事"
  与保留原计划/顺延选项随 P12b 预览界面提供。
- 自动化的标签条件尚未接任务-标签查询(仓库层无法直接查;调用方补查);
  动作执行(add_tag/suggest_split)由 C# 侧在 P12b 实现 —— 评估不执行动作,
  天然防递归。
- 复盘的三件要事完成度依赖 BigThree 标签查询,P12b 聚合时补。

### 下一步

- P12b:C ABI(plan/review/automation)+ C# 服务与界面(排程差异预览 +
  一次性撤销、每日/每周复盘页、自动化规则管理)。
