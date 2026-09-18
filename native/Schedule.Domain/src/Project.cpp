#include <equora/domain/Project.h>

namespace equora::domain {

Project Project::draft(std::string name) {
    Project p;
    p.name = std::move(name);
    return p;
}

} // namespace equora::domain
