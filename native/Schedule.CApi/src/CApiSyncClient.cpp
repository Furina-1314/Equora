#include "CApiInternal.h"

// ---- 句柄(全局作用域) ----

struct EqOutboxList {
    std::vector<equora::sync::OutboxEntry> items;
    std::vector<EqOutboxView> views;
    std::vector<std::unique_ptr<std::string>> strings;
};

struct EqSyncConflictList {
    std::vector<equora::sync::ConflictEntry> items;
    std::vector<EqSyncConflictView> views;
    std::vector<std::unique_ptr<std::string>> strings;
};

namespace {

using namespace equora;
using namespace equora::capi;

void fillOutboxViews(EqOutboxList& list) {
    list.strings.clear();
    list.views.clear();
    list.strings.reserve(list.items.size() * 4);
    for (const auto& e : list.items) {
        list.strings.push_back(std::make_unique<std::string>(e.operationId));
        list.strings.push_back(std::make_unique<std::string>(e.entityType));
        list.strings.push_back(std::make_unique<std::string>(e.entityId));
        list.strings.push_back(std::make_unique<std::string>(e.payload));
    }
    list.views.reserve(list.items.size());
    for (std::size_t i = 0; i < list.items.size(); ++i) {
        const auto& e = list.items[i];
        EqOutboxView v{};
        v.row_id = e.rowId;
        v.operation_id = list.strings[i * 4]->c_str();
        v.entity_type = list.strings[i * 4 + 1]->c_str();
        v.entity_id = list.strings[i * 4 + 2]->c_str();
        v.base_revision = e.baseRevision;
        v.kind = e.kind;
        v.payload = list.strings[i * 4 + 3]->c_str();
        v.status = static_cast<std::int32_t>(e.status);
        v.attempts = e.attempts;
        v.next_attempt_at = e.nextAttemptAtMs;
        list.views.push_back(v);
    }
}

void fillConflictViews(EqSyncConflictList& list) {
    list.strings.clear();
    list.views.clear();
    list.strings.reserve(list.items.size() * 6);
    for (const auto& c : list.items) {
        list.strings.push_back(std::make_unique<std::string>(c.operationId));
        list.strings.push_back(std::make_unique<std::string>(c.entityType));
        list.strings.push_back(std::make_unique<std::string>(c.entityId));
        list.strings.push_back(std::make_unique<std::string>(c.localPayload));
        list.strings.push_back(std::make_unique<std::string>(c.serverPayload));
    }
    list.views.reserve(list.items.size());
    for (std::size_t i = 0; i < list.items.size(); ++i) {
        const auto& c = list.items[i];
        EqSyncConflictView v{};
        v.row_id = c.rowId;
        v.operation_id = list.strings[i * 5]->c_str();
        v.entity_type = list.strings[i * 5 + 1]->c_str();
        v.entity_id = list.strings[i * 5 + 2]->c_str();
        v.local_payload = list.strings[i * 5 + 3]->c_str();
        v.server_payload = list.strings[i * 5 + 4]->c_str();
        v.server_revision = c.serverRevision;
        v.server_deleted = c.serverDeleted ? 1 : 0;
        v.created_at = c.createdAtMs;
        v.resolution = c.resolution;
        list.views.push_back(v);
    }
}

} // namespace

// ---- Outbox ----

int32_t eq_outbox_enqueue(EqCore* core, const EqOutboxInput* input,
                          EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || input == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/input must not be null");
        }
        (void)core->sync.enqueue(
            input->operation_id != nullptr ? input->operation_id : "",
            input->entity_type != nullptr ? input->entity_type : "",
            input->entity_id != nullptr ? input->entity_id : "",
            input->base_revision, input->kind,
            input->payload != nullptr ? input->payload : "{}",
            domain::utc::now());
        return 0;
    });
}

int32_t eq_outbox_due_pending(EqCore* core, int64_t now_ms, int32_t limit,
                              EqOutboxList** out_list, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqOutboxList>();
        list->items = core->sync.duePending(now_ms, limit > 0 ? limit : 64);
        fillOutboxViews(*list);
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_outbox_mark_sending(EqCore* core, int64_t row_id, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        core->sync.markSending(row_id);
        return 0;
    });
}

int32_t eq_outbox_mark_result(EqCore* core, int64_t row_id, int32_t accepted,
                              int64_t now_ms, int32_t max_attempts,
                              EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        core->sync.markResult(row_id, accepted != 0, now_ms,
                              max_attempts > 0 ? max_attempts : 10);
        return 0;
    });
}

