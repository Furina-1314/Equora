-- Equora 同步服务端迁移 001:账号、设备、实体镜像、变更流、幂等登记。
-- 所有时间用 TIMESTAMPTZ(UTC);payload 用 JSONB。

CREATE TABLE users (
    id              TEXT PRIMARY KEY,
    auth_token_hash TEXT NOT NULL UNIQUE,   -- SHA-256(token) 十六进制;不存明文令牌
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE devices (
    id           TEXT PRIMARY KEY,
    user_id      TEXT NOT NULL REFERENCES users (id),
    name         TEXT NOT NULL DEFAULT '',
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_sync_at TIMESTAMPTZ,
    revoked      BOOLEAN NOT NULL DEFAULT false
);
CREATE INDEX idx_devices_user ON devices (user_id);

CREATE TABLE sync_entities (
    user_id     TEXT    NOT NULL,
    entity_type TEXT    NOT NULL,
    entity_id   TEXT    NOT NULL,
    revision    BIGINT  NOT NULL,
    deleted     BOOLEAN NOT NULL DEFAULT false,
    payload     JSONB   NOT NULL DEFAULT '{}',
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, entity_type, entity_id)
);

-- 有序变更流:全局 BIGSERIAL 保证单调,按用户过滤即为用户内单调游标。
CREATE TABLE change_log (
    seq         BIGSERIAL PRIMARY KEY,
    user_id     TEXT    NOT NULL,
    entity_type TEXT    NOT NULL,
    entity_id   TEXT    NOT NULL,
    revision    BIGINT  NOT NULL,
    kind        SMALLINT NOT NULL,          -- 0 create / 1 update / 2 delete
    payload     JSONB   NOT NULL DEFAULT '{}',
    device_id   TEXT    NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_change_log_user ON change_log (user_id, seq);

-- 幂等登记:同 operationId 重放直接命中,返回原结果。
CREATE TABLE processed_ops (
    user_id          TEXT   NOT NULL,
    operation_id     TEXT   NOT NULL,
    result           SMALLINT NOT NULL,
    new_revision     BIGINT NOT NULL DEFAULT 0,
    server_revision  BIGINT NOT NULL DEFAULT 0,
    server_deleted   BOOLEAN NOT NULL DEFAULT false,
    server_payload   JSONB  NOT NULL DEFAULT '{}',
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, operation_id)
);
