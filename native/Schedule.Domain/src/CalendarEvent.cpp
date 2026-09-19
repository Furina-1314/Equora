#include <equora/domain/CalendarEvent.h>

namespace equora::domain {

CalendarEvent CalendarEvent::draft(std::string title) {
    CalendarEvent e;
    e.title = std::move(title);
    return e;
}

} // namespace equora::domain