int32_t eq_outbox_reset_stuck(EqCore* core, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        core->sync.resetStuck(domain::utc::now());
        return 0;
    });
}

int32_t eq_outbox_pending_count(EqCore* core, int32_t* out_count,
                                EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_count == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_count must not be null");
        }
        *out_count = static_cast<std::int32_t>(core->sync.pendingCount());
        return 0;
    });
}

int32_t eq_outbox_list_count(const EqOutboxList* list) {
    return list != nullptr ? static_cast<int32_t>(list->views.size()) : 0;
}

const EqOutboxView* eq_outbox_list_get(const EqOutboxList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->views.size()) {
        return nullptr;
    }
    return &list->views[static_cast<std::size_t>(index)];
}

void eq_outbox_list_destroy(EqOutboxList* list) { delete list; }

// ---- 状态 ----

int32_t eq_sync_state(EqCore* core, EqSyncStateView* out, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out must not be null");
        }
        const auto s = core->sync.loadState();
        static thread_local std::string url;
        static thread_local std::string device;
        url = s.serverUrl;
        device = s.deviceId;
        out->cursor = s.cursor;
        out->server_url = url.c_str();
        out->device_id = device.c_str();
        out->last_success_at = s.lastSuccessAtMs;
        return 0;
    });
}

int32_t eq_sync_save_cursor(EqCore* core, int64_t cursor, int64_t now_ms,
                            EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        core->sync.saveCursor(cursor, now_ms);
        return 0;
    });
}

int32_t eq_sync_save_url(EqCore* core, const char* url_utf8, EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || url_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/url must not be null");
        }
        core->sync.saveServerUrl(url_utf8);
        return 0;
    });
}

// ---- 冲突 ----

int32_t eq_sync_conflict_add(EqCore* core, const char* operation_id_utf8,
                        const char* entity_type_utf8, const char* entity_id_utf8,
                        const char* local_payload_utf8,
                        const char* server_payload_utf8, int64_t server_revision,
                        int32_t server_deleted, int64_t now_ms,
                        EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || operation_id_utf8 == nullptr || entity_type_utf8 == nullptr ||
            entity_id_utf8 == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/operation_id/entity_* must not be null");
        }
        core->sync.addConflict(
            operation_id_utf8, entity_type_utf8, entity_id_utf8,
            local_payload_utf8 != nullptr ? local_payload_utf8 : "{}",
            server_payload_utf8 != nullptr ? server_payload_utf8 : "{}",
            server_revision, server_deleted != 0, now_ms);
        return 0;
    });
}

int32_t eq_sync_conflict_open(EqCore* core, EqSyncConflictList** out_list,
                         EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr || out_list == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core/out_list must not be null");
        }
        auto list = std::make_unique<EqSyncConflictList>();
        list->items = core->sync.openConflicts();
        fillConflictViews(*list);
        *out_list = list.release();
        return 0;
    });
}

int32_t eq_sync_conflict_resolve(EqCore* core, int64_t row_id, int32_t resolution,
                            EqError* out_error) {
    using namespace equora;
    using namespace equora::capi;
    return guard(out_error, [&]() -> int32_t {
        if (core == nullptr) {
            return fillError(out_error, static_cast<int32_t>(ErrorCode::InvalidArgument),
                             "core must not be null");
        }
        core->sync.resolveConflict(row_id, resolution);
        return 0;
    });
}

int32_t eq_sync_conflict_list_count(const EqSyncConflictList* list) {
    return list != nullptr ? static_cast<int32_t>(list->views.size()) : 0;
}

const EqSyncConflictView* eq_sync_conflict_list_get(const EqSyncConflictList* list, int32_t index) {
    if (list == nullptr || index < 0 ||
        static_cast<std::size_t>(index) >= list->views.size()) {
        return nullptr;
    }
    return &list->views[static_cast<std::size_t>(index)];
}

void eq_sync_conflict_list_destroy(EqSyncConflictList* list) { delete list; }

int32_t eq_sync_backoff_delay(int32_t attempts, int64_t base_ms, int64_t max_ms,
                              double jitter, int64_t* out_ms) {
    if (out_ms == nullptr) return static_cast<std::int32_t>(domain::ErrorCode::InvalidArgument);
    *out_ms = equora::sync::backoffDelayMs(attempts, base_ms, max_ms, jitter);
    return 0;
}
