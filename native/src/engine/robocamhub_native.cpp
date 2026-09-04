#include "robocamhub_native.h"

#include "ingest/single_camera_ingest.h"

#include <gst/gst.h>

#include <cstddef>
#include <new>

namespace {

class GStreamerRuntime final {
public:
  GStreamerRuntime()
  {
    GError* error = nullptr;
    initialized_ = gst_init_check(nullptr, nullptr, &error) != FALSE;
    if (error != nullptr) {
      g_error_free(error);
    }
  }

  ~GStreamerRuntime()
  {
    if (initialized_) {
      gst_deinit();
    }
  }

  [[nodiscard]] bool IsInitialized() const
  {
    return initialized_;
  }

private:
  bool initialized_{false};
};

GStreamerRuntime& Runtime()
{
  static GStreamerRuntime runtime;
  return runtime;
}

}  // namespace

struct rch_engine {
  uint32_t abi_version{RCH_ABI_VERSION};
  robocamhub::ingest::SingleCameraIngest camera;
};

extern "C" uint32_t rch_get_abi_version(void) noexcept
{
  return RCH_ABI_VERSION;
}

extern "C" rch_result rch_engine_create(rch_engine_handle* out_engine) noexcept
{
  if (out_engine == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  *out_engine = nullptr;
  try {
    if (!Runtime().IsInitialized()) {
      return RCH_RESULT_GSTREAMER_ERROR;
    }
    *out_engine = new rch_engine();
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_engine_destroy(rch_engine_handle engine) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  delete engine;
  return RCH_RESULT_OK;
}

extern "C" rch_result rch_camera_configure(
  rch_engine_handle engine,
  const rch_camera_config_v1* config) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (config == nullptr || config->struct_size < sizeof(rch_camera_config_v1)
      || config->struct_version != RCH_CAMERA_CONFIG_VERSION) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    return engine->camera.Configure(*config);
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_camera_start(rch_engine_handle engine) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    return engine->camera.Start();
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_camera_stop(rch_engine_handle engine) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    return engine->camera.Stop();
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_camera_get_status(
  rch_engine_handle engine,
  rch_camera_status_v1* out_status) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  const auto status_version = out_status == nullptr ? 0U : out_status->struct_version;
  const auto status_size = out_status == nullptr ? 0U : out_status->struct_size;
  constexpr std::uint32_t status_v1_size =
    static_cast<std::uint32_t>(offsetof(rch_camera_status_v1, reconnect_attempt_count));

  const bool status_v1_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V1 && status_size >= status_v1_size;
  const bool status_v2_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V2 && status_size >= sizeof(rch_camera_status_v1);
  if (!status_v1_ok && !status_v2_ok) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    engine->camera.FillStatus(*out_status);
    out_status->struct_size = status_v2_ok
      ? static_cast<uint32_t>(sizeof(rch_camera_status_v1))
      : status_v1_size;
    out_status->struct_version = status_v2_ok
      ? RCH_CAMERA_STATUS_VERSION_V2
      : RCH_CAMERA_STATUS_VERSION_V1;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}
