#include <equora/domain/Tag.h>

namespace equora::domain {

Tag Tag::draft(std::string name) {
    Tag t;
    t.name = std::move(name);
    return t;
}

} // namespace equora::domain
