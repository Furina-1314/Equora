# Equora 开发进度

> 阶段划分见 [PHASES.md](PHASES.md)。每完成一个阶段在此登记:已完成、未完成、已知问题、下一步。
> 本文档只记录真实结果,不预先声称未验证的功能。

## 当前状态

- **当前阶段**:P6 — 日历与时间块核心(M2)
- **已完成**:P0–P4(M0 达成;M1 核心+服务层完成)
- **最新提交**:P5 任务管理界面

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
