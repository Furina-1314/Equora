# C ABI 边界测试:内存所有权、错误对象与异常边界。
find_package(GTest CONFIG REQUIRED)

add_executable(equora_capi_tests
    src/CApiTests.cpp
)

target_link_libraries(equora_capi_tests PRIVATE
    equora_capi
    GTest::gtest_main
)

# 确定性复制全部运行时 DLL(含传递依赖 sqlite3/gtest)。
# vcpkg applocal 在某些环境下不递归复制间接依赖,曾导致 CI 从 PATH
# 加载到第三方 sqlite3.dll 而崩溃,故显式复制。
add_custom_command(TARGET equora_capi_tests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy -t $<TARGET_FILE_DIR:equora_capi_tests>
            $<TARGET_RUNTIME_DLLS:equora_capi_tests>
    COMMAND_EXPAND_LISTS)

include(GoogleTest)
gtest_discover_tests(equora_capi_tests)
