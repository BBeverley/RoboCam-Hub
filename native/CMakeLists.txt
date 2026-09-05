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
option(RCH_BUILD_NDI_PROBE "Build the manual four-camera NDI sender diagnostics probe" ON)
option(RCH_ENABLE_NDI_SDK "Enable official NDI SDK backend when discoverable" ON)
set(RCH_NDI_SDK_ROOT "" CACHE PATH "Root folder of an installed official NDI SDK")

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
  src/ndi/ndi_sender_backend.cpp
)

if(WIN32)
  target_sources(robocamhub_native PRIVATE src/preview/native_preview_surface_win.cpp)
  target_link_libraries(robocamhub_native PRIVATE user32 gdi32)
elseif(APPLE)
  enable_language(OBJCXX)
  target_sources(robocamhub_native PRIVATE src/preview/native_preview_surface_mac.mm)
  set_source_files_properties(
    src/preview/native_preview_surface_mac.mm
    PROPERTIES COMPILE_FLAGS "-fno-objc-arc"
  )
  find_library(RCH_APPKIT_FRAMEWORK AppKit REQUIRED)
  find_library(RCH_COREGRAPHICS_FRAMEWORK CoreGraphics REQUIRED)
  target_link_libraries(
    robocamhub_native
    PRIVATE
      "${RCH_APPKIT_FRAMEWORK}"
      "${RCH_COREGRAPHICS_FRAMEWORK}"
  )
endif()

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

set(RCH_HAS_NDI_SDK OFF)
if(RCH_ENABLE_NDI_SDK)
  set(_rch_ndi_hints)
  if(RCH_NDI_SDK_ROOT)
    list(APPEND _rch_ndi_hints "${RCH_NDI_SDK_ROOT}")
  endif()
  if(DEFINED ENV{NDI_SDK_ROOT})
    list(APPEND _rch_ndi_hints "$ENV{NDI_SDK_ROOT}")
  endif()
  if(DEFINED ENV{NDI_SDK_DIR})
    list(APPEND _rch_ndi_hints "$ENV{NDI_SDK_DIR}")
  endif()
  if(DEFINED ENV{NDI_INSTALL_DIR})
    list(APPEND _rch_ndi_hints "$ENV{NDI_INSTALL_DIR}")
  endif()
  if(APPLE AND IS_DIRECTORY "/Library/NDI SDK for Apple")
    list(APPEND _rch_ndi_hints "/Library/NDI SDK for Apple")
  endif()

  find_path(
    RCH_NDI_SDK_INCLUDE_DIR
    NAMES Processing.NDI.Lib.h
    HINTS ${_rch_ndi_hints}
    PATH_SUFFIXES Include include
  )
  find_library(
    RCH_NDI_SDK_LIBRARY
    NAMES NDIlib_v6 NDIlib_v5 Processing.NDI.Lib.x64 Processing.NDI.Lib.x86 ndi
    HINTS ${_rch_ndi_hints}
    PATH_SUFFIXES Lib lib Bin bin Lib/macOS lib/macOS Lib/x64 lib/x64 Bin/x64 bin/x64
  )

  if(RCH_NDI_SDK_INCLUDE_DIR AND RCH_NDI_SDK_LIBRARY)
    set(RCH_HAS_NDI_SDK ON)
    target_compile_definitions(robocamhub_native PRIVATE RCH_HAS_NDI_SDK=1)
    target_include_directories(robocamhub_native PRIVATE "${RCH_NDI_SDK_INCLUDE_DIR}")
    target_link_libraries(robocamhub_native PRIVATE "${RCH_NDI_SDK_LIBRARY}")
    message(STATUS "Official NDI SDK detected: ${RCH_NDI_SDK_INCLUDE_DIR}")
    message(STATUS "Official NDI SDK library: ${RCH_NDI_SDK_LIBRARY}")
  else()
    message(STATUS "Official NDI SDK not found. Deterministic sender-core backend remains active.")
    message(STATUS "Set RCH_NDI_SDK_ROOT (or env NDI_SDK_ROOT / NDI_SDK_DIR / NDI_INSTALL_DIR) to an installed SDK root containing Processing.NDI.Lib.h and NDI library binaries.")
  endif()
endif()

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

if(RCH_BUILD_NDI_PROBE)
  add_executable(robocamhub_ndi_sender_probe tools/ndi_sender_probe.cpp)
  target_compile_features(robocamhub_ndi_sender_probe PRIVATE cxx_std_20)
  target_link_libraries(robocamhub_ndi_sender_probe PRIVATE robocamhub_native)
  set_target_properties(robocamhub_ndi_sender_probe PROPERTIES CXX_EXTENSIONS OFF)
  rch_enable_project_warnings(robocamhub_ndi_sender_probe)
endif()

if(BUILD_TESTING)
  add_subdirectory(tests)
endif()
