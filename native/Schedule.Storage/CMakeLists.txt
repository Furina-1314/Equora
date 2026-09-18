# 存储层:SQLite 访问、事务、版本化迁移。仅依赖 Domain。
find_package(unofficial-sqlite3 CONFIG REQUIRED)

add_library(equora_storage STATIC
    src/Database.cpp
    src/Migrations.cpp
)

add_library(Equora::Storage ALIAS equora_storage)

target_include_directories(equora_storage PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_link_libraries(equora_storage
    PUBLIC Equora::Domain
    PRIVATE unofficial::sqlite3::sqlite3
)
