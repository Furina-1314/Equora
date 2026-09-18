cmake_minimum_required(VERSION 3.24)

project(EquoraNative VERSION 0.1.0 LANGUAGES C CXX
        DESCRIPTION "Equora native core (C++20): domain, storage, scheduling, focus, analytics")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    add_compile_options(/utf-8 /W4 /permissive-)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

option(EQUORA_BUILD_TESTS "Build Equora unit tests" ON)

# 模块依赖方向(禁止循环):
# Domain <- Storage <- Core <- (CApi / Sync / Scheduling / Focus / Analytics)
add_subdirectory(Schedule.Domain)
add_subdirectory(Schedule.Storage)
add_subdirectory(Schedule.Core)

if(EQUORA_BUILD_TESTS)
    enable_testing()
    add_subdirectory(Schedule.Core.Tests)
endif()
