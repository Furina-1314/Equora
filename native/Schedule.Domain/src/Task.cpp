#include <equora/domain/Task.h>

namespace equora::domain {

Task Task::draft(std::string title) {
    Task t;
    t.title = std::move(title);
    return t;
}

} // namespace equora::domain
