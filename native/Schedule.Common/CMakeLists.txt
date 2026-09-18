# 跨模块基础设施工具:日志与校验和。仅依赖 Domain(时间格式化)。
add_library(equora_common STATIC
    src/Sha256.cpp
    src/Logger.cpp
)

add_library(Equora::Common ALIAS equora_common)

target_include_directories(equora_common PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_link_libraries(equora_common
    PUBLIC Equora::Domain
)
