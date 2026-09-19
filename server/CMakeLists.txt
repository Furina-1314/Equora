cmake_minimum_required(VERSION 3.24)

project(EquoraServer VERSION 0.1.0 LANGUAGES CXX
        DESCRIPTION "Equora sync server (Drogon + PostgreSQL) and protocol core")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    add_compile_options(/utf-8 /W4 /permissive-)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

option(EQUORA_BUILD_SERVER "Build the Drogon HTTP server (needs drogon+libpq)" OFF)

add_subdirectory(Equora.SyncCore)

enable_testing()
add_subdirectory(Equora.Server.Tests)

if(EQUORA_BUILD_SERVER)
    add_subdirectory(Equora.Server)
endif()
