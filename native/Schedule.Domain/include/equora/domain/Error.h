#pragma once

#include <stdexcept>
#include <string>
#include <string_view>

#include <equora/domain/ErrorCode.h>

namespace equora::domain {

// 统一异常基类:跨 C ABI 边界前必须被捕获并转换为错误对象。
class EquoraError : public std::runtime_error {
public:
    EquoraError(ErrorCode code, std::string message)
        : std::runtime_error(std::move(message)), code_(code) {}

    ErrorCode code() const noexcept { return code_; }

private:
    ErrorCode code_;
};

} // namespace equora::domain
