# Equora 架构

> 状态:P2(2026-09-18)。本文描述当前实现与目标架构;标注「(后续)」的部分尚未实现。

## 1. 总体分层

```text
┌─────────────────────────────────────────────────────────────┐
│  桌面前端(C# / WinUI 3)                                    │
│  Schedule.App / ViewModels / Services / NativeInterop        │
│  职责:界面、展示状态、导航、动画、无障碍、平台交互              │
├─────────────── C ABI(稳定边界,equora_capi.dll)─────────────┤
│  本地核心(C++20)                                            │
│  ┌────────────────┐  ┌────────────────┐  ┌───────────────┐  │
│  │ Schedule.CApi  │→ │ Schedule.Core  │→ │ Domain        │  │
│  │ 导出层/异常边界 │  │ 用例/仓库/规则  │  │ 实体/值对象    │  │
│  └────────────────┘  └───────┬────────┘  └───────────────┘  │
│                              ↓                                │
│                      ┌────────────────┐                       │
│                      │ Schedule.Storage│ SQLite、事务、迁移     │
│                      └────────────────┘                       │
│  (后续)Scheduling 排程 / Sync 同步 / Focus 专注 / Analytics   │
├─────────────────────────────────────────────────────────────┤
│  本地文件:SQLite(单一事实源)                                │
└─────────────────────────────────────────────────────────────┘
              ↑ 同步协议(HTTPS + JSON,后续)
┌─────────────────────────────────────────────────────────────┐
│  云端(后续):Drogon REST API + PostgreSQL(Docker)           │
└─────────────────────────────────────────────────────────────┘
```

### 依赖方向(禁止循环)

```text
Domain ← Storage ← Core ← CApi ← (桌面端 / 测试)
                    ← (后续)Sync / Scheduling / Focus / Analytics
```

- Domain 是纯 C++:不依赖 SQLite、WinUI、网络库。
- UI 永远不直接执行 SQL,只通过 C ABI。
- 云端与桌面端可共享纯领域规则与协议定义,不共享进程状态。

## 2. 进程与部署形态

- **桌面应用**:WinUI 3 进程,加载 `equora_capi.dll` 与 `sqlite3.dll`。
  MSIX 安装(后续),DLL 随包分发。
- **本机核心**:与桌面应用同进程(C ABI 直调),无独立服务;
  「严格限制」阶段才引入可选 Windows Service(P9/P15)。
- **浏览器扩展**(后续):独立进程,经 Native Messaging 与桌面程序通信。
- **同步服务**(后续):独立 Docker 部署,客户端仅访问 HTTPS API。

## 3. C ABI 边界规则(必须长期遵守)

头文件:`native/Schedule.CApi/include/equora/capi/equora_capi.h`

1. 只用 C 类型、不透明句柄(`EqCore`/`EqTaskHandle`/`EqTaskList`)、UTF-8 字符串、固定布局 DTO(`EqError`/`EqTaskInput`/`EqTaskView`)。
2. 跨边界禁止:`std::string`、STL 容器、C++ 异常、智能指针、C++ 类。
3. 内存所有权:原生分配 → 原生释放(`eq_*_destroy`);视图指针生命周期绑定所属句柄。
4. 所有导出函数捕获全部异常,转换为 `ErrorCode` + `EqError`(`guard` 边界)。
5. 批量优先:`eq_task_list_all` 一次跨 ABI 取回一批,避免逐条调用。
6. 版本化:`EQUORA_CAPI_VERSION` + `eq_api_version()`;破坏 ABI 必须升版本号。
7. 冒烟接口:`eq_ping` 供互操作层自检。

## 4. 数据流与一致性

- 所有写入走「单事务」:业务表 + (后续)`SyncOutbox` 同事务提交;崩溃后不出现半个操作。
- 任务实体带乐观并发字段 `revision`:更新必须携带基线 revision,不匹配即 `Conflict`。
- 软删除墓碑 `deleted_at`:列表默认过滤,同步阶段用于防止删除被离线写入复活。
- 时间统一 UTC 毫秒(Unix epoch)存储;展示层才做时区转换。

## 5. 模型扩展路线

| 阶段 | 新增模块 | 内容 |
|---|---|---|
| P4 | Storage v2 迁移 | Project/Tag/TaskTag/ChecklistItem 表,智能清单查询 |
| P6 | Scheduling | TimeBlock/Event/RRULE 展开、冲突检测、空闲搜索 |
| P9 | Focus | FocusSession 状态机、Interruption、DistractionInboxItem |
| P12 | Analytics | 本地统计与建议(确定性规则) |
| P13+ | Sync | Outbox/游标/冲突合并,与服务端协议对应 |

## 6. 线程模型(当前约定)

- `Database` 以 `SQLITE_OPEN_FULLMUTEX` 打开,连接本身可跨线程持有;
  但当前所有调用都在调用方线程同步完成,无内部后台线程。
- (后续)同步引擎与活动检测引入后台线程时,必须在 C ABI 层聚合到句柄内部加锁,不向调用方暴露锁。

## 7. 错误处理策略

| 层 | 机制 |
|---|---|
| Domain/Core | `EquoraError(code, message)` 异常 + `ErrorCode` 稳定枚举 |
| Storage | SQLite 返回码 → `StorageError`;迁移失败 → `MigrationError`(已回滚) |
| C ABI | 异常 → 错码 + `EqError.message`(截断 255) |
| C#(后续) | 错码 → `EquoraException`;UI 层用户可读信息 |

## 8. 安全基线

- 密钥/令牌不入源码、不入日志;桌面令牌走 DPAPI/Windows 凭据(后续)。
- 日志脱敏:不记录任务正文与令牌。
- 扩展最小权限;Service 最小权限 + IPC 调用方校验(后续)。

## 9. 构建与质量门

- C++:`cmake --preset win-x64-release` → `/W4 /permissive- /utf-8`,零警告;
  `ctest` 全绿(当前 41 例)。
- CI:`.github/workflows/native-ci.yml`(windows-latest:configure/build/test)。
- 依赖经 vcpkg 清单锁定(`native/vcpkg.json` + baseline commit)。
