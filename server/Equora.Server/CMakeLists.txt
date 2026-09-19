# Drogon HTTP 服务 + PostgreSQL 存储(重依赖:drogon / libpq / openssl)。
# 仅在 EQUORA_BUILD_SERVER=ON 时纳入(独立 preset win-x64-server)。
find_package(Drogon CONFIG REQUIRED)
find_package(PostgreSQL REQUIRED) # libpq(libpq-fe.h + libpq)

add_executable(equora_server
    src/main.cpp
    src/PostgresSyncStore.cpp
)

target_include_directories(equora_server PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${PostgreSQL_INCLUDE_DIRS}
)

target_link_libraries(equora_server PRIVATE
    Equora::SyncCore
    Drogon::Drogon
    ${PostgreSQL_LIBRARIES}
)

set_target_properties(equora_server PROPERTIES OUTPUT_NAME "equora-server")
