# 桌面端同步客户端:Outbox、退避、字段级合并。依赖 Storage 与 nlohmann。
find_package(nlohmann_json CONFIG REQUIRED)

add_library(equora_syncclient STATIC
    src/SyncClient.cpp
)

add_library(Equora::SyncClient ALIAS equora_syncclient)

target_include_directories(equora_syncclient PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_link_libraries(equora_syncclient
    PUBLIC Equora::Domain
           Equora::Storage
    PRIVATE nlohmann_json::nlohmann_json
)
