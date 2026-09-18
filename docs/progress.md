# Equora 开发进度

> 阶段划分见 [PHASES.md](PHASES.md)。每完成一个阶段在此登记:已完成、未完成、已知问题、下一步。
> 本文档只记录真实结果,不预先声称未验证的功能。

## 当前状态

- **当前阶段**:P1 — C++ 原生核心骨架
- **已完成**:P0
- **最新提交**:P0 仓库初始化

---

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
