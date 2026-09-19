# 衡序 Equora

**本地优先的 Windows 日程规划与专注软件。**

> **Equora** = **Equ**ilibrium + **H**ora(拉丁语"时间")——在时间中保持平衡,即"衡序"。
>
> 🏆 **v0.1.0 发布候选** · M0–M10 全部里程碑达成 · 262 例自动化测试全通过 · [发布说明](docs/release-notes.md)

## ✨ 功能总览

| 功能 | 说明 |
|---|---|
| 📋 **任务管理** | 三栏布局,9 个智能清单,搜索,撤销,回收站恢复 |
| 📅 **日历时间块** | 周视图拖拽创建/移动/缩放,冲突检测,重复规则,ICS 导入导出 |
| 🎯 **四象限** | 重要/紧急矩阵,拖拽换象限,今日三件要事,容量提示 |
| ⚡ **快速收集** | `Ctrl+Shift+Space` 全局快捷键,中文自然语言解析(日期/时长/标签/优先级),预览确认 |
| 🍅 **专注会话** | 番茄钟/深度工作/Flowtime/正计时/无计时五种模式,分心捕获,崩溃恢复 |
| 🌐 **网站限制** | Edge/Chrome MV3 扩展,会话规则(浏览器重启自动解除),白名单优先 |
| 🧠 **智能规划** | 确定性排程引擎,空闲时间搜索,估时校正,任务拆分建议 |
| 📊 **每日/每周复盘** | 计划 vs 实际,专注度比率,最佳时段,估时准确度 |
| 🔄 **自动化规则** | 触发器/条件/动作,防递归,执行日志 |
| ☁️ **多设备同步** | 本地优先,可选自托管同步服务器(Docker),冲突中心,幂等重试 |
| 🔒 **安全设计** | 20+ 关键进程永不限,紧急解锁,审计日志,脱敏 |

## 📥 快速安装

### 桌面应用(开发版)

**前置要求**:Windows 10 1809+ / Windows 11 · Visual Studio 2022(含 C++ 桌面开发) · CMake 3.24+ · .NET SDK 10 · [vcpkg](https://github.com/microsoft/vcpkg)

```bash
git clone https://github.com/Furina-1314/Equora.git
cd Equora

# 设置 VCPKG_ROOT 指向你的 vcpkg 克隆(如 E:\code\vcpkg)

# 1. 构建原生核心(C++20)
cd native
cmake --preset win-x64-release
cmake --build --preset win-x64-release
ctest --preset win-x64-release    # 137/137 通过

# 2. 构建桌面应用(WinUI 3)
cd ../desktop
dotnet build Equora.slnx -c Release
dotnet test Equora.App.Tests --no-build   # 105/105 通过

# 3. 运行
dotnet run --project Equora.App -c Release
```

### 同步服务器(可选,Docker Compose)

```bash
# 自动启动 PostgreSQL + Equora Server
docker compose -f docker/docker-compose.yml up --build

# 服务器: http://127.0.0.1:8787
# 健康检查: curl http://127.0.0.1:8787/api/v1/health
# 令牌: dev-local-token(环境变量自定义)
```

### 浏览器扩展(可选)

```bash
# 1. 构建 Native Host
cd desktop && dotnet build Equora.NativeHost -c Release

# 2. 注册(详见 extension/Equora.BrowserExtension/README.md)
# 3. chrome://extensions → 开发者模式 → 加载解包的扩展 → 选择 extension/Equora.BrowserExtension
```

## 📊 测试覆盖

| 测试套件 | 数量 | CI |
|---|---|---|
| 原生核心(GoogleTest / C++20) | 137 | [![native-ci](https://github.com/Furina-1314/Equora/actions/workflows/native-ci.yml/badge.svg)](https://github.com/Furina-1314/Equora/actions/workflows/native-ci.yml) |
| 桌面应用(xUnit / C#) | 105 | [![desktop-ci](https://github.com/Furina-1314/Equora/actions/workflows/desktop-ci.yml/badge.svg)](https://github.com/Furina-1314/Equora/actions/workflows/desktop-ci.yml) |
| 同步服务器(GoogleTest) | 20 | [![server-ci](https://github.com/Furina-1314/Equora/actions/workflows/server-ci.yml/badge.svg)](https://github.com/Furina-1314/Equora/actions/workflows/server-ci.yml) |
| **合计** | **262** | **全部通过** |

## 🏗️ 架构

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

## 📁 仓库结构

```text
Equora/
├─ desktop/                    C# 桌面应用
│  ├─ Equora.App/              WinUI 3 入口(页面/控件/转换器)
│  ├─ Equora.App.ViewModels/   MVVM 视图模型
│  ├─ Equora.App.Services/     数据服务/同步/安全机制
│  ├─ Equora.App.NativeInterop/ C ABI P/Invoke 封装
│  ├─ Equora.NativeHost/       Native Messaging Host
│  └─ Equora.App.Tests/        xUnit 测试
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

## 📚 文档

| 文档 | 说明 |
|---|---|
| [用户指南](docs/user-guide.md) | 安装、功能操作、FAQ、快捷键 |
| [隐私与安全](docs/privacy-and-security.md) | 采集清单、网络通信、限制安全出口 |
| [架构](docs/architecture.md) | 分层设计、C ABI 规则、线程模型 |
| [数据模型](docs/data-model.md) | 实体定义、迁移策略、备份恢复 |
| [开发进度](docs/progress.md) | P0–P16 全阶段记录 |
| [阶段规划](docs/PHASES.md) | 里程碑映射 |
| [发布说明](docs/release-notes.md) | 版本变更、已知限制 |
| [第三方声明](THIRD-PARTY-NOTICES.md) | 开源组件许可 |

## 🎨 核心设计原则

- **本地优先**:无账号、无网络时全部核心功能可用
- **单一事实源**:日历、列表、四象限、专注共享同一批任务数据
- **可恢复限制**:一切应用/网站限制必有安全白名单 + 紧急解锁 + 自动解除
- **可解释智能**:自动排程附理由,冲突显示双方版本,估时校正保留原始值
- **隐私优先**:前台监测默认关闭;开启后仅读进程名,不读窗口内容

## 📄 许可证

[MIT](LICENSE) © 2026 Furina-1314

## 🤝 贡献

发现问题或建议改进请提交 [GitHub Issues](https://github.com/Furina-1314/Equora/issues)。

---

**衡序 Equora** —— 在时间中保持平衡。
