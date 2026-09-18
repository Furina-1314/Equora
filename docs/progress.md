# Equora 开发进度

> 阶段划分见 [PHASES.md](PHASES.md)。每完成一个阶段在此登记:已完成、未完成、已知问题、下一步。
> 本文档只记录真实结果,不预先声称未验证的功能。

## 当前状态

- **当前阶段**:P3 — WinUI 3 应用外壳
- **已完成**:P0、P1、P2
- **最新提交**:P2 C ABI 互操作层

---

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
