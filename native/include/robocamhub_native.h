#ifndef ROBOCAMHUB_NATIVE_H
#define ROBOCAMHUB_NATIVE_H

#include <stdint.h>

#if defined(_WIN32)
  #if defined(ROBOCAMHUB_NATIVE_BUILD)
    #define RCH_API __declspec(dllexport)
  #else
    #define RCH_API __declspec(dllimport)
  #endif
#elif defined(__GNUC__) || defined(__clang__)
  #define RCH_API __attribute__((visibility("default")))
#else
  #define RCH_API
#endif

#if defined(__cplusplus)
  #define RCH_NOEXCEPT noexcept
extern "C" {
#else
  #define RCH_NOEXCEPT
#endif

#define RCH_ABI_VERSION_MAJOR UINT32_C(1)
#define RCH_ABI_VERSION_MINOR UINT32_C(0)
#define RCH_ABI_VERSION ((RCH_ABI_VERSION_MAJOR << 16U) | RCH_ABI_VERSION_MINOR)

/* Fixed-width result type with stable named error-code constants. */
typedef int32_t rch_result;

enum rch_result_code {
  RCH_RESULT_OK = 0,
  RCH_RESULT_INVALID_ARGUMENT = 1,
  RCH_RESULT_INVALID_HANDLE = 2,
  RCH_RESULT_OUT_OF_MEMORY = 3,
  RCH_RESULT_INTERNAL_ERROR = 4
};

/* Opaque, native-owned handle. Create it with rch_engine_create and release it
 * exactly once with rch_engine_destroy. */
typedef struct rch_engine* rch_engine_handle;

RCH_API uint32_t rch_get_abi_version(void) RCH_NOEXCEPT;

/* On success, out_engine receives a non-null handle owned by the caller. */
RCH_API rch_result rch_engine_create(rch_engine_handle* out_engine) RCH_NOEXCEPT;

/* Releases engine. Passing a null handle returns RCH_RESULT_INVALID_HANDLE. */
RCH_API rch_result rch_engine_destroy(rch_engine_handle engine) RCH_NOEXCEPT;

#if defined(__cplusplus)
}
#endif

#undef RCH_NOEXCEPT

#endif
