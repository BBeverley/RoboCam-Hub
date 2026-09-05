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
#define RCH_ABI_VERSION_MINOR UINT32_C(5)
#define RCH_ABI_VERSION ((RCH_ABI_VERSION_MAJOR << 16U) | RCH_ABI_VERSION_MINOR)

#define RCH_CAMERA_CONFIG_VERSION UINT32_C(1)
#define RCH_CAMERA_STATUS_VERSION_V1 UINT32_C(1)
#define RCH_CAMERA_STATUS_VERSION_V2 UINT32_C(2)
#define RCH_CAMERA_STATUS_VERSION_V3 UINT32_C(3)
#define RCH_CAMERA_STATUS_VERSION RCH_CAMERA_STATUS_VERSION_V3
#define RCH_ENGINE_DIAGNOSTICS_VERSION_V1 UINT32_C(1)
#define RCH_ENGINE_DIAGNOSTICS_VERSION_V2 UINT32_C(2)
#define RCH_ENGINE_DIAGNOSTICS_VERSION RCH_ENGINE_DIAGNOSTICS_VERSION_V2
#define RCH_FRAME_LEASE_STATUS_VERSION_V1 UINT32_C(1)
#define RCH_FRAME_LEASE_STATUS_VERSION RCH_FRAME_LEASE_STATUS_VERSION_V1
#define RCH_VIEW_STATUS_VERSION_V1 UINT32_C(1)
#define RCH_VIEW_STATUS_VERSION_V2 UINT32_C(2)
#define RCH_VIEW_STATUS_VERSION_V3 UINT32_C(3)
#define RCH_VIEW_STATUS_VERSION RCH_VIEW_STATUS_VERSION_V3
#define RCH_VIEW_SOURCE_STATUS_VERSION_V1 UINT32_C(1)
#define RCH_VIEW_SOURCE_STATUS_VERSION RCH_VIEW_SOURCE_STATUS_VERSION_V1
#define RCH_VIEW_FRAME_LEASE_STATUS_VERSION_V1 UINT32_C(1)
#define RCH_VIEW_FRAME_LEASE_STATUS_VERSION RCH_VIEW_FRAME_LEASE_STATUS_VERSION_V1
#define RCH_VIEW_MAX_SOURCE_SLOTS UINT32_C(16)
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
  RCH_RESULT_CONNECTION_TIMEOUT = 11,
  RCH_RESULT_BUFFER_TOO_SMALL = 12
};

/* Fixed-width camera state type with stable named constants. */
typedef uint32_t rch_camera_state;

enum rch_camera_state_code {
  RCH_CAMERA_STATE_STOPPED = 0,
  RCH_CAMERA_STATE_STARTING = 1,
  RCH_CAMERA_STATE_RECEIVING = 2,
  RCH_CAMERA_STATE_FAILED = 3,
  RCH_CAMERA_STATE_STOPPING = 4,
  RCH_CAMERA_STATE_WAITING_TO_RETRY = 5
};

/* Fixed-width view render-state constants for view diagnostics snapshots. */
typedef uint32_t rch_view_render_state;

enum rch_view_render_state_code {
  RCH_VIEW_RENDER_STATE_STOPPED = 0,
  RCH_VIEW_RENDER_STATE_RUNNING = 1
};

typedef uint32_t rch_view_source_state;

enum rch_view_source_state_code {
  RCH_VIEW_SOURCE_STATE_UNBOUND = 0,
  RCH_VIEW_SOURCE_STATE_WAITING_FOR_FIRST_FRAME = 1,
  RCH_VIEW_SOURCE_STATE_LIVE = 2,
  RCH_VIEW_SOURCE_STATE_FROZEN_LAST_GOOD = 3,
  RCH_VIEW_SOURCE_STATE_RECONNECTING = 4,
  RCH_VIEW_SOURCE_STATE_MISSING_OR_STALE = 5
};

