#include <equora/domain/Calendar.h>

namespace equora::domain {

Calendar Calendar::draft(std::string name) {
    Calendar c;
    c.name = std::move(name);
    return c;
}

} // namespace equora::domain
