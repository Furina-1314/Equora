# 衡序 Equora

**本地优先的 Windows 日程规划与专注软件。**

[官方网站](https://furina-1314.github.io/Equora/) · [下载安装包](https://github.com/Furina-1314/Equora/releases/latest)

任务、日历、四象限、专注与应用限制,共享同一份本地数据。无账号,无广告,离线可用;数据始终存储在用户自己的设备上。

<p align="center">
  <img src="docs/images/app-home.png" alt="衡序 Equora — 工作概览" width="49%" />
  &nbsp;
  <img src="docs/images/app-tasks.png" alt="衡序 Equora — 任务管理与详情" width="49%" />
</p>

> **Equora** = **Equ**ilibrium + **H**ora(拉丁语"时间")——在时间中保持平衡,即"衡序"。

当前版本 [v0.2.2](https://github.com/Furina-1314/Equora/releases/tag/v0.2.2) · 138 项原生、140 项桌面、20 项服务器及 8 项扩展测试通过 · [发布说明](docs/release-notes.md) · [下载安装包](https://github.com/Furina-1314/Equora/releases/latest)

## 功能

| 模块 | 能力 |
|---|---|
| 任务管理 | 三栏布局,九个智能清单,项目与标签,全文搜索,撤销栈,回收站恢复与确认后永久删除 |
| 日历 | 周视图时间块,拖拽创建、移动与缩放,冲突检测,重复规则,ICS 导入导出;学期周次与每周/指定周次批量安排,生成任务可独立编辑 |
| 四象限 | 重要/紧急矩阵,拖拽换象限,今日要事,容量提示 |
| 快速收集 | 全局快捷键 `Ctrl+Shift+Space`,中文自然语言解析(日期、时长、标签、优先级),预览确认 |
| 专注 | 番茄钟、深度工作、正计时三种模式,暂停与继续,工作/休息轮次,分心捕获,崩溃恢复 |
| 使用限制 | 应用程序与网站域名规则(Edge/Chrome 扩展);专注期间禁止、每日时段、每日额度三类条件;临时允许与一键暂停 |
| 智能规划 | 确定性排程引擎,空闲时间搜索,估时校正,任务拆分建议 |
| 复盘 | 每日与每周报告:计划对比实际、专注度、最佳时段、估时准确度 |
| 自动化 | 触发器、条件、动作,防递归保护,执行日志 |
| 多设备同步 | 可选自托管服务器(Docker),冲突中心,幂等重试,指数退避 |
| 安全设计 | 关键进程白名单,紧急解锁,脱敏审计日志 |

## 安装

从 [Releases](https://github.com/Furina-1314/Equora/releases) 获取。两种桌面安装包均为自包含,无需安装 .NET 运行时。

**EXE 安装包(推荐)** —— 双击 `Equora-vX.Y.Z-win-x64-Setup.exe` 完成安装。安装到当前用户目录,不需要管理员权限,不需要信任证书。

**MSIX 包** —— 下载 `Equora-vX.Y.Z-win-x64.msix` 与随附的 `.cer` 证书。安装包使用自签名证书签署,首次安装需将其导入本机受信任存储(管理员 PowerShell):

```powershell
Import-Certificate -FilePath .\Equora-vX.Y.Z.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage .\Equora-vX.Y.Z-win-x64.msix
```

**网站限制扩展** —— 启用网站限制需要加载随发行版提供的 Edge/Chrome 扩展(`Equora-vX.Y.Z-BrowserExtension.zip` 或安装目录内的 `BrowserExtension` 文件夹),并按应用内"使用限制"页的指引连接。详见 [使用限制指南](docs/usage-restrictions.md)。

## 从源码构建

前置要求:Windows 10 19041 或更高 · Visual Studio 2022(C++ 桌面开发负载)· CMake 3.24+ · .NET SDK 10 · [vcpkg](https://github.com/microsoft/vcpkg)

```bash
git clone https://github.com/Furina-1314/Equora.git
cd Equora

# 设置 VCPKG_ROOT 指向你的 vcpkg 克隆(如 E:\code\vcpkg)

# 1. 构建原生核心(C++20)
cd native
cmake --preset win-x64-release
cmake --build --preset win-x64-release
ctest --preset win-x64-release    # 138/138 通过

# 2. 构建桌面应用(WinUI 3)
cd ../desktop
dotnet build Equora.slnx -c Release
dotnet test Equora.App.Tests -c Release --no-build   # 140/140 通过

# 3. 运行
dotnet run --project Equora.App -c Release
```

同步服务器(可选,Docker Compose):

```bash
docker compose -f docker/docker-compose.yml up --build

# 服务地址: http://127.0.0.1:8787
# 健康检查: curl http://127.0.0.1:8787/api/v1/health
# 默认令牌: dev-local-token(可用环境变量自定义)
```

发布打包(MSIX 与 EXE 安装器)由 [desktop/packaging/build-release.ps1](desktop/packaging/build-release.ps1) 一键完成,详见脚本头注。

## 测试与持续集成

| 测试套件 | 数量 | CI |
|---|---|---|
| 原生核心(GoogleTest / C++20) | 138 | [![native-ci](https://github.com/Furina-1314/Equora/actions/workflows/native-ci.yml/badge.svg)](https://github.com/Furina-1314/Equora/actions/workflows/native-ci.yml) |
| 桌面应用(xUnit / C#) | 140 | [![desktop-ci](https://github.com/Furina-1314/Equora/actions/workflows/desktop-ci.yml/badge.svg)](https://github.com/Furina-1314/Equora/actions/workflows/desktop-ci.yml) |
| 同步服务器(GoogleTest) | 20 | [![server-ci](https://github.com/Furina-1314/Equora/actions/workflows/server-ci.yml/badge.svg)](https://github.com/Furina-1314/Equora/actions/workflows/server-ci.yml) |
| 浏览器扩展 | 8 | 本地验证 |
| **合计** | **306** | |

## 架构

```text
┌─────────────────────────────────────────────────────┐
│  桌面前端(C# / WinUI 3 / MVVM)                    │
│  Equora.App / ViewModels / Services / NativeInterop │
├────────── 稳定 C ABI v2(equora_capi.dll)──────────┤
│  本地核心(C++20, 零警告 /W4)                      │
│  ┌──────────┐ ┌──────────┐ ┌───────────────┐      │
│  │ Domain   │ │ Storage  │ │ Scheduling    │      │
│  │ 实体/值对象│ │ SQLite迁移│ │ 排程/冲突/空闲 │      │
│  └──────────┘ └──────────┘ └───────────────┘      │
│  ┌──────────┐ ┌──────────┐ ┌───────────────┐      │
│  │ Core     │ │ SyncClient│ │ Common        │      │
│  │ 仓库/用例 │ │ Outbox/退避│ │ 日志/SHA-256  │      │
│  └──────────┘ └──────────┘ └───────────────┘      │
├─────────────────────────────────────────────────────┤
│  本地 SQLite(WAL, 6 个版本化迁移)                │
└─────────────────────────────────────────────────────┘
              ↑ HTTPS + JSON 协议 v1(可选)
┌─────────────────────────────────────────────────────┐
│  同步服务器(Drogon + PostgreSQL, Docker)           │
│  幂等 · base_revision 冲突 · 墓碑 · 有序游标        │
│  令牌桶限流 · 脱敏审计 · SHA-256 令牌哈希           │
└─────────────────────────────────────────────────────┘
```

## 仓库结构

```text
Equora/
├─ desktop/                    C# 桌面应用
│  ├─ Equora.App/              WinUI 3 入口(页面/控件/转换器)
│  ├─ Equora.App.ViewModels/   MVVM 视图模型
│  ├─ Equora.App.Services/     数据服务/同步/限制/安全机制
│  ├─ Equora.App.NativeInterop/ C ABI P/Invoke 封装
│  ├─ Equora.NativeHost/       Native Messaging Host
│  ├─ Equora.App.Tests/        xUnit 测试
│  └─ packaging/               发布打包脚本(MSIX / Inno Setup)
├─ native/                     C++20 原生核心
│  ├─ Schedule.Domain/         实体/值对象(纯 C++)
│  ├─ Schedule.Common/         日志/SHA-256
│  ├─ Schedule.Storage/        SQLite/迁移/备份
│  ├─ Schedule.Scheduling/     排程/冲突/空闲/重复展开
│  ├─ Schedule.Core/           仓库/用例/复盘/自动化
│  ├─ Schedule.Sync/           Outbox/退避/合并
│  ├─ Schedule.CApi/           稳定 C ABI 导出层
│  └─ Schedule.Core.Tests/     GoogleTest(137 例)
├─ server/                     同步服务器
│  ├─ Equora.SyncCore/         协议核心(纯逻辑)
│  ├─ Equora.Server/           Drogon HTTP + PostgreSQL
│  └─ Equora.Server.Tests/     GoogleTest(20 例)
├─ extension/                  Edge/Chrome MV3 扩展
├─ docker/                     Dockerfile + Compose
├─ docs/                       架构/数据模型/用户指南/隐私/发布说明
└─ THIRD-PARTY-NOTICES.md      第三方组件许可清单
```

## 文档

| 文档 | 说明 |
|---|---|
| [用户指南](docs/user-guide.md) | 安装、功能操作、FAQ、快捷键 |
| [使用限制指南](docs/usage-restrictions.md) | 应用与网站限制的规则、安全边界与浏览器扩展连接 |
| [隐私与安全](docs/privacy-and-security.md) | 采集清单、网络通信、限制安全出口 |
| [架构](docs/architecture.md) | 分层设计、C ABI 规则、线程模型 |
| [数据模型](docs/data-model.md) | 实体定义、迁移策略、备份恢复 |
| [开发进度](docs/progress.md) | P0–P17 全阶段记录 |
| [阶段规划](docs/PHASES.md) | 里程碑映射 |
| [发布说明](docs/release-notes.md) | 版本变更、已知限制 |
| [第三方声明](THIRD-PARTY-NOTICES.md) | 开源组件许可 |

## 设计原则

- **本地优先** —— 无账号、无网络时全部核心功能可用。
- **单一事实源** —— 日历、清单、四象限、专注共享同一批任务数据。
- **可恢复的限制** —— 一切应用/网站限制必有关键进程白名单、紧急解锁与自动解除。
- **可解释的智能** —— 自动排程附理由,冲突显示双方版本,估时校正保留原始值。
- **隐私优先** —— 前台监测默认关闭;开启后仅读进程名,不读窗口内容。

## 许可证

[MIT](LICENSE) © 2026 Furina-1314

## 反馈

问题与建议请提交 [GitHub Issues](https://github.com/Furina-1314/Equora/issues)。

---

**衡序 Equora** —— 在时间中保持平衡。