/* The UTF-8 strings are borrowed only for the duration of
 * rch_camera_configure. Strings are NUL-terminated, nonempty UTF-8 (ID <=255
 * bytes; URL <=2048 bytes). connect_timeout_ms is the deadline for the first
 * decoded frame, measured after the PLAYING request returns: zero selects
 * 10000 ms, otherwise 100..120000. reserved is zero. */
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
 * since local frame arrival, or RCH_NO_FRAME_AGE_MS if no frame exists.
 *
 * Version 1 includes fields up to latest_frame_age_ms.
 * Version 2 additively appends reconnect/backoff diagnostics.
 * Version 3 additively appends frame-consumer and View binding counts. */
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
  uint32_t reconnect_attempt_count;
  uint32_t successful_reconnect_count;
  uint32_t next_retry_delay_ms;
  uint32_t reserved_v2;
  uint32_t direct_frame_consumer_count;
  uint32_t bound_view_source_count;
  uint32_t total_frame_consumer_count;
  uint32_t reserved_v3;
} rch_camera_status_v1;

/* Low-frequency aggregate snapshot for the current configured camera registry.
 * Callers must set struct_size and struct_version before querying.
 * All counts reflect a point-in-time snapshot of configured logical cameras. */
typedef struct rch_engine_diagnostics_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  uint32_t configured_camera_count;
  uint32_t active_rtsp_session_total;
  uint32_t active_decoder_total;
  uint32_t cameras_starting_count;
  uint32_t cameras_receiving_count;
  uint32_t cameras_waiting_to_retry_count;
  uint32_t cameras_failed_count;
  uint32_t cameras_stopped_count;
  uint32_t reserved;
  uint64_t successful_reconnect_total;
  uint32_t view_count;
  uint32_t direct_frame_consumer_count;
  uint32_t total_bound_view_source_count;
  uint32_t reserved_v2;
} rch_engine_diagnostics_v1;

/* Metadata snapshot for a leased latest frame reference. This type never
 * exposes frame pixels. has_frame==0 means no decoded frame is currently
 * available for the consumer's camera source. */
typedef struct rch_frame_lease_status_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  uint32_t has_frame;
  uint32_t width;
  uint32_t height;
  uint32_t reserved;
  uint64_t decoded_frame_count;
  uint64_t latest_frame_sequence;
  uint64_t latest_frame_timestamp_ns;
  uint64_t latest_frame_age_ms;
} rch_frame_lease_status_v1;

/* Point-in-time diagnostics for a minimal native View/source binding object. */
typedef struct rch_view_status_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  uint32_t bound_source_count;
  uint32_t sources_with_frame_count;
  uint32_t stale_or_missing_source_count;
  uint32_t reserved;
  uint64_t last_observed_source_sequence;
  uint32_t render_state;
  uint32_t configured_width;
  uint32_t configured_height;
  uint32_t target_fps;
  uint64_t render_frame_count;
  uint64_t latest_composed_frame_sequence;
  uint64_t latest_composed_frame_age_ms;
  uint32_t render_fps_milli;
  uint32_t sources_contributing_count;
  uint32_t output_consumer_count;
  uint32_t reserved_v2;
  uint32_t last_render_duration_us;
  uint32_t average_render_duration_us;
  uint32_t p95_render_duration_us;
  uint32_t stale_source_frame_count;
  uint32_t live_source_count;
  uint32_t waiting_for_first_frame_count;
  uint32_t frozen_source_count;
  uint32_t reconnecting_source_count;
  uint32_t render_deadline_miss_count;
  uint32_t reserved_v3;
  uint64_t last_render_deadline_miss_us;
  uint64_t last_render_deadline_miss_sequence;
} rch_view_status_v1;

typedef struct rch_view_source_status_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  uint32_t slot_index;
  uint32_t source_state;
  uint32_t has_binding;
  uint32_t freeze_cache_has_frame;
  uint32_t source_live;
  char camera_id_utf8[256];
  uint64_t latest_observed_sequence;
  uint64_t latest_source_frame_age_ms;
  uint32_t camera_state;
  uint32_t reserved;
} rch_view_source_status_v1;

/* Metadata snapshot for a leased composed View frame reference.
 * This type never exposes full-frame payload ownership across the ABI. */
