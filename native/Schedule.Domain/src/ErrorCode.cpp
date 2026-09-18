#include <equora/domain/ErrorCode.h>

namespace equora::domain {

const char* toString(ErrorCode code) noexcept {
    switch (code) {
    case ErrorCode::Ok:              return "Ok";
    case ErrorCode::Unknown:         return "Unknown";
    case ErrorCode::InvalidArgument: return "InvalidArgument";
    case ErrorCode::NotFound:        return "NotFound";
    case ErrorCode::Conflict:        return "Conflict";
    case ErrorCode::StorageError:    return "StorageError";
    case ErrorCode::MigrationError:  return "MigrationError";
    case ErrorCode::IoError:         return "IoError";
    case ErrorCode::NotSupported:    return "NotSupported";
    }
    return "InvalidErrorCode";
}

} // namespace equora::domain
