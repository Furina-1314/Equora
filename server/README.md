# Equora 同步服务

Drogon + PostgreSQL 的同步服务器(模式 B/C 共用同一镜像)。

## 组件

| 路径 | 说明 |
|---|---|
| `Equora.SyncCore/` | 协议核心(幂等 / base_revision 冲突 / 墓碑 / 游标)+ MemorySyncStore + JSON 编解码 —— 全部可单测 |
| `Equora.Server/` | Drogon HTTP 层 + PostgresSyncStore(重依赖,独立 preset) |
| `Equora.Server.Tests/` | gtest(当前 15 例,轻量 preset 可跑) |
| `../migrations/` | PostgreSQL 迁移(001 初始化) |
| `../docker/` | Dockerfile + docker-compose(本机模式 B) |

## 本机开发

```bash
# 轻量(仅协议核心 + 测试,无重依赖):
cd server
cmake --preset win-x64-release && cmake --build --preset win-x64-release
ctest --preset win-x64-release

# 完整服务器(drogon/libpq;较重):
cmake --preset win-x64-server && cmake --build --preset win-x64-server --target equora_server

# 数据库 + 服务器(Compose;迁移自动执行):
cd .. && docker compose -f docker/docker-compose.yml up --build
```

## API

### `GET /api/v1/health`
`{"status":"ok"}`(数据库不可达时 503 `degraded`)。

### `POST /api/v1/sync`
```
Authorization: Bearer <token>
Content-Type: application/json
```
```json
{
  "protocolVersion": 1,
  "deviceId": "uuid",
  "cursor": 18291,
  "operations": [
    {"operationId": "uuid", "entityType": "task", "entityId": "uuid",
     "baseRevision": 5, "operation": "update", "payload": {}}
  ]
}
```

响应:`outcomes[]`(accepted / duplicate / conflict[带服务端版本] / invalid)、
`changes[]`(有序变更流,含墓碑)、`nextCursor`。

安全:令牌仅存 SHA-256 哈希;设备可撤销(403);幂等键防重复效果。

## 已知限制

- PostgresSyncStore 为单连接 + 互斥(M7 单机规模足够;连接池随 M8 加固)。
- Docker 镜像构建依赖网络拉取 vcpkg 基线,首次较慢;本仓库 CI 未覆盖该路径
  (服务器二进制的 CI 构建随 M8 评估 runner 成本后决定)。
