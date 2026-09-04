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
#define RCH_ABI_VERSION_MINOR UINT32_C(1)
#define RCH_ABI_VERSION ((RCH_ABI_VERSION_MAJOR << 16U) | RCH_ABI_VERSION_MINOR)

#define RCH_CAMERA_CONFIG_VERSION UINT32_C(1)
#define RCH_CAMERA_STATUS_VERSION UINT32_C(1)
#define RCH_NO_FRAME_AGE_MS UINT64_MAX

/* Fixed-width result type with stable named error-code constants. */
typedef int32_t rch_result;

enum rch_result_code {
  RCH_RESULT_OK = 0,
  RCH_RESULT_INVALID_ARGUMENT = 1,
  RCH_RESULT_INVALID_HANDLE = 2,
  RCH_RESULT_OUT_OF_MEMORY = 3,
  RCH_RESULT_INTERNAL_ERROR = 4,
  RCH_RESULT_INVALID_STATE = 5,
  RCH_RESULT_ALREADY_STARTED = 6,
  RCH_RESULT_NOT_CONFIGURED = 7,
  RCH_RESULT_GSTREAMER_ERROR = 8,
  RCH_RESULT_RTSP_FAILURE = 9,
  RCH_RESULT_DECODER_FAILURE = 10,
  RCH_RESULT_CONNECTION_TIMEOUT = 11
};

/* Fixed-width camera state type with stable named constants. */
typedef uint32_t rch_camera_state;

enum rch_camera_state_code {
  RCH_CAMERA_STATE_STOPPED = 0,
  RCH_CAMERA_STATE_STARTING = 1,
  RCH_CAMERA_STATE_RECEIVING = 2,
  RCH_CAMERA_STATE_FAILED = 3,
  RCH_CAMERA_STATE_STOPPING = 4
};

/* The UTF-8 strings are borrowed only for the duration of
 * rch_camera_configure. Strings are NUL-terminated, nonempty UTF-8 (ID <=255
 * bytes; URL <=2048 bytes). connect_timeout_ms is the deadline for the first
 * decoded frame: zero selects 10000 ms, otherwise 100..120000. reserved is zero. */
typedef struct rch_camera_config_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  const char* camera_id_utf8;
  const char* rtsp_url_utf8;
  uint32_t connect_timeout_ms;
  uint32_t reserved;
} rch_camera_config_v1;

/* This is a low-frequency metadata snapshot. It never exposes frame pixels or
 * transfers ownership of the native latest-frame sample. Callers must set
 * struct_size and struct_version before querying. Counts describe the single
 * owned pipeline while starting/receiving, not wire-level connection evidence.
 * Frame count/sequence are cumulative across starts; timestamp is stream PTS
 * (DTS fallback, zero if absent), not wall-clock time. Age is monotonic time
 * since local frame arrival, or RCH_NO_FRAME_AGE_MS if no frame exists. */
typedef struct rch_camera_status_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  rch_camera_state state;
  rch_result last_result;
  uint32_t active_rtsp_session_count;
  uint32_t active_decoder_count;
  uint32_t has_latest_frame;
  uint32_t latest_frame_width;
  uint32_t latest_frame_height;
  uint32_t reserved;
  uint64_t decoded_frame_count;
  uint64_t latest_frame_sequence;
  uint64_t latest_frame_timestamp_ns;
  uint64_t latest_frame_age_ms;
} rch_camera_status_v1;

/* Opaque, native-owned handle. Create it with rch_engine_create and release it
 * exactly once with rch_engine_destroy. */
typedef struct rch_engine* rch_engine_handle;

RCH_API uint32_t rch_get_abi_version(void) RCH_NOEXCEPT;

/* On success, out_engine receives a non-null handle owned by the caller. */
RCH_API rch_result rch_engine_create(rch_engine_handle* out_engine) RCH_NOEXCEPT;

/* Releases engine. Passing a null handle returns RCH_RESULT_INVALID_HANDLE. */
RCH_API rch_result rch_engine_destroy(rch_engine_handle engine) RCH_NOEXCEPT;

/* Gate 1A owns exactly one configured camera per engine. Configuration can be
 * replaced only while stopped or failed. The production RTP transport is UDP.
 * Control calls are serialized internally and can block during GStreamer
 * teardown: do not call them on the UI thread. Status may be queried concurrently
 * (fields can reflect an in-progress transition). The caller must serialize
 * engine destruction against every other operation using that handle. */
RCH_API rch_result rch_camera_configure(
  rch_engine_handle engine,
  const rch_camera_config_v1* config) RCH_NOEXCEPT;

/* Starts the configured embedded GStreamer RTSP/H.264 pipeline. A repeated
 * start while active returns RCH_RESULT_ALREADY_STARTED without creating
 * another RTSP session or decoder. */
RCH_API rch_result rch_camera_start(rch_engine_handle engine) RCH_NOEXCEPT;

/* Stops the camera pipeline and releases its session and decoder ownership. */
RCH_API rch_result rch_camera_stop(rch_engine_handle engine) RCH_NOEXCEPT;

/* Returns a point-in-time status/counter snapshot. */
RCH_API rch_result rch_camera_get_status(
  rch_engine_handle engine,
  rch_camera_status_v1* out_status) RCH_NOEXCEPT;

#if defined(__cplusplus)
}
#endif

#undef RCH_NOEXCEPT

#endif
