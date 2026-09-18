# 领域核心:纯 C++,不依赖 SQLite / WinUI / 网络库。
add_library(equora_domain STATIC
    src/ErrorCode.cpp
    src/Uuid.cpp
    src/Time.cpp
    src/Task.cpp
    src/Project.cpp
    src/Tag.cpp
)

add_library(Equora::Domain ALIAS equora_domain)

target_include_directories(equora_domain PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
)

target_compile_features(equora_domain PUBLIC cxx_std_20)
