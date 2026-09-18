#include <equora/core/Transfer.h>

#include <nlohmann/json.hpp>

#include <equora/domain/Error.h>
#include <equora/domain/Time.h>

namespace equora::core {

using domain::ErrorCode;
using domain::EquoraError;
using domain::Task;
using domain::UtcMillis;

namespace {

[[nodiscard]] std::string isoOrNull(const std::optional<UtcMillis>& t) {
    return t.has_value() ? domain::utc::toIso8601(*t) : std::string();
}

[[nodiscard]] std::optional<UtcMillis> parseIso(const std::string& text) {
    if (text.empty()) return std::nullopt;
    return domain::utc::parseIso8601(text);
}

// CSV 字段转义:仅必要时加引号,内部引号翻倍。
void appendCsvField(std::string& out, std::string_view value) {
    const bool needsQuote = value.find_first_of(",\"\n\r") != std::string_view::npos;
    if (!needsQuote) {
        out.append(value);
        return;
    }
    out.push_back('"');
    for (const char c : value) {
        if (c == '"') out.push_back('"');
        out.push_back(c);
    }
    out.push_back('"');
}

[[nodiscard]] domain::TaskStatus readStatus(long v) {
    return static_cast<domain::TaskStatus>(v);
}
[[nodiscard]] domain::Priority readPriority(long v) {
    return static_cast<domain::Priority>(v);
}

} // namespace

std::string exportTasksJson(const TaskRepository& repo) {
    nlohmann::json tasks = nlohmann::json::array();
    for (const Task& t : repo.listAll(/*includeDeleted=*/false)) {
        tasks.push_back({
            {"id", t.id},
            {"title", t.title},
            {"note", t.note},
            {"status", static_cast<std::int64_t>(t.status)},
            {"priority", static_cast<std::int64_t>(t.priority)},
            {"importance", t.importance},
            {"due_at", isoOrNull(t.dueAt)},
            {"estimate_minutes", t.estimateMinutes.has_value()
                                     ? nlohmann::json(*t.estimateMinutes)
                                     : nlohmann::json(nullptr)},
            {"actual_minutes", t.actualMinutes},
            {"project_id", t.projectId.has_value() ? nlohmann::json(*t.projectId)
                                                   : nlohmann::json(nullptr)},
            {"created_at", domain::utc::toIso8601(t.createdAt)},
            {"updated_at", domain::utc::toIso8601(t.updatedAt)},
            {"revision", t.revision},
            {"last_device_id", t.lastDeviceId},
        });
    }

    nlohmann::json root = {
        {"format", "equora.tasks"},
        {"version", 1},
        {"exported_at", domain::utc::toIso8601(domain::utc::now())},
        {"tasks", std::move(tasks)},
    };
    return root.dump(2);
}

std::string exportTasksCsv(const TaskRepository& repo) {
    std::string out;
    out.append("\xEF\xBB\xBF"); // UTF-8 BOM
    out.append("id,title,note,status,priority,importance,due_at,estimate_minutes,"
               "actual_minutes,project_id,created_at,updated_at,revision\n");

    for (const Task& t : repo.listAll(/*includeDeleted=*/false)) {
        appendCsvField(out, t.id);
        out.push_back(',');
        appendCsvField(out, t.title);
        out.push_back(',');
        appendCsvField(out, t.note);
        out.push_back(',');
        out.append(std::to_string(static_cast<int>(t.status)));
        out.push_back(',');
        out.append(std::to_string(static_cast<int>(t.priority)));
        out.push_back(',');
        out.append(std::to_string(t.importance));
        out.push_back(',');
        appendCsvField(out, isoOrNull(t.dueAt));
        out.push_back(',');
        appendCsvField(out, t.estimateMinutes.has_value()
                                ? std::to_string(*t.estimateMinutes)
                                : std::string());
        out.push_back(',');
        out.append(std::to_string(t.actualMinutes));
        out.push_back(',');
        appendCsvField(out, t.projectId.value_or(""));
        out.push_back(',');
        appendCsvField(out, domain::utc::toIso8601(t.createdAt));
        out.push_back(',');
        appendCsvField(out, domain::utc::toIso8601(t.updatedAt));
        out.push_back(',');
        out.append(std::to_string(t.revision));
        out.push_back('\n');
    }
    return out;
}

ImportResult importTasksJson(const TaskRepository& repo, std::string_view jsonText) {
    nlohmann::json root;
    try {
        root = nlohmann::json::parse(jsonText);
    } catch (const nlohmann::json::parse_error& e) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          std::string("invalid JSON: ") + e.what());
    }
    if (!root.is_object() || !root.contains("tasks") || !root["tasks"].is_array()) {
        throw EquoraError(ErrorCode::InvalidArgument,
                          "expected {\"format\":\"equora.tasks\",...,\"tasks\":[...]}");
    }

    ImportResult result;
    for (const auto& j : root["tasks"]) {
        try {
            if (!j.is_object()) throw std::runtime_error("entry is not an object");

            Task t;
            t.id = j.at("id").get<std::string>();
            t.title = j.at("title").get<std::string>();
            t.note = j.value("note", std::string());
            t.status = readStatus(j.value("status", 0));
            t.priority = readPriority(j.value("priority", 2));
            t.importance = j.value("importance", 0);
            t.dueAt = parseIso(j.value("due_at", std::string()));
            t.estimateMinutes = j.contains("estimate_minutes") && j["estimate_minutes"].is_number_integer()
                                    ? std::optional<std::int32_t>(
                                          j["estimate_minutes"].get<std::int32_t>())
                                    : std::nullopt;
            t.actualMinutes = j.value("actual_minutes", 0);
            t.projectId = j.contains("project_id") && j["project_id"].is_string()
                              ? std::optional<std::string>(j["project_id"].get<std::string>())
                              : std::nullopt;
            t.createdAt = domain::utc::parseIso8601(j.at("created_at").get<std::string>())
                              .value_or(0);
            t.updatedAt = domain::utc::parseIso8601(j.at("updated_at").get<std::string>())
                              .value_or(0);
            t.revision = j.value("revision", static_cast<std::int64_t>(1));
            t.deletedAt = std::nullopt;
            t.lastDeviceId = j.value("last_device_id", std::string());

            if (repo.findById(t.id, /*includeDeleted=*/true).has_value()) {
                ++result.skipped;
                continue;
            }
            (void)repo.importTask(std::move(t));
            ++result.imported;
        } catch (const EquoraError&) {
            ++result.skipped; // 单条非法/冲突:计入 skipped,继续
        } catch (const std::exception&) {
            ++result.skipped;
        }
    }
    return result;
}

} // namespace equora::core
