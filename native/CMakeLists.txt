cmake_minimum_required(VERSION 3.25)

project(
  RoboCamHubNative
  VERSION 0.1.0
  DESCRIPTION "RoboCam Hub native media-core lifecycle skeleton"
  LANGUAGES C CXX
)

include(CTest)

option(RCH_ENABLE_SANITIZERS "Enable AddressSanitizer and UndefinedBehaviorSanitizer" OFF)

# Keep Windows test executables beside the shared-library DLL. The same layout
# also gives macOS and other generators predictable build artefact locations.
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

function(rch_enable_project_warnings target)
  if(MSVC)
    target_compile_options(${target} PRIVATE /W4 /WX)
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic -Werror)
  endif()
endfunction()

function(rch_enable_sanitizers target)
  if(NOT RCH_ENABLE_SANITIZERS)
    return()
  endif()

  if(CMAKE_CXX_COMPILER_ID MATCHES "AppleClang|Clang|GNU")
    target_compile_options(${target} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
    target_link_options(${target} PRIVATE -fsanitize=address,undefined)
  else()
    message(FATAL_ERROR "RCH_ENABLE_SANITIZERS is not supported by this compiler")
  endif()
endfunction()

add_library(robocamhub_native SHARED
  src/engine/robocamhub_native.cpp
)

target_compile_features(robocamhub_native PRIVATE cxx_std_20)
target_compile_definitions(robocamhub_native PRIVATE ROBOCAMHUB_NATIVE_BUILD)
target_include_directories(robocamhub_native
  PUBLIC
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
    "$<INSTALL_INTERFACE:include>"
)

set_target_properties(robocamhub_native PROPERTIES
  CXX_EXTENSIONS OFF
  CXX_VISIBILITY_PRESET hidden
  OUTPUT_NAME robocamhub_native
  VISIBILITY_INLINES_HIDDEN YES
)

if(WIN32)
  target_compile_definitions(robocamhub_native PRIVATE RCH_PLATFORM_WINDOWS=1)
elseif(APPLE)
  target_compile_definitions(robocamhub_native PRIVATE RCH_PLATFORM_MACOS=1)
endif()

rch_enable_project_warnings(robocamhub_native)
rch_enable_sanitizers(robocamhub_native)

if(BUILD_TESTING)
  add_subdirectory(tests)
endif()
