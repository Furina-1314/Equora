# 同步协议核心:纯逻辑 + 内存存储(生产存储在 Equora.Server 内以 PostgreSQL 实现)。
add_library(equora_synccore STATIC
    src/SyncService.cpp
    src/MemorySyncStore.cpp
)

add_library(Equora::SyncCore ALIAS equora_synccore)

target_include_directories(equora_synccore PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_compile_features(equora_synccore PUBLIC cxx_std_20)
