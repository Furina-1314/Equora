# 核心用例层:仓库、查询与业务规则。
find_package(nlohmann_json CONFIG REQUIRED)

add_library(equora_core STATIC
    src/TaskRepository.cpp
    src/CalendarRepository.cpp
    src/FocusRepository.cpp
    src/TransferIcs.cpp
    src/ProjectRepository.cpp
    src/TagRepository.cpp
    src/ChecklistRepository.cpp
    src/SmartLists.cpp
    src/Transfer.cpp
)

add_library(Equora::Core ALIAS equora_core)

target_include_directories(equora_core PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_link_libraries(equora_core
    PUBLIC Equora::Domain
           Equora::Storage
           Equora::Common
           Equora::Scheduling
    PRIVATE nlohmann_json::nlohmann_json
)
