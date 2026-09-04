#include "robocamhub_native.h"

#include "ingest/single_camera_ingest.h"

#include <gst/gst.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstddef>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

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

struct CameraEntry final {
  explicit CameraEntry(std::shared_ptr<robocamhub::ingest::SingleCameraIngest> ingest)
      : ingest(std::move(ingest))
  {
  }

  std::shared_ptr<robocamhub::ingest::SingleCameraIngest> ingest;
  std::mutex lifecycle_mutex;
  std::atomic<bool> removed{false};
  std::atomic<std::uint64_t> generation{0};
  std::atomic<std::uint32_t> direct_consumer_count{0};
  std::atomic<std::uint32_t> view_binding_count{0};
};

struct ViewState;

struct EngineRegistry final {
  std::mutex camera_registry_mutex_;
  std::unordered_map<std::string, std::shared_ptr<CameraEntry>> cameras_;
  std::mutex view_registry_mutex_;
  std::unordered_map<std::string, std::shared_ptr<ViewState>> views_;
  std::atomic<bool> shutting_down{false};
};

struct ViewSourceBinding final {
  std::string camera_id;
  std::weak_ptr<CameraEntry> camera;
  std::uint64_t last_observed_sequence{0};
};

struct ViewState final {
  explicit ViewState(std::shared_ptr<EngineRegistry> owner, std::string id)
      : registry(std::move(owner)), view_id(std::move(id))
  {
  }

  std::shared_ptr<EngineRegistry> registry;
  std::string view_id;
  std::mutex mutex;
  std::array<std::optional<ViewSourceBinding>, RCH_VIEW_MAX_SOURCE_SLOTS> sources{};
  std::atomic<bool> removed{false};
};

struct rch_frame_consumer final {
  std::shared_ptr<EngineRegistry> registry;
  std::weak_ptr<CameraEntry> camera;
  std::atomic<bool> destroyed{false};
};

struct rch_frame_lease final {
  std::shared_ptr<EngineRegistry> registry;
  robocamhub::frames::LatestFrameLease lease;
  std::atomic<bool> destroyed{false};
};

struct rch_view final {
  std::shared_ptr<ViewState> state;
  std::atomic<bool> destroyed{false};
};

namespace {

bool CanProceedWithEntry(const std::shared_ptr<CameraEntry>& entry,
                         std::uint64_t expected_generation)
{
  if (entry == nullptr || entry->ingest == nullptr) {
    return false;
  }
  if (entry->removed.load(std::memory_order_acquire)) {
    return false;
  }
  return entry->generation.load(std::memory_order_acquire) == expected_generation;
}

}  // namespace

struct rch_engine {
  uint32_t abi_version{RCH_ABI_VERSION};
  std::shared_ptr<EngineRegistry> registry_{std::make_shared<EngineRegistry>()};
  robocamhub::ingest::SingleCameraIngest camera;
};

bool IsValidCameraIdUtf8(const char* value)
{
  return value != nullptr && value[0] != '\0' && std::strlen(value) <= 255U
    && g_utf8_validate(value, -1, nullptr) != FALSE;
}

std::shared_ptr<CameraEntry> FindCameraById(
  const std::shared_ptr<EngineRegistry>& registry,
  const char* camera_id_utf8)
{
  if (registry == nullptr || camera_id_utf8 == nullptr || !IsValidCameraIdUtf8(camera_id_utf8)) {
    return nullptr;
  }

  std::lock_guard lock(registry->camera_registry_mutex_);
  const auto found = registry->cameras_.find(camera_id_utf8);
  if (found == registry->cameras_.end()) {
    return nullptr;
  }
  return found->second;
}

std::vector<std::pair<std::string, std::shared_ptr<CameraEntry>>> SnapshotSortedCameras(
  const std::shared_ptr<EngineRegistry>& registry)
{
  std::vector<std::pair<std::string, std::shared_ptr<CameraEntry>>> snapshot;
  {
    std::lock_guard lock(registry->camera_registry_mutex_);
    snapshot.reserve(registry->cameras_.size());
    for (const auto& [camera_id, entry] : registry->cameras_) {
      snapshot.emplace_back(camera_id, entry);
    }
  }

  std::sort(
    snapshot.begin(),
    snapshot.end(),
    [](const auto& left, const auto& right) { return left.first < right.first; });

  return snapshot;
}

