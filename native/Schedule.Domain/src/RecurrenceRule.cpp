#include <equora/domain/RecurrenceRule.h>

#include <algorithm>
#include <cstdio>
#include <sstream>

namespace equora::domain {

namespace {

const char* weekdayCode(Weekday w) {
    switch (w) {
    case Weekday::Mon: return "MO";
    case Weekday::Tue: return "TU";
    case Weekday::Wed: return "WE";
    case Weekday::Thu: return "TH";
    case Weekday::Fri: return "FR";
    case Weekday::Sat: return "SA";
    case Weekday::Sun: return "SU";
    }
    return "MO";
}

std::optional<Weekday> parseWeekday(std::string_view s) {
    if (s == "MO") return Weekday::Mon;
    if (s == "TU") return Weekday::Tue;
    if (s == "WE") return Weekday::Wed;
    if (s == "TH") return Weekday::Thu;
    if (s == "FR") return Weekday::Fri;
    if (s == "SA") return Weekday::Sat;
    if (s == "SU") return Weekday::Sun;
    return std::nullopt;
}

std::vector<std::string> splitBy(std::string_view s, char sep) {
    std::vector<std::string> out;
    std::size_t pos = 0;
    while (pos <= s.size()) {
        const std::size_t next = s.find(sep, pos);
        const auto part = s.substr(pos, next == std::string_view::npos ? s.size() - pos : next - pos);
        if (!part.empty()) out.emplace_back(part);
        if (next == std::string_view::npos) break;
        pos = next + 1;
    }
    return out;
}

} // namespace

std::string RecurrenceRule::toRruleString() const {
    std::ostringstream out;
    out << "FREQ=";
    switch (freq) {
    case RecurFreq::Daily:   out << "DAILY"; break;
    case RecurFreq::Weekly:  out << "WEEKLY"; break;
    case RecurFreq::Monthly: out << "MONTHLY"; break;
    case RecurFreq::Yearly:  out << "YEARLY"; break;
    }
    if (interval > 1) out << ";INTERVAL=" << interval;
    if (freq == RecurFreq::Weekly && !byWeekday.empty()) {
        out << ";BYDAY=";
        for (std::size_t i = 0; i < byWeekday.size(); ++i) {
            if (i > 0) out << ",";
            out << weekdayCode(byWeekday[i]);
        }
    }
    if (freq == RecurFreq::Monthly && monthMode == MonthMode::NthWeekday) {
        out << ";BYDAY=" << monthNth << weekdayCode(monthWeekday);
    }
    if (freq == RecurFreq::Monthly && monthMode == MonthMode::LastWeekday) {
        out << ";BYDAY=-1" << weekdayCode(monthWeekday);
    }
    if (untilUtc.has_value()) out << ";UNTIL=" << utc::toIso8601(*untilUtc);
    if (maxCount.has_value()) out << ";COUNT=" << *maxCount;
    return out.str();
}

std::optional<RecurrenceRule> RecurrenceRule::parseRrule(std::string_view rrule,
                                                         std::string* whyNot) {
    auto fail = [&](const char* msg) {
        if (whyNot != nullptr) *whyNot = msg;
        return std::optional<RecurrenceRule>();
    };

    RecurrenceRule rule;
    bool hasFreq = false;
    for (const auto& token : splitBy(rrule, ';')) {
        const auto eq = token.find('=');
        if (eq == std::string::npos) return fail("malformed part (missing '=')");
        const std::string key = token.substr(0, eq);
        const std::string value = token.substr(eq + 1);

        if (key == "FREQ") {
            hasFreq = true;
            if (value == "DAILY") rule.freq = RecurFreq::Daily;
            else if (value == "WEEKLY") rule.freq = RecurFreq::Weekly;
            else if (value == "MONTHLY") rule.freq = RecurFreq::Monthly;
            else if (value == "YEARLY") rule.freq = RecurFreq::Yearly;
            else return fail("unsupported FREQ");
        } else if (key == "INTERVAL") {
            rule.interval = std::max(0, atoi(value.c_str()));
            if (rule.interval < 1) return fail("invalid INTERVAL");
        } else if (key == "BYDAY") {
            for (const auto& day : splitBy(value, ',')) {
                std::string_view sv = day;
                std::optional<Weekday> wd;
                if (sv.size() > 2) {
                    // 数字前缀(MONTHLY 的 nth):±nXX。
                    const std::string suffix = std::string(sv.substr(sv.size() - 2));
                    wd = parseWeekday(suffix);
                    const int nth = atoi(std::string(sv.substr(0, sv.size() - 2)).c_str());
                    if (!wd.has_value()) return fail("invalid BYDAY");
                    if (rule.freq == RecurFreq::Monthly) {
                        if (nth == -1) {
                            rule.monthMode = MonthMode::LastWeekday;
                        } else if (nth >= 1 && nth <= 5) {
                            rule.monthMode = MonthMode::NthWeekday;
                            rule.monthNth = nth;
                        } else {
                            return fail("unsupported BYDAY ordinal");
                        }
                        rule.monthWeekday = *wd;
                    }
                    continue;
                }
                wd = parseWeekday(sv);
                if (!wd.has_value()) return fail("invalid BYDAY");
                rule.byWeekday.push_back(*wd);
            }
        } else if (key == "UNTIL") {
            auto parsed = utc::parseIso8601(value);
            if (!parsed.has_value()) return fail("invalid UNTIL");
            rule.untilUtc = parsed;
        } else if (key == "COUNT") {
            const long long n = atoll(value.c_str());
            if (n < 1) return fail("invalid COUNT");
            rule.maxCount = n;
        } else {
            return fail("unsupported part");
        }
    }

    if (!hasFreq) return fail("missing FREQ");
    return rule;
}

} // namespace equora::domain
