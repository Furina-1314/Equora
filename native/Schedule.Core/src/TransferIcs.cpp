#include <equora/core/TransferIcs.h>

#include <algorithm>
#include <map>
#include <sstream>

#include <equora/domain/Error.h>
#include <equora/domain/Time.h>

namespace equora::core {

using domain::ErrorCode;
using domain::EquoraError;
using domain::utc::now;
using domain::utc::parseIso8601;
using domain::utc::toIso8601;

namespace {

// RFC5545:续行以单个空格/制表符开头。
[[nodiscard]] std::vector<std::string> unfoldLines(std::string_view text) {
    std::vector<std::string> lines;
    std::string current;
    std::size_t i = 0;
    while (i <= text.size()) {
        const std::size_t nl = text.find('\n', i);
        std::string_view line =
            text.substr(i, nl == std::string_view::npos ? text.size() - i : nl - i);
        if (!line.empty() && line.back() == '\r') line.remove_suffix(1);

        if (!line.empty() && (line[0] == ' ' || line[0] == '\t')) {
            current.append(line.substr(1));
        } else {
            if (!current.empty()) lines.push_back(std::move(current));
            current.assign(line);
        }
        if (nl == std::string::npos) break;
        i = nl + 1;
    }
    if (!current.empty()) lines.push_back(std::move(current));
    return lines;
}

// ISO-UTC(带 Z)或 ICS 基本格式(20260919T080000Z)。
[[nodiscard]] std::optional<domain::UtcMillis> parseIcsTime(const std::string& v) {
    if (v.find('-') != std::string::npos) return parseIso8601(v);
    if (v.size() == 16 && v.back() == 'Z') {
        const std::string iso = v.substr(0, 4) + "-" + v.substr(4, 2) + "-" + v.substr(6, 2) +
                                "T" + v.substr(9, 2) + ":" + v.substr(11, 2) + ":" +
                                v.substr(13, 2) + ".000Z";
        return parseIso8601(iso);
    }
    return std::nullopt;
}

[[nodiscard]] std::string toIcsTime(domain::UtcMillis t) {
    const std::string iso = toIso8601(t);
    return iso.substr(0, 4) + iso.substr(5, 2) + iso.substr(8, 2) + "T" + iso.substr(11, 2) +
           iso.substr(14, 2) + iso.substr(17, 2) + "Z";
}

[[nodiscard]] std::string escapeText(const std::string& s) {
    std::string out;
    for (const char c : s) {
        switch (c) {
        case '\\': out += "\\\\"; break;
        case ';':  out += "\\;"; break;
        case ',':  out += "\\,"; break;
        case '\n': out += "\\n"; break;
        case '\r': break;
        default:   out.push_back(c);
        }
    }
    return out;
}

[[nodiscard]] std::string unescapeText(const std::string& s) {
    std::string out;
    for (std::size_t i = 0; i < s.size(); ++i) {
        if (s[i] == '\\' && i + 1 < s.size()) {
            const char n = s[++i];
            if (n == 'n' || n == 'N') out.push_back('\n');
            else out.push_back(n);
        } else {
            out.push_back(s[i]);
        }
    }
    return out;
}

} // namespace

std::string exportIcs(const CalendarRepository& repo, domain::UtcMillis from,
                      domain::UtcMillis to) {
    std::ostringstream out;
    out << "BEGIN:VCALENDAR\r\n"
        << "VERSION:2.0\r\n"
        << "PRODID:-//Equora//HengXu 0.2//CN\r\n"
        << "CALSCALE:GREGORIAN\r\n";

    const auto stamp = toIcsTime(now());
    for (const auto& e : repo.eventsInRange(from, to)) {
        out << "BEGIN:VEVENT\r\n"
            << "UID:" << e.id << "\r\n"
            << "DTSTAMP:" << stamp << "\r\n"
            << "DTSTART:" << toIcsTime(e.startAt) << "\r\n"
            << "DTEND:" << toIcsTime(e.endAt) << "\r\n"
            << "SUMMARY:" << escapeText(e.title) << "\r\n";
        if (!e.location.empty()) out << "LOCATION:" << escapeText(e.location) << "\r\n";
        if (!e.note.empty()) out << "DESCRIPTION:" << escapeText(e.note) << "\r\n";
        for (const auto& r : repo.rulesForHost("event", e.id)) {
            out << "RRULE:" << r.toRruleString() << "\r\n";
        }
        out << "END:VEVENT\r\n";
    }

    for (const auto& b : repo.blocksInRange(from, to)) {
        std::string title = b.title.empty() ? "专注块" : b.title;
        if (b.title.empty() && b.taskId.has_value() && !b.taskId->empty()) {
            title = "任务块 " + b.taskId->substr(0, 8);
        }
        out << "BEGIN:VEVENT\r\n"
            << "UID:" << b.id << "\r\n"
            << "DTSTAMP:" << stamp << "\r\n"
            << "DTSTART:" << toIcsTime(b.startAt) << "\r\n"
            << "DTEND:" << toIcsTime(b.endAt) << "\r\n"
            << "SUMMARY:" << escapeText(title) << "\r\n";
        if (!b.note.empty()) out << "DESCRIPTION:" << escapeText(b.note) << "\r\n";
        out << "RELATED-TO:" << b.taskId.value_or("") << "\r\n";
        out << "END:VEVENT\r\n";
    }

    out << "END:VCALENDAR\r\n";
    return out.str();
}

IcsImportResult importIcs(const CalendarRepository& repo, std::string_view icsText) {
    IcsImportResult result;
    const auto lines = unfoldLines(icsText);

    std::map<std::string, std::string> fields;
    auto flushEvent = [&]() {
        if (fields.empty()) return;
        if (fields.find("SUMMARY") == fields.end() ||
            fields.find("DTSTART") == fields.end()) {
            ++result.failed;
            fields.clear();
            return;
        }
        auto start = parseIcsTime(fields["DTSTART"]);
        auto end = fields.count("DTEND") != 0 ? parseIcsTime(fields["DTEND"]) : std::nullopt;
        if (!start.has_value()) {
            ++result.failed;
            fields.clear();
            return;
        }

        domain::CalendarEvent ev = domain::CalendarEvent::draft(unescapeText(fields["SUMMARY"]));
        ev.id = fields.count("UID") != 0 ? fields["UID"] : "";
        ev.startAt = *start;
        ev.endAt = end.value_or(*start + 3'600'000);
        ev.location = fields.count("LOCATION") != 0 ? unescapeText(fields["LOCATION"]) : "";
        ev.note = fields.count("DESCRIPTION") != 0 ? unescapeText(fields["DESCRIPTION"]) : "";

        if (repo.findEvent(ev.id, /*includeDeleted=*/true).has_value()) {
            ++result.skipped;
            fields.clear();
            return;
        }
        try {
            const auto created = repo.createEvent(ev);
            if (fields.count("RRULE") != 0) {
                std::string why;
                auto parsed = domain::RecurrenceRule::parseRrule(fields["RRULE"], &why);
                if (parsed.has_value()) {
                    parsed->hostType = "event";
                    parsed->hostId = created.id;
                    (void)repo.createRule(*parsed);
                }
            }
            ++result.imported;
        } catch (const EquoraError&) {
            ++result.skipped;
        }
        fields.clear();
    };

    for (const auto& line : lines) {
        if (line == "BEGIN:VEVENT") {
            fields.clear();
        } else if (line == "END:VEVENT") {
            flushEvent();
        } else if (!line.empty() && line != "BEGIN:VCALENDAR" && line != "END:VCALENDAR") {
            const auto colon = line.find(':');
            if (colon == std::string::npos) continue;
            std::string key = line.substr(0, colon);
            const auto semi = key.find(';');
            if (semi != std::string::npos) key = key.substr(0, semi);
            fields[key] = line.substr(colon + 1);
        }
    }
    return result;
}

} // namespace equora::core
