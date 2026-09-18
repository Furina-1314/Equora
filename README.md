# 衡序 Equora

本地优先的 Windows 日程规划与专注软件。

> **Equora** = **Equ**ilibrium + **H**ora(拉丁语"时间")——在时间中保持平衡,即"衡序"。

## 项目简介

Equora 覆盖完整的时间管理闭环:

1. **快速收集** — 任务与想法秒级记录(全局快捷键、自然语言解析)。
2. **判断优先级** — 重要/紧急四象限,决策辅助而非替用户决策。
3. **安排日程** — 拖拽式日历时间块,冲突检测与过载警告。
4. **专注执行** — 番茄钟、深度工作、Flowtime 多种计时模式。
5. **限制分心** — 温和提醒 / 增加摩擦 / 严格限制三级应用与网站管控,全部可恢复。
6. **记录复盘** — 计划时长、实际时长、中断与完成情况,本地统计分析。
7. **多设备同步** — 单机离线完整可用,按需接入自托管或云同步服务。

## 核心原则

- **本地优先**:无账号、无网络时全部核心功能可用。
- **单一事实源**:日历、列表、四象限、专注界面共享同一批任务数据。
- **隐私优先**:专注分析仅在本地进行,不记录按键、屏幕与文档内容。
- **可恢复限制**:一切应用/网站限制必有白名单、紧急解锁与崩溃恢复。
- **原生 Windows 体验**:WinUI 3 / Fluent Design / 高 DPI / 深浅色 / 无障碍。

## 技术栈

| 层 | 技术 |
|---|---|
| 桌面前端 | C# / .NET / WinUI 3 (Windows App SDK) / CommunityToolkit.Mvvm / MSIX |
| 本地核心 | C++20 / CMake / SQLite,领域模型、排程、同步、专注规则 |
| 互操作 | 稳定 C ABI + .NET `LibraryImport` P/Invoke |
| 云端同步(后期) | C++20 / Drogon / PostgreSQL / Docker |
| 浏览器限制(后期) | Edge/Chrome MV3 扩展 / declarativeNetRequest / Native Messaging |

## 仓库结构

```text
desktop/    C# WinUI 3 应用(入口、ViewModel、服务、P/Invoke 封装)
native/     C++ 领域模型、存储、排程、同步、专注、分析与 C ABI
extension/  Edge/Chrome 扩展(后期)
server/     Drogon 同步服务(后期)
docs/       架构、数据模型、同步协议、进度等文档
tests/      应用层测试与测试数据
```

## 构建

### 前置要求

- Windows 10 1809+ / Windows 11
- Visual Studio 2022(含 *使用 C++ 的桌面开发* 工作负载)
- CMake 3.24+
- .NET SDK(.NET 8+)
- [vcpkg](https://github.com/microsoft/vcpkg)(用于 `sqlite3`、`gtest` 等原生依赖)

### 构建 C++ 原生核心

```bash
git clone https://github.com/Furina-1314/Equora.git
cd Equora

# 假设 vcpkg 位于 C:\dev\vcpkg(VCPKG_ROOT 已设置)
cmake -S native -B native/build --preset win-x64-release
cmake --build native/build --config Release
ctest --test-dir native/build --build-config Release --output-on-failure
```

### 构建 WinUI 3 应用(后续阶段提供)

```bash
cd desktop
dotnet build Equora.sln -c Release
```

## 开发阶段

开发按阶段(Phase)推进,每个 Phase 以可运行成果 + Git 提交为断点。
阶段划分与当前进度见 [docs/PHASES.md](docs/PHASES.md) 与 [docs/progress.md](docs/progress.md)。

## 许可证

[MIT](LICENSE) © 2026 Furina-1314
