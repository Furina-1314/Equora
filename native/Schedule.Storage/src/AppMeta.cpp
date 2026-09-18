#include <equora/storage/AppMeta.h>

#include <equora/domain/Uuid.h>

namespace equora::storage {

std::optional<std::string> getAppMeta(const Database& db, std::string_view key) {
    auto st = db.prepare("SELECT value FROM app_meta WHERE key = ?");
    st.bind(1, key);
    if (!st.step()) return std::nullopt;
    return st.columnText(0);
}

void setAppMeta(const Database& db, std::string_view key, std::string_view value) {
    Transaction tx = db.beginTransaction();
    {
        auto st = db.prepare(
            "INSERT INTO app_meta (key, value) VALUES (?, ?) "
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value");
        st.bind(1, key).bind(2, value);
        st.step();
    }
    tx.commit();
}

std::string getOrCreateDeviceId(const Database& db) {
    if (auto existing = getAppMeta(db, "device_id")) {
        if (!existing->empty()) return *existing;
    }
    const std::string id = domain::Uuid::random().toString();
    setAppMeta(db, "device_id", id);
    return id;
}

} // namespace equora::storage
