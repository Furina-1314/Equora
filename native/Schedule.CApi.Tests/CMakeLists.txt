# C ABI 边界测试:内存所有权、错误对象与异常边界。
find_package(GTest CONFIG REQUIRED)

add_executable(equora_capi_tests
    src/CApiTests.cpp
)

target_link_libraries(equora_capi_tests PRIVATE
    equora_capi
    GTest::gtest_main
)

# 测试进程工作目录没有 DLL 搜索路径,拷贝被测 DLL 到输出目录。
add_custom_command(TARGET equora_capi_tests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_FILE:equora_capi>
            $<TARGET_FILE_DIR:equora_capi_tests>)

include(GoogleTest)
gtest_discover_tests(equora_capi_tests)
