#include "ndi/ndi_sender_backend.h"

#include <gst/gst.h>

#include <chrono>
#include <cstddef>
#include <limits>
#include <mutex>
#include <new>

#if defined(RCH_HAS_NDI_SDK)
#include <Processing.NDI.Lib.h>
#endif

namespace robocamhub::ndi {
namespace {

#if defined(RCH_HAS_NDI_SDK)

class NdiRuntime final {
public:
  [[nodiscard]] bool Acquire() noexcept
  {
    try {
      const std::lock_guard lock(mutex_);
      if (reference_count_ == 0U && !NDIlib_initialize()) {
        return false;
      }
      ++reference_count_;
      return true;
    } catch (...) {
      return false;
    }
  }

  void Release() noexcept
  {
    try {
      const std::lock_guard lock(mutex_);
      if (reference_count_ == 0U) {
        return;
      }
      --reference_count_;
      if (reference_count_ == 0U) {
        NDIlib_destroy();
      }
    } catch (...) {
      // Runtime release is best-effort during destruction and cannot cross the C ABI.
    }
  }

private:
  std::mutex mutex_;
  std::size_t reference_count_{0};
};

NdiRuntime& Runtime()
{
  static NdiRuntime runtime;
  return runtime;
}

struct OfficialSenderBackend final {
  NDIlib_send_instance_t sender{nullptr};
  std::chrono::steady_clock::time_point last_receiver_poll{};
  std::uint32_t receiver_count{0U};
  bool receiver_count_known{false};
};

#endif

}  // namespace

void* CreateOfficialSenderBackend(const char* sender_name_utf8) noexcept
{
#if defined(RCH_HAS_NDI_SDK)
  if (sender_name_utf8 == nullptr || sender_name_utf8[0] == '\0' || !Runtime().Acquire()) {
    return nullptr;
  }

  auto* backend = new (std::nothrow) OfficialSenderBackend();
  if (backend == nullptr) {
    Runtime().Release();
    return nullptr;
  }

  NDIlib_send_create_t settings{};
  settings.p_ndi_name = sender_name_utf8;
  settings.p_groups = nullptr;
  settings.clock_video = true;
  settings.clock_audio = false;
  backend->sender = NDIlib_send_create(&settings);
  if (backend->sender == nullptr) {
    delete backend;
    Runtime().Release();
    return nullptr;
  }
  return backend;
#else
  static_cast<void>(sender_name_utf8);
  return nullptr;
#endif
}

void DestroyOfficialSenderBackend(void* context) noexcept
{
#if defined(RCH_HAS_NDI_SDK)
  auto* backend = static_cast<OfficialSenderBackend*>(context);
  if (backend == nullptr) {
    return;
  }
  if (backend->sender != nullptr) {
    NDIlib_send_destroy(backend->sender);
  }
  delete backend;
  Runtime().Release();
#else
  static_cast<void>(context);
#endif
}

SenderBackendSendResult SendOfficialFrame(
  void* context,
  const frames::LatestFrameLease& lease) noexcept
{
  SenderBackendSendResult result{};
#if defined(RCH_HAS_NDI_SDK)
  auto* backend = static_cast<OfficialSenderBackend*>(context);
  if (backend == nullptr || backend->sender == nullptr || !lease.has_frame || lease.sample() == nullptr
      || lease.width == 0U || lease.height == 0U
      || lease.width > static_cast<std::uint32_t>(std::numeric_limits<int>::max())
      || lease.height > static_cast<std::uint32_t>(std::numeric_limits<int>::max())) {
    result.result = RCH_RESULT_INVALID_ARGUMENT;
    return result;
  }

  auto* caps = gst_sample_get_caps(lease.sample());
  auto* buffer = gst_sample_get_buffer(lease.sample());
  if (caps == nullptr || gst_caps_is_empty(caps) || buffer == nullptr) {
    result.result = RCH_RESULT_INVALID_ARGUMENT;
    return result;
  }

  const auto* structure = gst_caps_get_structure(caps, 0);
  const auto* format = gst_structure_get_string(structure, "format");
  int width = 0;
  int height = 0;
  if (format == nullptr || g_ascii_strcasecmp(format, "RGBA") != 0
      || !gst_structure_get_int(structure, "width", &width)
      || !gst_structure_get_int(structure, "height", &height)
      || width != static_cast<int>(lease.width) || height != static_cast<int>(lease.height)) {
    result.result = RCH_RESULT_INVALID_ARGUMENT;
    return result;
  }

  const auto stride = static_cast<std::size_t>(lease.width) * 4U;
  if (stride > static_cast<std::size_t>(std::numeric_limits<int>::max())
      || lease.height > std::numeric_limits<std::size_t>::max() / stride) {
    result.result = RCH_RESULT_INVALID_ARGUMENT;
    return result;
  }

  GstMapInfo map{};
  if (gst_buffer_map(buffer, &map, GST_MAP_READ) == FALSE) {
    result.result = RCH_RESULT_INTERNAL_ERROR;
    return result;
  }

  const auto required_size = stride * static_cast<std::size_t>(lease.height);
  if (map.size < required_size) {
    gst_buffer_unmap(buffer, &map);
    result.result = RCH_RESULT_INVALID_ARGUMENT;
    return result;
  }

  NDIlib_video_frame_v2_t frame{};
  frame.xres = width;
  frame.yres = height;
  frame.FourCC = NDIlib_FourCC_video_type_RGBA;
  frame.frame_rate_N = 60;
  frame.frame_rate_D = 1;
  frame.picture_aspect_ratio = static_cast<float>(width) / static_cast<float>(height);
  frame.frame_format_type = NDIlib_frame_format_type_progressive;
  frame.timecode = NDIlib_send_timecode_synthesize;
  frame.p_data = map.data;
  frame.line_stride_in_bytes = static_cast<int>(stride);
  frame.p_metadata = nullptr;
  frame.timestamp = 0;

  NDIlib_send_send_video_v2(backend->sender, &frame);
  const auto now = std::chrono::steady_clock::now();
  if (!backend->receiver_count_known
      || now - backend->last_receiver_poll >= std::chrono::seconds(1)) {
    const int receiver_count = NDIlib_send_get_no_connections(backend->sender, 0U);
    backend->receiver_count_known = receiver_count >= 0;
    backend->receiver_count = receiver_count <= 0
      ? 0U
      : static_cast<std::uint32_t>(receiver_count);
    backend->last_receiver_poll = now;
  }
  gst_buffer_unmap(buffer, &map);

  result.accepted = true;
  result.result = RCH_RESULT_OK;
  result.receiver_count_known = backend->receiver_count_known;
  result.receiver_count = backend->receiver_count;
  return result;
#else
  static_cast<void>(context);
  static_cast<void>(lease);
  result.result = RCH_RESULT_INTERNAL_ERROR;
  return result;
#endif
}

}  // namespace robocamhub::ndi
