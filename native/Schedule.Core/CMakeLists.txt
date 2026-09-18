# 核心用例层:仓库与业务规则。仅依赖 Domain 与 Storage 抽象。
add_library(equora_core STATIC
    src/TaskRepository.cpp
)

add_library(Equora::Core ALIAS equora_core)

target_include_directories(equora_core PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_link_libraries(equora_core
    PUBLIC Equora::Domain
           Equora::Storage
)
