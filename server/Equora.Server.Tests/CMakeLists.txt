find_package(GTest CONFIG REQUIRED)

add_executable(equora_server_tests
    src/SyncServiceTests.cpp
)

target_link_libraries(equora_server_tests PRIVATE
    Equora::SyncCore
    GTest::gtest_main
)

include(GoogleTest)
gtest_discover_tests(equora_server_tests)