typedef struct rch_view_frame_lease_status_v1 {
  uint32_t struct_size;
  uint32_t struct_version;
  uint32_t has_frame;
  uint32_t width;
  uint32_t height;
  uint32_t reserved;
  uint64_t composed_frame_count;
  uint64_t latest_frame_sequence;
  uint64_t latest_frame_timestamp_ns;
  uint64_t latest_frame_age_ms;
} rch_view_frame_lease_status_v1;

/* Opaque, native-owned handle. Create it with rch_engine_create and release it
 * exactly once with rch_engine_destroy. */
typedef struct rch_engine* rch_engine_handle;
typedef struct rch_frame_consumer* rch_frame_consumer_handle;
typedef struct rch_frame_lease* rch_frame_lease_handle;
typedef struct rch_view* rch_view_handle;
typedef struct rch_view_frame_lease* rch_view_frame_lease_handle;

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

/* Adds or overwrites a camera configuration keyed by camera_id. If the camera
 * already exists, the managed engine reuses the same logical camera slot and does
 * not create a second RTSP session or decoder until a separate start call. */
RCH_API rch_result rch_camera_add(
  rch_engine_handle engine,
  const rch_camera_config_v1* config) RCH_NOEXCEPT;

/* Removes the named camera if present and tears down its active pipeline before
 * releasing the registry entry. */
RCH_API rch_result rch_camera_remove(
  rch_engine_handle engine,
  const char* camera_id_utf8) RCH_NOEXCEPT;

/* Starts the configured embedded GStreamer RTSP/H.264 pipeline. A repeated
 * start while active returns RCH_RESULT_ALREADY_STARTED without creating
 * another RTSP session or decoder. */
RCH_API rch_result rch_camera_start(rch_engine_handle engine) RCH_NOEXCEPT;

/* Starts the camera identified by camera_id without creating a new RTSP session or
 * decoder for an already-running logical camera. */
RCH_API rch_result rch_camera_start_by_id(
  rch_engine_handle engine,
  const char* camera_id_utf8) RCH_NOEXCEPT;

/* Stops the camera pipeline and releases its session and decoder ownership. */
RCH_API rch_result rch_camera_stop(rch_engine_handle engine) RCH_NOEXCEPT;

/* Stops the named camera and releases its session/decoder ownership without
 * affecting any other registered camera. */
RCH_API rch_result rch_camera_stop_by_id(
  rch_engine_handle engine,
  const char* camera_id_utf8) RCH_NOEXCEPT;

/* Returns a point-in-time status/counter snapshot. */
RCH_API rch_result rch_camera_get_status(
  rch_engine_handle engine,
  rch_camera_status_v1* out_status) RCH_NOEXCEPT;

/* Returns the status for the named camera. Invalid camera IDs or stale handles
 * return RCH_RESULT_INVALID_ARGUMENT or RCH_RESULT_INVALID_HANDLE. */
RCH_API rch_result rch_camera_get_status_by_id(
  rch_engine_handle engine,
  const char* camera_id_utf8,
  rch_camera_status_v1* out_status) RCH_NOEXCEPT;

/* Enumerates configured logical camera IDs in deterministic lexical order.
 * IDs are UTF-8, NUL-terminated, and densely packed in out_ids_utf8_buffer.
 * out_required_buffer_size receives the required byte size for all IDs,
 * including each trailing NUL terminator.
 * If out_ids_utf8_buffer_size is too small, returns
 * RCH_RESULT_BUFFER_TOO_SMALL and leaves caller memory unchanged.
 * For count-only queries, pass out_ids_utf8_buffer as null with size zero. */
RCH_API rch_result rch_camera_enumerate_ids(
  rch_engine_handle engine,
  char* out_ids_utf8_buffer,
  uint32_t out_ids_utf8_buffer_size,
  uint32_t* out_required_buffer_size,
  uint32_t* out_camera_count) RCH_NOEXCEPT;

/* Returns a low-frequency aggregate diagnostics snapshot for configured
 * logical cameras.
 * Callers must provide a versioned caller-owned buffer. */
