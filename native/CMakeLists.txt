cmake_minimum_required(VERSION 3.24)

project(EquoraNative VERSION 0.1.0 LANGUAGES C CXX
        DESCRIPTION "Equora native core (C++20): domain, storage, scheduling, focus, analytics")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# $<TARGET_RUNTIME_DLLS> 需要 CMP0139(递归收集 DLL 依赖)。
if(POLICY CMP0139)
    cmake_policy(SET CMP0139 NEW)
endif()

if(MSVC)
    add_compile_options(/utf-8 /W4 /permissive-)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

option(EQUORA_BUILD_TESTS "Build Equora unit tests" ON)

# 模块依赖方向(禁止循环):
# Domain <- Common <- Storage <- Scheduling <- Core <- CApi <- (tests)
add_subdirectory(Schedule.Domain)
add_subdirectory(Schedule.Common)
add_subdirectory(Schedule.Storage)
add_subdirectory(Schedule.Scheduling)
add_subdirectory(Schedule.Core)
add_subdirectory(Schedule.Sync)
add_subdirectory(Schedule.CApi)

if(EQUORA_BUILD_TESTS)
    enable_testing()
    add_subdirectory(Schedule.Core.Tests)
    add_subdirectory(Schedule.CApi.Tests)
endif()
