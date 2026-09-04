#include "robocamhub_native.h"

#include "ingest/single_camera_ingest.h"

#include <gst/gst.h>

#include <cstddef>
#include <cstring>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <unordered_map>
#include <utility>

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
  std::mutex camera_registry_mutex_;
  std::unordered_map<std::string, std::shared_ptr<robocamhub::ingest::SingleCameraIngest>> cameras_;
  robocamhub::ingest::SingleCameraIngest camera;
};

bool IsValidCameraIdUtf8(const char* value)
{
  return value != nullptr && value[0] != '\0' && std::strlen(value) <= 255U
    && g_utf8_validate(value, -1, nullptr) != FALSE;
}

std::shared_ptr<robocamhub::ingest::SingleCameraIngest> FindCameraById(
  rch_engine_handle engine,
  const char* camera_id_utf8)
{
  if (engine == nullptr || camera_id_utf8 == nullptr || !IsValidCameraIdUtf8(camera_id_utf8)) {
    return nullptr;
  }

  std::lock_guard lock(engine->camera_registry_mutex_);
  const auto found = engine->cameras_.find(camera_id_utf8);
  if (found == engine->cameras_.end()) {
    return nullptr;
  }
  return found->second;
}

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

  try {
    std::vector<std::shared_ptr<robocamhub::ingest::SingleCameraIngest>> cameras;
    {
      std::lock_guard lock(engine->camera_registry_mutex_);
      cameras.reserve(engine->cameras_.size());
      for (auto& [camera_id, camera] : engine->cameras_) {
        (void)camera_id;
        cameras.push_back(camera);
      }
      engine->cameras_.clear();
    }

    for (auto& camera : cameras) {
      if (camera != nullptr) {
        camera->Stop();
      }
    }

    engine->camera.Stop();
    delete engine;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
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

extern "C" rch_result rch_camera_add(
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
    if (!IsValidCameraIdUtf8(config->camera_id_utf8) || config->reserved != 0) {
      return RCH_RESULT_INVALID_ARGUMENT;
    }

    std::shared_ptr<robocamhub::ingest::SingleCameraIngest> camera;
    {
      std::lock_guard lock(engine->camera_registry_mutex_);
      auto found = engine->cameras_.find(config->camera_id_utf8);
      if (found != engine->cameras_.end()) {
        camera = found->second;
      } else {
        camera = std::make_shared<robocamhub::ingest::SingleCameraIngest>();
        engine->cameras_[config->camera_id_utf8] = camera;
      }
    }

    return camera->Configure(*config);
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_camera_remove(
  rch_engine_handle engine,
  const char* camera_id_utf8) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (!IsValidCameraIdUtf8(camera_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    std::shared_ptr<robocamhub::ingest::SingleCameraIngest> camera;
    {
      std::lock_guard lock(engine->camera_registry_mutex_);
      const auto found = engine->cameras_.find(camera_id_utf8);
      if (found == engine->cameras_.end()) {
        return RCH_RESULT_NOT_CONFIGURED;
      }
      camera = found->second;
      engine->cameras_.erase(found);
    }

    camera->Stop();
    return RCH_RESULT_OK;
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

extern "C" rch_result rch_camera_start_by_id(
  rch_engine_handle engine,
  const char* camera_id_utf8) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (!IsValidCameraIdUtf8(camera_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  const auto camera = FindCameraById(engine, camera_id_utf8);
  if (camera == nullptr) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  try {
    return camera->Start();
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

extern "C" rch_result rch_camera_stop_by_id(
  rch_engine_handle engine,
  const char* camera_id_utf8) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (!IsValidCameraIdUtf8(camera_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  const auto camera = FindCameraById(engine, camera_id_utf8);
  if (camera == nullptr) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  try {
    return camera->Stop();
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
    rch_camera_status_v1 full_status{};
    engine->camera.FillStatus(full_status);
    full_status.struct_size = status_v2_ok
      ? static_cast<uint32_t>(sizeof(rch_camera_status_v1))
      : status_v1_size;
    full_status.struct_version = status_v2_ok
      ? RCH_CAMERA_STATUS_VERSION_V2
      : RCH_CAMERA_STATUS_VERSION_V1;

    const auto bytes_to_copy = status_v2_ok ? sizeof(rch_camera_status_v1) : status_v1_size;
    std::memcpy(out_status, &full_status, bytes_to_copy);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_camera_get_status_by_id(
  rch_engine_handle engine,
  const char* camera_id_utf8,
  rch_camera_status_v1* out_status) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (!IsValidCameraIdUtf8(camera_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }
  if (out_status == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  const auto camera = FindCameraById(engine, camera_id_utf8);
  if (camera == nullptr) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  const auto status_version = out_status->struct_version;
  const auto status_size = out_status->struct_size;
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
    rch_camera_status_v1 full_status{};
    camera->FillStatus(full_status);
    full_status.struct_size = status_v2_ok
      ? static_cast<uint32_t>(sizeof(rch_camera_status_v1))
      : status_v1_size;
    full_status.struct_version = status_v2_ok
      ? RCH_CAMERA_STATUS_VERSION_V2
      : RCH_CAMERA_STATUS_VERSION_V1;

    const auto bytes_to_copy = status_v2_ok ? sizeof(rch_camera_status_v1) : status_v1_size;
    std::memcpy(out_status, &full_status, bytes_to_copy);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}