RCH_API rch_result rch_engine_get_diagnostics(
  rch_engine_handle engine,
  rch_engine_diagnostics_v1* out_diagnostics) RCH_NOEXCEPT;

/* Creates a native consumer bound to one configured logical camera ID.
 * The consumer reads only the shared latest-frame source and never owns a
 * separate RTSP session or decoder. */
RCH_API rch_result rch_frame_consumer_create(
  rch_engine_handle engine,
  const char* camera_id_utf8,
  rch_frame_consumer_handle* out_consumer) RCH_NOEXCEPT;

/* Releases a frame consumer handle. */
RCH_API rch_result rch_frame_consumer_destroy(
  rch_frame_consumer_handle consumer) RCH_NOEXCEPT;

/* Acquires a reference-counted lease to the newest currently available frame
 * metadata for this consumer's camera source. */
RCH_API rch_result rch_frame_consumer_acquire_latest(
  rch_frame_consumer_handle consumer,
  rch_frame_lease_handle* out_lease) RCH_NOEXCEPT;

/* Returns metadata for a frame lease handle. */
RCH_API rch_result rch_frame_lease_get_status(
  rch_frame_lease_handle lease,
  rch_frame_lease_status_v1* out_status) RCH_NOEXCEPT;

/* Releases a frame lease handle. */
RCH_API rch_result rch_frame_lease_destroy(
  rch_frame_lease_handle lease) RCH_NOEXCEPT;

/* Creates a minimal native View ownership object keyed by a stable view ID. */
RCH_API rch_result rch_view_create(
  rch_engine_handle engine,
  const char* view_id_utf8,
  rch_view_handle* out_view) RCH_NOEXCEPT;

/* Destroys a native View ownership object and releases its source bindings. */
RCH_API rch_result rch_view_destroy(
  rch_view_handle view) RCH_NOEXCEPT;

/* Binds one source slot of a native View to a logical camera ID.
 * slot_index must be less than RCH_VIEW_MAX_SOURCE_SLOTS. */
RCH_API rch_result rch_view_bind_camera_source(
  rch_view_handle view,
  uint32_t slot_index,
  const char* camera_id_utf8) RCH_NOEXCEPT;

/* Clears the logical camera binding for a source slot. */
RCH_API rch_result rch_view_unbind_source(
  rch_view_handle view,
  uint32_t slot_index) RCH_NOEXCEPT;

/* Returns point-in-time View/source diagnostics. */
RCH_API rch_result rch_view_get_status(
  rch_view_handle view,
  rch_view_status_v1* out_status) RCH_NOEXCEPT;

/* Returns point-in-time status for a single source slot in a View. */
RCH_API rch_result rch_view_get_source_status(
  rch_view_handle view,
  uint32_t slot_index,
  rch_view_source_status_v1* out_status) RCH_NOEXCEPT;

/* Acquires a reference-counted lease to the latest composed View frame. */
RCH_API rch_result rch_view_acquire_latest_frame(
  rch_view_handle view,
  rch_view_frame_lease_handle* out_lease) RCH_NOEXCEPT;

/* Returns metadata for a composed-frame lease handle. */
RCH_API rch_result rch_view_frame_lease_get_status(
  rch_view_frame_lease_handle lease,
  rch_view_frame_lease_status_v1* out_status) RCH_NOEXCEPT;

/* Samples one RGBA pixel from a composed-frame lease at (x,y). */
RCH_API rch_result rch_view_frame_lease_sample_rgba(
  rch_view_frame_lease_handle lease,
  uint32_t x,
  uint32_t y,
  uint8_t* out_r,
  uint8_t* out_g,
  uint8_t* out_b,
  uint8_t* out_a) RCH_NOEXCEPT;

/* Releases a composed-frame lease handle. */
RCH_API rch_result rch_view_frame_lease_destroy(
  rch_view_frame_lease_handle lease) RCH_NOEXCEPT;

#if defined(__cplusplus)
}
#endif

#undef RCH_NOEXCEPT

#endif
