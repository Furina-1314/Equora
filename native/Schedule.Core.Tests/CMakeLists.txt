# 单元测试:GoogleTest,覆盖 Common / Domain / Storage / Core。
find_package(GTest CONFIG REQUIRED)

add_executable(equora_core_tests
    src/UuidTests.cpp
    src/TimeTests.cpp
    src/Sha256Tests.cpp
    src/LoggerTests.cpp
    src/MigrationTests.cpp
    src/MigrationV2Tests.cpp
    src/TaskRepositoryTests.cpp
    src/ProjectRepositoryTests.cpp
    src/TagRepositoryTests.cpp
    src/ChecklistRepositoryTests.cpp
    src/TaskQueryTests.cpp
    src/BackupTests.cpp
    src/TransferTests.cpp
    src/RecurrenceTests.cpp
    src/CalendarRepoTests.cpp
    src/FocusSessionTests.cpp
    src/PlannerTests.cpp
    src/ReviewTests.cpp
    src/SyncClientTests.cpp
    src/PerfTests.cpp
    src/FaultInjectionTests.cpp
)

target_link_libraries(equora_core_tests PRIVATE
    Equora::Core
    Equora::Common
    Equora::SyncClient
    GTest::gtest_main
)

target_compile_definitions(equora_core_tests PRIVATE
    EQUORA_TEST_TMPDIR="${CMAKE_CURRENT_BINARY_DIR}/tmp"
)

include(GoogleTest)
gtest_discover_tests(equora_core_tests)
