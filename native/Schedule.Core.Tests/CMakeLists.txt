# 单元测试:GoogleTest,覆盖 Domain / Storage / Core。
find_package(GTest CONFIG REQUIRED)

add_executable(equora_core_tests
    src/UuidTests.cpp
    src/TimeTests.cpp
    src/MigrationTests.cpp
    src/TaskRepositoryTests.cpp
)

target_link_libraries(equora_core_tests PRIVATE
    Equora::Core
    GTest::gtest_main
)

target_compile_definitions(equora_core_tests PRIVATE
    EQUORA_TEST_TMPDIR="${CMAKE_CURRENT_BINARY_DIR}/tmp"
)

include(GoogleTest)
gtest_discover_tests(equora_core_tests)
