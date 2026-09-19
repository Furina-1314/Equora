# 排程算法:重复展开、冲突检测、空闲搜索。纯函数,仅依赖 Domain。
add_library(equora_scheduling STATIC
    src/Recurrence.cpp
    src/Conflict.cpp
    src/FreeSlot.cpp
)

add_library(Equora::Scheduling ALIAS equora_scheduling)

target_include_directories(equora_scheduling PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_link_libraries(equora_scheduling PUBLIC Equora::Domain)
