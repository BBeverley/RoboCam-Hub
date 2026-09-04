cmake_minimum_required(VERSION 3.25)

project(
  RoboCamHubNative
  VERSION 0.2.0
  DESCRIPTION "RoboCam Hub native GStreamer media core"
  LANGUAGES C CXX
)

include(CTest)

option(RCH_ENABLE_SANITIZERS "Enable AddressSanitizer and UndefinedBehaviorSanitizer" OFF)
option(RCH_BUILD_INGEST_PROBE "Build the manual single-camera diagnostics probe" ON)

find_package(PkgConfig REQUIRED)
find_package(Threads REQUIRED)
pkg_check_modules(
  GSTREAMER
  REQUIRED
  IMPORTED_TARGET
  gstreamer-1.0>=1.24
  gstreamer-app-1.0>=1.24
)

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
  src/frames/latest_frame.cpp
  src/ingest/single_camera_ingest.cpp
)

target_compile_features(robocamhub_native PRIVATE cxx_std_20)
target_compile_definitions(robocamhub_native PRIVATE ROBOCAMHUB_NATIVE_BUILD)
target_include_directories(robocamhub_native
  PUBLIC
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
    "$<INSTALL_INTERFACE:include>"
  PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/src"
)
target_link_libraries(robocamhub_native PRIVATE PkgConfig::GSTREAMER Threads::Threads)

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

if(RCH_BUILD_INGEST_PROBE)
  add_executable(robocamhub_ingest_probe tools/ingest_probe.cpp)
  target_compile_features(robocamhub_ingest_probe PRIVATE cxx_std_20)
  target_link_libraries(robocamhub_ingest_probe PRIVATE robocamhub_native)
  set_target_properties(robocamhub_ingest_probe PROPERTIES CXX_EXTENSIONS OFF)
  rch_enable_project_warnings(robocamhub_ingest_probe)
endif()

if(BUILD_TESTING)
  add_subdirectory(tests)
endif()