std::vector<std::shared_ptr<ViewState>> SnapshotViews(const std::shared_ptr<EngineRegistry>& registry)
{
  std::vector<std::shared_ptr<ViewState>> snapshot;
  {
    std::lock_guard lock(registry->view_registry_mutex_);
    snapshot.reserve(registry->views_.size());
    for (const auto& [view_id, view] : registry->views_) {
      (void)view_id;
      snapshot.push_back(view);
    }
  }
  return snapshot;
}

bool IsRegistryActive(const std::shared_ptr<EngineRegistry>& registry)
{
  return registry != nullptr && !registry->shutting_down.load(std::memory_order_acquire);
}

void DecrementIfPositive(std::atomic<std::uint32_t>& counter)
{
  auto current = counter.load(std::memory_order_acquire);
  while (current > 0U
         && !counter.compare_exchange_weak(current, current - 1U, std::memory_order_acq_rel)) {
  }
}

void ReleaseViewBinding(std::optional<ViewSourceBinding>& binding)
{
  if (!binding.has_value()) {
    return;
  }

  if (auto camera = binding->camera.lock(); camera != nullptr) {
    DecrementIfPositive(camera->view_binding_count);
  }
  binding.reset();
}

void ReleaseAllViewBindings(ViewState& state)
{
  std::lock_guard lock(state.mutex);
  for (auto& binding : state.sources) {
    ReleaseViewBinding(binding);
  }
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
    if (engine->registry_ != nullptr) {
      engine->registry_->shutting_down.store(true, std::memory_order_release);
    }

    std::vector<std::shared_ptr<CameraEntry>> camera_entries;
    std::vector<std::shared_ptr<ViewState>> views;
    {
      std::lock_guard lock(engine->registry_->camera_registry_mutex_);
      camera_entries.reserve(engine->registry_->cameras_.size());
      for (auto& [camera_id, entry] : engine->registry_->cameras_) {
        (void)camera_id;
        camera_entries.push_back(entry);
      }
      engine->registry_->cameras_.clear();
    }

    {
      std::lock_guard lock(engine->registry_->view_registry_mutex_);
      views.reserve(engine->registry_->views_.size());
      for (auto& [view_id, view] : engine->registry_->views_) {
        (void)view_id;
        views.push_back(view);
      }
      engine->registry_->views_.clear();
    }

    for (auto& view : views) {
      if (view == nullptr) {
        continue;
      }
      view->removed.store(true, std::memory_order_release);
      ReleaseAllViewBindings(*view);
    }

    for (auto& entry : camera_entries) {
      if (entry == nullptr || entry->ingest == nullptr) {
        continue;
      }
      std::unique_lock lock(entry->lifecycle_mutex);
      entry->removed.store(true, std::memory_order_release);
      entry->generation.fetch_add(1U, std::memory_order_acq_rel);
      entry->ingest->Stop();
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

    std::shared_ptr<CameraEntry> entry;
    {
      std::lock_guard lock(engine->registry_->camera_registry_mutex_);
      auto found = engine->registry_->cameras_.find(config->camera_id_utf8);
      if (found != engine->registry_->cameras_.end()) {
        entry = found->second;
      } else {
        entry = std::make_shared<CameraEntry>(std::make_shared<robocamhub::ingest::SingleCameraIngest>());
        engine->registry_->cameras_[config->camera_id_utf8] = entry;
      }
    }

    std::unique_lock lifecycle_lock(entry->lifecycle_mutex);
    if (entry->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    const auto generation = entry->generation.load(std::memory_order_acquire);
    if (!CanProceedWithEntry(entry, generation)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    auto result = entry->ingest->Configure(*config);
    if (result == RCH_RESULT_OK) {
      entry->generation.fetch_add(1U, std::memory_order_acq_rel);
    }
    return result;
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
    std::shared_ptr<CameraEntry> entry;
    {
      std::lock_guard lock(engine->registry_->camera_registry_mutex_);
      const auto found = engine->registry_->cameras_.find(camera_id_utf8);
      if (found == engine->registry_->cameras_.end()) {
        return RCH_RESULT_NOT_CONFIGURED;
      }
      entry = found->second;
      engine->registry_->cameras_.erase(found);
    }

    std::unique_lock lifecycle_lock(entry->lifecycle_mutex);
    entry->removed.store(true, std::memory_order_release);
    entry->generation.fetch_add(1U, std::memory_order_acq_rel);
    entry->ingest->Stop();
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

  const auto entry = FindCameraById(engine->registry_, camera_id_utf8);
  if (entry == nullptr || entry->ingest == nullptr) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  try {
    std::unique_lock lifecycle_lock(entry->lifecycle_mutex);
    const auto generation = entry->generation.load(std::memory_order_acquire);
    if (!CanProceedWithEntry(entry, generation)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    const auto result = entry->ingest->Start();
    if (result == RCH_RESULT_OK && entry->generation.load(std::memory_order_acquire) != generation) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    return result;
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

  const auto entry = FindCameraById(engine->registry_, camera_id_utf8);
  if (entry == nullptr || entry->ingest == nullptr) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  try {
    std::unique_lock lifecycle_lock(entry->lifecycle_mutex);
    const auto generation = entry->generation.load(std::memory_order_acquire);
    if (!CanProceedWithEntry(entry, generation)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    const auto result = entry->ingest->Stop();
    if (result == RCH_RESULT_OK && entry->generation.load(std::memory_order_acquire) != generation) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    return result;
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
  constexpr std::uint32_t status_v2_size =
    static_cast<std::uint32_t>(offsetof(rch_camera_status_v1, direct_frame_consumer_count));

  const bool status_v1_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V1 && status_size >= status_v1_size;
  const bool status_v2_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V2 && status_size >= status_v2_size;
  const bool status_v3_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V3 && status_size >= sizeof(rch_camera_status_v1);
  if (!status_v1_ok && !status_v2_ok && !status_v3_ok) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    rch_camera_status_v1 full_status{};
    engine->camera.FillStatus(full_status);
    full_status.direct_frame_consumer_count = 0;
    full_status.bound_view_source_count = 0;
    full_status.total_frame_consumer_count = 0;
    full_status.reserved_v3 = 0;
    full_status.struct_size = status_v3_ok
      ? static_cast<uint32_t>(sizeof(rch_camera_status_v1))
      : (status_v2_ok ? status_v2_size : status_v1_size);
    full_status.struct_version = status_v3_ok
      ? RCH_CAMERA_STATUS_VERSION_V3
      : (status_v2_ok ? RCH_CAMERA_STATUS_VERSION_V2 : RCH_CAMERA_STATUS_VERSION_V1);

    const auto bytes_to_copy = status_v3_ok
      ? sizeof(rch_camera_status_v1)
      : (status_v2_ok ? status_v2_size : status_v1_size);
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

  const auto entry = FindCameraById(engine->registry_, camera_id_utf8);
  if (entry == nullptr || entry->ingest == nullptr) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  if (entry->removed.load(std::memory_order_acquire)) {
    return RCH_RESULT_NOT_CONFIGURED;
  }

  const auto status_version = out_status->struct_version;
  const auto status_size = out_status->struct_size;
  constexpr std::uint32_t status_v1_size =
    static_cast<std::uint32_t>(offsetof(rch_camera_status_v1, reconnect_attempt_count));
  constexpr std::uint32_t status_v2_size =
    static_cast<std::uint32_t>(offsetof(rch_camera_status_v1, direct_frame_consumer_count));

  const bool status_v1_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V1 && status_size >= status_v1_size;
  const bool status_v2_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V2 && status_size >= status_v2_size;
  const bool status_v3_ok =
    status_version == RCH_CAMERA_STATUS_VERSION_V3 && status_size >= sizeof(rch_camera_status_v1);
  if (!status_v1_ok && !status_v2_ok && !status_v3_ok) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    rch_camera_status_v1 full_status{};
    std::unique_lock lifecycle_lock(entry->lifecycle_mutex);
    const auto generation = entry->generation.load(std::memory_order_acquire);
    if (!CanProceedWithEntry(entry, generation)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }
    entry->ingest->FillStatus(full_status);
    full_status.direct_frame_consumer_count = entry->direct_consumer_count.load(std::memory_order_acquire);
    full_status.bound_view_source_count = entry->view_binding_count.load(std::memory_order_acquire);
    full_status.total_frame_consumer_count =
      full_status.direct_frame_consumer_count + full_status.bound_view_source_count;
    full_status.reserved_v3 = 0;
    full_status.struct_size = status_v3_ok
      ? static_cast<uint32_t>(sizeof(rch_camera_status_v1))
      : (status_v2_ok ? status_v2_size : status_v1_size);
    full_status.struct_version = status_v3_ok
      ? RCH_CAMERA_STATUS_VERSION_V3
      : (status_v2_ok ? RCH_CAMERA_STATUS_VERSION_V2 : RCH_CAMERA_STATUS_VERSION_V1);

    const auto bytes_to_copy = status_v3_ok
      ? sizeof(rch_camera_status_v1)
      : (status_v2_ok ? status_v2_size : status_v1_size);
    std::memcpy(out_status, &full_status, bytes_to_copy);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_camera_enumerate_ids(
  rch_engine_handle engine,
  char* out_ids_utf8_buffer,
  uint32_t out_ids_utf8_buffer_size,
  uint32_t* out_required_buffer_size,
  uint32_t* out_camera_count) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_required_buffer_size == nullptr || out_camera_count == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }
  if (out_ids_utf8_buffer == nullptr && out_ids_utf8_buffer_size != 0U) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    const auto cameras = SnapshotSortedCameras(engine->registry_);
    std::size_t required_size = 0;
    for (const auto& [camera_id, entry] : cameras) {
      (void)entry;
      required_size += camera_id.size() + 1U;
    }

    if (required_size > static_cast<std::size_t>(std::numeric_limits<uint32_t>::max())) {
      return RCH_RESULT_OUT_OF_MEMORY;
    }

    *out_camera_count = static_cast<uint32_t>(cameras.size());
    *out_required_buffer_size = static_cast<uint32_t>(required_size);

    if (out_ids_utf8_buffer == nullptr && out_ids_utf8_buffer_size == 0U) {
      return RCH_RESULT_OK;
    }

    if (required_size == 0U) {
      return RCH_RESULT_OK;
    }

    if (out_ids_utf8_buffer_size < required_size || out_ids_utf8_buffer == nullptr) {
      return RCH_RESULT_BUFFER_TOO_SMALL;
    }

    std::size_t write_offset = 0;
    for (const auto& [camera_id, entry] : cameras) {
      (void)entry;
      const auto camera_id_size = camera_id.size() + 1U;
      std::memcpy(out_ids_utf8_buffer + write_offset, camera_id.c_str(), camera_id_size);
      write_offset += camera_id_size;
    }

    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_engine_get_diagnostics(
  rch_engine_handle engine,
  rch_engine_diagnostics_v1* out_diagnostics) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_diagnostics == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  constexpr std::uint32_t diagnostics_v1_size =
    static_cast<std::uint32_t>(offsetof(rch_engine_diagnostics_v1, view_count));
  const bool diagnostics_v1_ok =
    out_diagnostics->struct_version == RCH_ENGINE_DIAGNOSTICS_VERSION_V1
    && out_diagnostics->struct_size >= diagnostics_v1_size;
  const bool diagnostics_v2_ok =
    out_diagnostics->struct_version == RCH_ENGINE_DIAGNOSTICS_VERSION_V2
    && out_diagnostics->struct_size >= sizeof(rch_engine_diagnostics_v1);
  if (!diagnostics_v1_ok && !diagnostics_v2_ok) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    rch_engine_diagnostics_v1 diagnostics{};
    diagnostics.struct_size = diagnostics_v2_ok
      ? static_cast<uint32_t>(sizeof(rch_engine_diagnostics_v1))
      : diagnostics_v1_size;
    diagnostics.struct_version = diagnostics_v2_ok
      ? RCH_ENGINE_DIAGNOSTICS_VERSION_V2
      : RCH_ENGINE_DIAGNOSTICS_VERSION_V1;

    const auto cameras = SnapshotSortedCameras(engine->registry_);
    const auto views = SnapshotViews(engine->registry_);

    for (const auto& [camera_id, entry] : cameras) {
      (void)camera_id;
      if (entry == nullptr || entry->ingest == nullptr) {
        continue;
      }

      std::unique_lock lifecycle_lock(entry->lifecycle_mutex);
      if (entry->removed.load(std::memory_order_acquire)) {
        continue;
      }

      rch_camera_status_v1 status{};
      entry->ingest->FillStatus(status);
      diagnostics.configured_camera_count += 1U;
      diagnostics.active_rtsp_session_total += status.active_rtsp_session_count;
      diagnostics.active_decoder_total += status.active_decoder_count;
      diagnostics.successful_reconnect_total += status.successful_reconnect_count;
      diagnostics.direct_frame_consumer_count +=
        entry->direct_consumer_count.load(std::memory_order_acquire);
      diagnostics.total_bound_view_source_count +=
        entry->view_binding_count.load(std::memory_order_acquire);

      switch (status.state) {
        case RCH_CAMERA_STATE_STARTING:
          diagnostics.cameras_starting_count += 1U;
          break;
        case RCH_CAMERA_STATE_RECEIVING:
          diagnostics.cameras_receiving_count += 1U;
          break;
        case RCH_CAMERA_STATE_WAITING_TO_RETRY:
          diagnostics.cameras_waiting_to_retry_count += 1U;
          break;
        case RCH_CAMERA_STATE_FAILED:
          diagnostics.cameras_failed_count += 1U;
          break;
        case RCH_CAMERA_STATE_STOPPED:
        case RCH_CAMERA_STATE_STOPPING:
        default:
          diagnostics.cameras_stopped_count += 1U;
          break;
      }
    }

    diagnostics.view_count = static_cast<std::uint32_t>(views.size());
    diagnostics.reserved_v2 = 0;

    const auto bytes_to_copy = diagnostics_v2_ok
      ? sizeof(diagnostics)
      : diagnostics_v1_size;
    std::memcpy(out_diagnostics, &diagnostics, bytes_to_copy);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_frame_consumer_create(
  rch_engine_handle engine,
  const char* camera_id_utf8,
  rch_frame_consumer_handle* out_consumer) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_consumer == nullptr || !IsValidCameraIdUtf8(camera_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  *out_consumer = nullptr;
  if (!IsRegistryActive(engine->registry_)) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    const auto entry = FindCameraById(engine->registry_, camera_id_utf8);
    if (entry == nullptr || entry->ingest == nullptr || entry->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }

    auto* consumer = new rch_frame_consumer();
    consumer->registry = engine->registry_;
    consumer->camera = entry;
    entry->direct_consumer_count.fetch_add(1U, std::memory_order_acq_rel);
    *out_consumer = consumer;
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_frame_consumer_destroy(
  rch_frame_consumer_handle consumer) noexcept
{
  if (consumer == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (consumer->destroyed.exchange(true, std::memory_order_acq_rel)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    if (auto entry = consumer->camera.lock(); entry != nullptr) {
      DecrementIfPositive(entry->direct_consumer_count);
    }

    delete consumer;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_frame_consumer_acquire_latest(
  rch_frame_consumer_handle consumer,
  rch_frame_lease_handle* out_lease) noexcept
{
  if (consumer == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_lease == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  *out_lease = nullptr;
  try {
    if (consumer->destroyed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (!IsRegistryActive(consumer->registry)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    const auto entry = consumer->camera.lock();
    if (entry == nullptr || entry->ingest == nullptr || entry->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }

    auto* lease = new rch_frame_lease();
    lease->registry = consumer->registry;
    lease->lease = entry->ingest->AcquireLatestFrameLease();
    *out_lease = lease;
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_frame_lease_get_status(
  rch_frame_lease_handle lease,
  rch_frame_lease_status_v1* out_status) noexcept
{
  if (lease == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_status == nullptr || out_status->struct_version != RCH_FRAME_LEASE_STATUS_VERSION_V1
      || out_status->struct_size < sizeof(rch_frame_lease_status_v1)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    if (lease->destroyed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    rch_frame_lease_status_v1 status{};
    status.struct_size = static_cast<std::uint32_t>(sizeof(status));
    status.struct_version = RCH_FRAME_LEASE_STATUS_VERSION_V1;
    status.has_frame = lease->lease.has_frame ? 1U : 0U;
    status.width = lease->lease.width;
    status.height = lease->lease.height;
    status.reserved = 0;
    status.decoded_frame_count = lease->lease.frame_count;
    status.latest_frame_sequence = lease->lease.sequence;
    status.latest_frame_timestamp_ns = lease->lease.timestamp_ns;
    status.latest_frame_age_ms = lease->lease.has_frame ? lease->lease.age_ms : RCH_NO_FRAME_AGE_MS;
    std::memcpy(out_status, &status, sizeof(status));
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_frame_lease_destroy(
  rch_frame_lease_handle lease) noexcept
{
  if (lease == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (lease->destroyed.exchange(true, std::memory_order_acq_rel)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    lease->lease = robocamhub::frames::LatestFrameLease{};
    delete lease;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_create(
  rch_engine_handle engine,
  const char* view_id_utf8,
  rch_view_handle* out_view) noexcept
{
  if (engine == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_view == nullptr || !IsValidCameraIdUtf8(view_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  *out_view = nullptr;
  if (!IsRegistryActive(engine->registry_)) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    std::shared_ptr<ViewState> view_state;
    {
      std::lock_guard lock(engine->registry_->view_registry_mutex_);
      const auto duplicate = engine->registry_->views_.find(view_id_utf8);
      if (duplicate != engine->registry_->views_.end()) {
        return RCH_RESULT_INVALID_ARGUMENT;
      }

      view_state = std::make_shared<ViewState>(engine->registry_, view_id_utf8);
      engine->registry_->views_[view_id_utf8] = view_state;
    }

    auto* view = new rch_view();
    view->state = std::move(view_state);
    *out_view = view;
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_destroy(
  rch_view_handle view) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (view->destroyed.exchange(true, std::memory_order_acq_rel)) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (view->state != nullptr) {
      auto registry = view->state->registry;
      {
        std::lock_guard lock(registry->view_registry_mutex_);
        auto found = registry->views_.find(view->state->view_id);
        if (found != registry->views_.end() && found->second == view->state) {
          registry->views_.erase(found);
        }
      }
      view->state->removed.store(true, std::memory_order_release);
      ReleaseAllViewBindings(*view->state);
    }
    view->state.reset();
    delete view;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_bind_camera_source(
  rch_view_handle view,
  uint32_t slot_index,
  const char* camera_id_utf8) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (slot_index >= RCH_VIEW_MAX_SOURCE_SLOTS || !IsValidCameraIdUtf8(camera_id_utf8)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    if (view->destroyed.load(std::memory_order_acquire) || view->state == nullptr) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (!IsRegistryActive(view->state->registry) || view->state->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    const auto entry = FindCameraById(view->state->registry, camera_id_utf8);
    if (entry == nullptr || entry->ingest == nullptr || entry->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_NOT_CONFIGURED;
    }

    std::lock_guard lock(view->state->mutex);
    auto& slot = view->state->sources[slot_index];
    ReleaseViewBinding(slot);

    ViewSourceBinding binding{};
    binding.camera_id = camera_id_utf8;
    binding.camera = entry;
    binding.last_observed_sequence = 0;
    slot = std::move(binding);
    entry->view_binding_count.fetch_add(1U, std::memory_order_acq_rel);
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_unbind_source(
  rch_view_handle view,
  uint32_t slot_index) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (slot_index >= RCH_VIEW_MAX_SOURCE_SLOTS) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    if (view->destroyed.load(std::memory_order_acquire) || view->state == nullptr) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    std::lock_guard lock(view->state->mutex);
    auto& slot = view->state->sources[slot_index];
    ReleaseViewBinding(slot);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_get_status(
  rch_view_handle view,
  rch_view_status_v1* out_status) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_status == nullptr || out_status->struct_version != RCH_VIEW_STATUS_VERSION_V1
      || out_status->struct_size < sizeof(rch_view_status_v1)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    if (view->destroyed.load(std::memory_order_acquire) || view->state == nullptr) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (!IsRegistryActive(view->state->registry)
        || view->state->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    rch_view_status_v1 status{};
    status.struct_size = static_cast<std::uint32_t>(sizeof(status));
    status.struct_version = RCH_VIEW_STATUS_VERSION_V1;

    std::lock_guard lock(view->state->mutex);
    for (auto& source : view->state->sources) {
      if (!source.has_value()) {
        continue;
      }

      ++status.bound_source_count;
      auto camera = source->camera.lock();
      if (camera == nullptr || camera->ingest == nullptr || camera->removed.load(std::memory_order_acquire)) {
        ++status.stale_or_missing_source_count;
        continue;
      }

      const auto lease = camera->ingest->AcquireLatestFrameLease();
      if (lease.has_frame) {
        ++status.sources_with_frame_count;
        source->last_observed_sequence = lease.sequence;
        if (lease.sequence > status.last_observed_source_sequence) {
          status.last_observed_source_sequence = lease.sequence;
        }
      } else {
        ++status.stale_or_missing_source_count;
      }
    }

    status.reserved = 0;
    std::memcpy(out_status, &status, sizeof(status));
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}
