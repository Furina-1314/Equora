#include <equora/domain/Focus.h>

namespace equora::domain {

std::int64_t FocusSession::effectiveMs(UtcMillis now) const {
    const UtcMillis end = actualEnd.value_or(now);
    const std::int64_t raw = end - actualStart;
    return raw - pausedMs > 0 ? raw - pausedMs : 0;
}

FocusProfile FocusProfile::draft(std::string name) {
    FocusProfile p;
    p.name = std::move(name);
    return p;
}

} // namespace equora::domain
