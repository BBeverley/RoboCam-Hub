add_executable(robocamhub_native_tests
  lifecycle_test.c
)

set_target_properties(robocamhub_native_tests PROPERTIES
  C_EXTENSIONS OFF
  C_STANDARD 11
  C_STANDARD_REQUIRED YES
)

target_link_libraries(robocamhub_native_tests PRIVATE robocamhub_native)

rch_enable_project_warnings(robocamhub_native_tests)
rch_enable_sanitizers(robocamhub_native_tests)

add_test(NAME robocamhub_native.lifecycle COMMAND robocamhub_native_tests)
