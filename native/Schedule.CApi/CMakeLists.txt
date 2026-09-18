# 稳定 C ABI 导出层:桌面端(C#)与本地核心(C++)的唯一边界。
add_library(equora_capi SHARED
    src/CApi.cpp
)

add_library(Equora::CApi ALIAS equora_capi)

target_include_directories(equora_capi PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_compile_definitions(equora_capi PRIVATE EQUORA_CAPI_BUILD)

target_link_libraries(equora_capi
    PUBLIC Equora::Core
)

set_target_properties(equora_capi PROPERTIES
    OUTPUT_NAME "equora_capi"
    PREFIX ""
)
