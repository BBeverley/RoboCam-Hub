#include "robocamhub_native.h"

#include "frames/latest_frame.h"
#include "ingest/single_camera_ingest.h"
#include "ndi/ndi_sender_backend.h"

#include <gst/gst.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstring>
#include <deque>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <optional>
#include <string>
#include <thread>
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

struct ViewSlotFreezeCache final {
  std::string camera_id;
  std::vector<std::uint8_t> rgba;
  std::uint64_t last_sequence{0};
  bool has_frame{false};
};

struct ViewSlotDiagnostics final {
  std::atomic<std::uint32_t> source_state{RCH_VIEW_SOURCE_STATE_UNBOUND};
  std::atomic<std::uint64_t> latest_sequence{0};
  std::atomic<std::uint8_t> freeze_cache_has_frame{0};
};

struct ViewRenderStats final {
  std::uint64_t render_frame_count{0};
  std::uint64_t latest_composed_frame_sequence{0};
  std::uint64_t last_observed_source_sequence{0};
  std::uint32_t bound_source_count{0};
  std::uint32_t sources_with_frame_count{0};
  std::uint32_t stale_or_missing_source_count{0};
  std::uint32_t live_source_count{0};
  std::uint32_t waiting_for_first_frame_count{0};
  std::uint32_t frozen_source_count{0};
  std::uint32_t reconnecting_source_count{0};
  std::uint32_t sources_contributing_count{0};
  std::uint32_t render_fps_milli{0};
  std::uint32_t last_render_duration_us{0};
  std::uint32_t average_render_duration_us{0};
  std::uint32_t p95_render_duration_us{0};
  std::uint32_t stale_source_frame_count{0};
  std::uint64_t render_deadline_miss_count{0};
  std::uint64_t last_render_deadline_miss_us{0};
  std::uint64_t last_render_deadline_miss_sequence{0};
};

constexpr std::uint32_t kViewComposedWidth = 1920;
constexpr std::uint32_t kViewComposedHeight = 1080;
constexpr std::uint32_t kViewTargetFps = 60;
constexpr std::size_t kGate3BViewSourceSlots = 4;
constexpr std::size_t kViewComposedStride = static_cast<std::size_t>(kViewComposedWidth) * 4U;
constexpr std::size_t kQuadrantWidth = kViewComposedWidth / 2U;
constexpr std::size_t kQuadrantHeight = kViewComposedHeight / 2U;
constexpr std::size_t kQuadrantStride = kQuadrantWidth * 4U;
constexpr std::size_t kQuadrantPixels = kQuadrantWidth * kQuadrantHeight * 4U;

static_assert(kGate3BViewSourceSlots <= RCH_VIEW_MAX_SOURCE_SLOTS,
              "fixed compositor slot count must not exceed ABI slot ceiling");

std::uint64_t MonotonicTimeNs()
{
  return static_cast<std::uint64_t>(g_get_monotonic_time()) * UINT64_C(1000);
}

struct ViewState final {
  explicit ViewState(std::shared_ptr<EngineRegistry> owner, std::string id)
      : registry(std::move(owner)), view_id(std::move(id))
  {
    composed_caps = gst_caps_new_simple(
      "video/x-raw",
      "format", G_TYPE_STRING, "RGBA",
      "width", G_TYPE_INT, static_cast<int>(kViewComposedWidth),
      "height", G_TYPE_INT, static_cast<int>(kViewComposedHeight),
      nullptr);
    composed_pixels.resize(static_cast<std::size_t>(kViewComposedHeight) * kViewComposedStride, 0U);
    for (auto& cache : slot_freeze_cache) {
      cache.rgba.resize(kQuadrantPixels, 0U);
    }
    for (auto& slot : slot_diagnostics) {
      slot.source_state.store(RCH_VIEW_SOURCE_STATE_UNBOUND, std::memory_order_release);
      slot.latest_sequence.store(0, std::memory_order_release);
      slot.freeze_cache_has_frame.store(0, std::memory_order_release);
    }
  }

  ~ViewState()
  {
    stop_requested.store(true, std::memory_order_release);
    if (render_thread.joinable()) {
      render_thread.join();
    }
    latest_composed_frame.Clear();
    if (composed_caps != nullptr) {
      gst_caps_unref(composed_caps);
      composed_caps = nullptr;
    }
  }

  std::shared_ptr<EngineRegistry> registry;
  std::string view_id;
  std::mutex mutex;
  std::array<std::optional<ViewSourceBinding>, RCH_VIEW_MAX_SOURCE_SLOTS> sources{};
  std::array<ViewSlotFreezeCache, kGate3BViewSourceSlots> slot_freeze_cache{};
  std::array<ViewSlotDiagnostics, kGate3BViewSourceSlots> slot_diagnostics{};
  std::vector<std::uint8_t> composed_pixels;
  GstCaps* composed_caps{nullptr};
  robocamhub::frames::LatestFrame latest_composed_frame;
  std::thread render_thread;
  std::atomic<bool> stop_requested{false};
  std::atomic<bool> render_running{false};
  std::atomic<std::uint32_t> output_consumer_count{0};
  std::mutex stats_mutex;
  ViewRenderStats stats{};
  std::deque<std::uint32_t> recent_render_durations_us;
  std::chrono::steady_clock::time_point fps_window_start{std::chrono::steady_clock::now()};
  std::uint64_t fps_window_frames{0};
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

struct rch_view_frame_lease final {
  std::shared_ptr<ViewState> owner;
  robocamhub::frames::LatestFrameLease lease;
  std::atomic<bool> destroyed{false};
};

bool IsRegistryActive(const std::shared_ptr<EngineRegistry>& registry);
void DecrementIfPositive(std::atomic<std::uint32_t>& counter);

using SenderBackendSendResult = robocamhub::ndi::SenderBackendSendResult;
using SenderBackendSendFn = SenderBackendSendResult (*)(
  void* context,
  const robocamhub::frames::LatestFrameLease& lease) noexcept;
using SenderBackendDestroyFn = void (*)(void* context) noexcept;

struct SenderBackendDispatch final {
  void* context{nullptr};
  SenderBackendSendFn send{nullptr};
  SenderBackendDestroyFn destroy{nullptr};
  bool is_official_sdk{false};
};

struct rch_ndi_sender final {
  std::weak_ptr<ViewState> view;
  std::string sender_name;
  std::thread worker;
  std::mutex mutex;
  std::atomic<bool> destroyed{false};
  std::atomic<bool> stop_requested{false};
  std::atomic<bool> running{false};
  std::atomic<uint32_t> state{RCH_NDI_SENDER_STATE_STOPPED};
  std::atomic<std::uint64_t> sent_frame_count{0};
  std::atomic<std::uint64_t> latest_sent_sequence{0};
  std::atomic<std::uint64_t> latest_sent_frame_age_ms{RCH_NO_FRAME_AGE_MS};
  std::atomic<std::uint32_t> last_result{RCH_RESULT_OK};
  std::atomic<std::uint64_t> dropped_or_skipped_frame_count{0};
  std::atomic<std::uint32_t> last_send_duration_us{0};
  std::atomic<std::uint32_t> average_send_duration_us{0};
  std::atomic<std::uint32_t> p95_send_duration_us{0};
  std::atomic<std::uint32_t> send_fps_milli{0};
  std::atomic<std::uint32_t> receiver_count{0};
  std::atomic<std::uint32_t> receiver_count_known{0};
  std::atomic<std::uint64_t> worker_tick_count{0};
  std::atomic<std::uint64_t> unique_sequence_observed_count{0};
  std::atomic<std::uint64_t> duplicate_sequence_tick_count{0};
  std::atomic<std::uint64_t> latest_observed_sequence{0};
  std::atomic<bool> has_observed_sequence{false};
  std::chrono::steady_clock::time_point last_loop_start{std::chrono::steady_clock::now()};
  std::vector<std::uint32_t> recent_send_durations_us{};
  std::deque<std::chrono::steady_clock::time_point> accepted_send_times{};
  SenderBackendDispatch backend{};
#if defined(RCH_NDI_SENDER_TESTING)
  std::atomic<std::uint32_t> test_backend_delay_ms{0};
#endif
};

namespace {

bool IsValidLabelUtf8(const char* value, std::size_t max_length)
{
  return value != nullptr && value[0] != '\0' && std::strlen(value) <= max_length
    && g_utf8_validate(value, -1, nullptr) != FALSE;
}

#if !defined(RCH_HAS_NDI_SDK)
SenderBackendSendResult DeterministicSenderBackendSend(
  void* context,
  const robocamhub::frames::LatestFrameLease& lease) noexcept
{
  static_cast<void>(context);
  SenderBackendSendResult result{};
  if (!lease.has_frame || lease.sample() == nullptr) {
    result.accepted = false;
    result.result = RCH_RESULT_INVALID_ARGUMENT;
    return result;
  }

  result.accepted = true;
  result.result = RCH_RESULT_OK;
  result.receiver_count_known = false;
  result.receiver_count = 0U;
  return result;
}
#endif

void UpdateAcceptedSendRate(rch_ndi_sender& sender, std::chrono::steady_clock::time_point now)
{
  std::lock_guard lock(sender.mutex);
  sender.accepted_send_times.push_back(now);
  constexpr auto window = std::chrono::seconds(2);
  const auto floor = now - window;
  while (!sender.accepted_send_times.empty() && sender.accepted_send_times.front() < floor) {
    sender.accepted_send_times.pop_front();
  }

  if (sender.accepted_send_times.size() < 2U) {
    sender.send_fps_milli.store(0U, std::memory_order_release);
    return;
  }

  const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
    sender.accepted_send_times.back() - sender.accepted_send_times.front()).count();
  if (elapsed < 500) {
    sender.send_fps_milli.store(0U, std::memory_order_release);
    return;
  }

  const auto sample_count = static_cast<std::uint64_t>(sender.accepted_send_times.size());
  const auto fps_milli = static_cast<std::uint32_t>((sample_count * 1000000ULL)
                                                    / static_cast<std::uint64_t>(elapsed));
  sender.send_fps_milli.store(fps_milli, std::memory_order_release);
}

void UpdateSendDurationStats(rch_ndi_sender& sender, std::uint32_t duration_us)
{
  std::lock_guard lock(sender.mutex);
  sender.recent_send_durations_us.push_back(duration_us);
  constexpr std::size_t max_window = 128;
  if (sender.recent_send_durations_us.size() > max_window) {
    sender.recent_send_durations_us.erase(sender.recent_send_durations_us.begin());
  }

  std::uint64_t sum = 0;
  std::vector<std::uint32_t> sorted = sender.recent_send_durations_us;
  std::sort(sorted.begin(), sorted.end());
  for (const auto sample : sender.recent_send_durations_us) {
    sum += sample;
  }

  sender.last_send_duration_us.store(duration_us, std::memory_order_release);
  sender.average_send_duration_us.store(
    sorted.empty() ? 0U : static_cast<std::uint32_t>(sum / static_cast<std::uint64_t>(sorted.size())),
    std::memory_order_release);
  if (!sorted.empty()) {
    const auto p95_index = static_cast<std::size_t>((sorted.size() - 1U) * 95U / 100U);
    sender.p95_send_duration_us.store(sorted[p95_index], std::memory_order_release);
  } else {
    sender.p95_send_duration_us.store(0U, std::memory_order_release);
  }
}

void NdiSenderWorker(const std::shared_ptr<ViewState>& state, rch_ndi_sender* sender)
{
  if (state == nullptr || sender == nullptr) {
    return;
  }

  sender->state.store(RCH_NDI_SENDER_STATE_STARTING, std::memory_order_release);
  sender->running.store(true, std::memory_order_release);

  const auto tick_period = std::chrono::microseconds(1000000 / kViewTargetFps);
  auto next_tick = std::chrono::steady_clock::now();

  while (!sender->stop_requested.load(std::memory_order_acquire)) {
    const auto start = std::chrono::steady_clock::now();
    sender->last_loop_start = start;
    sender->worker_tick_count.fetch_add(1U, std::memory_order_acq_rel);

    if (state->stop_requested.load(std::memory_order_acquire) || state->removed.load(std::memory_order_acquire)) {
      sender->state.store(RCH_NDI_SENDER_STATE_FAILED, std::memory_order_release);
      sender->last_result.store(RCH_RESULT_INVALID_HANDLE, std::memory_order_release);
      break;
    }

    const auto lease = state->latest_composed_frame.AcquireLease();
    if (!lease.has_frame || lease.sample() == nullptr) {
      sender->state.store(RCH_NDI_SENDER_STATE_WAITING_FOR_VIEW_FRAME, std::memory_order_release);
      sender->latest_sent_frame_age_ms.store(RCH_NO_FRAME_AGE_MS, std::memory_order_release);
      sender->dropped_or_skipped_frame_count.fetch_add(1U, std::memory_order_acq_rel);
      sender->last_result.store(RCH_RESULT_OK, std::memory_order_release);
      next_tick += tick_period;
      const auto now = std::chrono::steady_clock::now();
      if (next_tick > now) {
        std::this_thread::sleep_until(next_tick);
      } else {
        next_tick = now;
      }
      continue;
    }

    sender->state.store(RCH_NDI_SENDER_STATE_RUNNING, std::memory_order_release);
    const bool has_observed_sequence = sender->has_observed_sequence.load(std::memory_order_acquire);
    const auto previous_sequence = sender->latest_observed_sequence.load(std::memory_order_acquire);
    if (has_observed_sequence && lease.sequence <= previous_sequence) {
      sender->duplicate_sequence_tick_count.fetch_add(1U, std::memory_order_acq_rel);
      sender->dropped_or_skipped_frame_count.fetch_add(1U, std::memory_order_acq_rel);
      sender->last_result.store(RCH_RESULT_OK, std::memory_order_release);
      next_tick += tick_period;
      const auto now = std::chrono::steady_clock::now();
      if (next_tick > now) {
        std::this_thread::sleep_until(next_tick);
      } else {
        next_tick = now;
      }
      continue;
    }

    if (has_observed_sequence && lease.sequence > previous_sequence + 1U) {
      sender->dropped_or_skipped_frame_count.fetch_add(
        lease.sequence - previous_sequence - 1U,
        std::memory_order_acq_rel);
    }
    sender->latest_observed_sequence.store(lease.sequence, std::memory_order_release);
    sender->has_observed_sequence.store(true, std::memory_order_release);
    sender->unique_sequence_observed_count.fetch_add(1U, std::memory_order_acq_rel);

#if defined(RCH_NDI_SENDER_TESTING)
    const auto backend_delay = sender->test_backend_delay_ms.load(std::memory_order_acquire);
    if (backend_delay > 0U) {
      std::this_thread::sleep_for(std::chrono::milliseconds(backend_delay));
    }
#endif
    const auto backend_send = sender->backend.send == nullptr
      ? SenderBackendSendResult{}
      : sender->backend.send(sender->backend.context, lease);
    sender->last_result.store(backend_send.result, std::memory_order_release);
    if (!backend_send.accepted || backend_send.result != RCH_RESULT_OK) {
      sender->dropped_or_skipped_frame_count.fetch_add(1U, std::memory_order_acq_rel);
      sender->state.store(RCH_NDI_SENDER_STATE_FAILED, std::memory_order_release);
      sender->latest_sent_frame_age_ms.store(RCH_NO_FRAME_AGE_MS, std::memory_order_release);
    } else {
      sender->sent_frame_count.fetch_add(1U, std::memory_order_acq_rel);
      sender->latest_sent_sequence.store(lease.sequence, std::memory_order_release);
      sender->latest_sent_frame_age_ms.store(lease.age_ms, std::memory_order_release);
      sender->receiver_count_known.store(backend_send.receiver_count_known ? 1U : 0U, std::memory_order_release);
      sender->receiver_count.store(
        backend_send.receiver_count_known ? backend_send.receiver_count : 0U,
        std::memory_order_release);
      const auto duration_us = static_cast<std::uint32_t>(std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now() - start).count());
      UpdateSendDurationStats(*sender, duration_us);
      UpdateAcceptedSendRate(*sender, std::chrono::steady_clock::now());
    }

    next_tick += tick_period;
    const auto now = std::chrono::steady_clock::now();
    if (next_tick > now) {
      std::this_thread::sleep_until(next_tick);
    } else {
      next_tick = now;
    }
  }

  sender->running.store(false, std::memory_order_release);
  sender->state.store(RCH_NDI_SENDER_STATE_STOPPED, std::memory_order_release);
}

}  // namespace

extern "C" rch_result rch_ndi_sender_create(
  rch_view_handle view,
  const char* sender_name_utf8,
  rch_ndi_sender_handle* out_sender) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_sender == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }
  if (view->destroyed.load(std::memory_order_acquire) || view->state == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (!IsRegistryActive(view->state->registry) || view->state->removed.load(std::memory_order_acquire)) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  *out_sender = nullptr;
  try {
    const auto sender_name = sender_name_utf8 == nullptr || sender_name_utf8[0] == '\0'
      ? std::string("ROBOCAM - Gate4A")
      : std::string(sender_name_utf8);
    if (!IsValidLabelUtf8(sender_name.c_str(), 255U)) {
      return RCH_RESULT_INVALID_ARGUMENT;
    }

    auto* sender = new rch_ndi_sender();
    sender->view = view->state;
    sender->sender_name = sender_name;
    sender->stop_requested.store(false, std::memory_order_release);
    sender->running.store(false, std::memory_order_release);
    sender->state.store(RCH_NDI_SENDER_STATE_STOPPED, std::memory_order_release);
    sender->last_result.store(RCH_RESULT_OK, std::memory_order_release);
#if defined(RCH_HAS_NDI_SDK)
    sender->backend.context = robocamhub::ndi::CreateOfficialSenderBackend(sender->sender_name.c_str());
    if (sender->backend.context == nullptr) {
      delete sender;
      return RCH_RESULT_INTERNAL_ERROR;
    }
    sender->backend.send = robocamhub::ndi::SendOfficialFrame;
    sender->backend.destroy = robocamhub::ndi::DestroyOfficialSenderBackend;
    sender->backend.is_official_sdk = true;
#else
    sender->backend.send = DeterministicSenderBackendSend;
    sender->backend.is_official_sdk = false;
#endif
    view->state->output_consumer_count.fetch_add(1U, std::memory_order_acq_rel);
    *out_sender = sender;
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_ndi_sender_destroy(
  rch_ndi_sender_handle sender) noexcept
{
  if (sender == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (sender->destroyed.exchange(true, std::memory_order_acq_rel)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    sender->stop_requested.store(true, std::memory_order_release);
    if (sender->worker.joinable()) {
      sender->worker.join();
    }

    if (sender->backend.destroy != nullptr) {
      sender->backend.destroy(sender->backend.context);
      sender->backend.context = nullptr;
    }

    if (auto view = sender->view.lock(); view != nullptr) {
      DecrementIfPositive(view->output_consumer_count);
    }

    delete sender;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_ndi_sender_start(
  rch_ndi_sender_handle sender) noexcept
{
  if (sender == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (sender->destroyed.load(std::memory_order_acquire)) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (sender->running.load(std::memory_order_acquire) || sender->worker.joinable()) {
      return RCH_RESULT_ALREADY_STARTED;
    }
    if (auto view = sender->view.lock(); view == nullptr || view->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    sender->stop_requested.store(false, std::memory_order_release);
    {
      std::lock_guard lock(sender->mutex);
      sender->accepted_send_times.clear();
    }
    sender->send_fps_milli.store(0U, std::memory_order_release);
    sender->worker_tick_count.store(0U, std::memory_order_release);
    sender->unique_sequence_observed_count.store(0U, std::memory_order_release);
    sender->duplicate_sequence_tick_count.store(0U, std::memory_order_release);
    sender->latest_observed_sequence.store(0U, std::memory_order_release);
    sender->has_observed_sequence.store(false, std::memory_order_release);
    sender->dropped_or_skipped_frame_count.store(0U, std::memory_order_release);
    sender->sent_frame_count.store(0U, std::memory_order_release);
    sender->latest_sent_sequence.store(0U, std::memory_order_release);
    sender->latest_sent_frame_age_ms.store(RCH_NO_FRAME_AGE_MS, std::memory_order_release);
    sender->receiver_count.store(0U, std::memory_order_release);
    sender->receiver_count_known.store(0U, std::memory_order_release);
    sender->last_result.store(RCH_RESULT_OK, std::memory_order_release);
    sender->state.store(RCH_NDI_SENDER_STATE_STARTING, std::memory_order_release);
    sender->worker = std::thread(NdiSenderWorker, sender->view.lock(), sender);
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_ndi_sender_stop(
  rch_ndi_sender_handle sender) noexcept
{
  if (sender == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (sender->destroyed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    sender->stop_requested.store(true, std::memory_order_release);
    if (sender->worker.joinable()) {
      sender->worker.join();
    }
    sender->running.store(false, std::memory_order_release);
    sender->state.store(RCH_NDI_SENDER_STATE_STOPPED, std::memory_order_release);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_ndi_sender_get_status(
  rch_ndi_sender_handle sender,
  rch_ndi_sender_status_v1* out_status) noexcept
{
  if (sender == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  constexpr std::uint32_t sender_status_v1_size =
    static_cast<std::uint32_t>(offsetof(rch_ndi_sender_status_v1, worker_tick_count));
  const bool sender_status_v1_ok =
    out_status != nullptr
    && out_status->struct_version == RCH_NDI_SENDER_STATUS_VERSION_V1
    && out_status->struct_size >= sender_status_v1_size;
  const bool sender_status_v2_ok =
    out_status != nullptr
    && out_status->struct_version == RCH_NDI_SENDER_STATUS_VERSION_V2
    && out_status->struct_size >= sizeof(rch_ndi_sender_status_v1);
  if (!sender_status_v1_ok && !sender_status_v2_ok) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }
  if (sender->destroyed.load(std::memory_order_acquire)) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    rch_ndi_sender_status_v1 status{};
    status.struct_size = sender_status_v2_ok
      ? static_cast<std::uint32_t>(sizeof(status))
      : sender_status_v1_size;
    status.struct_version = sender_status_v2_ok
      ? RCH_NDI_SENDER_STATUS_VERSION_V2
      : RCH_NDI_SENDER_STATUS_VERSION_V1;
    std::lock_guard lock(sender->mutex);

    const auto view = sender->view.lock();
    if (view == nullptr || view->removed.load(std::memory_order_acquire)) {
      status.state = RCH_NDI_SENDER_STATE_FAILED;
      status.last_result = RCH_RESULT_INVALID_HANDLE;
      status.receiver_count_known = 0U;
      status.receiver_count = 0U;
      const std::size_t bytes_to_copy = sender_status_v2_ok
        ? sizeof(status)
        : static_cast<std::size_t>(sender_status_v1_size);
      std::memcpy(out_status, &status, bytes_to_copy);
      return RCH_RESULT_OK;
    }

    status.state = sender->state.load(std::memory_order_acquire);
    status.configured_width = kViewComposedWidth;
    status.configured_height = kViewComposedHeight;
    status.target_fps = kViewTargetFps;
    status.last_result = sender->last_result.load(std::memory_order_acquire);
    status.sent_frame_count = sender->sent_frame_count.load(std::memory_order_acquire);
    status.latest_sent_sequence = sender->latest_sent_sequence.load(std::memory_order_acquire);
    status.latest_sent_frame_age_ms = sender->latest_sent_frame_age_ms.load(std::memory_order_acquire);
    status.send_fps_milli = sender->send_fps_milli.load(std::memory_order_acquire);
    status.dropped_or_skipped_frame_count = sender->dropped_or_skipped_frame_count.load(std::memory_order_acquire);
    status.last_send_duration_us = sender->last_send_duration_us.load(std::memory_order_acquire);
    status.average_send_duration_us = sender->average_send_duration_us.load(std::memory_order_acquire);
    status.p95_send_duration_us = sender->p95_send_duration_us.load(std::memory_order_acquire);
    status.receiver_count_known = sender->receiver_count_known.load(std::memory_order_acquire);
    status.receiver_count = status.receiver_count_known != 0U
      ? sender->receiver_count.load(std::memory_order_acquire)
      : 0U;
    std::memset(status.sender_name_utf8, 0, sizeof(status.sender_name_utf8));
    const auto copy_count = std::min<std::size_t>(sender->sender_name.size(), sizeof(status.sender_name_utf8) - 1U);
    std::memcpy(status.sender_name_utf8, sender->sender_name.data(), copy_count);
    status.sender_name_utf8[copy_count] = '\0';
    status.reserved = 0U;
    status.worker_tick_count = sender->worker_tick_count.load(std::memory_order_acquire);
    status.unique_sequence_observed_count = sender->unique_sequence_observed_count.load(std::memory_order_acquire);
    status.duplicate_sequence_tick_count = sender->duplicate_sequence_tick_count.load(std::memory_order_acquire);
    status.reserved_v2 = sender->backend.is_official_sdk ? 1U : 0U;

    const std::size_t bytes_to_copy = sender_status_v2_ok
      ? sizeof(status)
      : static_cast<std::size_t>(sender_status_v1_size);
    std::memcpy(out_status, &status, bytes_to_copy);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

#if defined(RCH_NDI_SENDER_TESTING)
namespace robocamhub::testing {

rch_result SetNdiSenderBackendDelay(
  rch_ndi_sender_handle sender,
  std::uint32_t delay_ms) noexcept
{
  if (sender == nullptr || sender->destroyed.load(std::memory_order_acquire)) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (sender->running.load(std::memory_order_acquire) || sender->worker.joinable()) {
    return RCH_RESULT_INVALID_STATE;
  }
  sender->test_backend_delay_ms.store(delay_ms, std::memory_order_release);
  return RCH_RESULT_OK;
}

}  // namespace robocamhub::testing
#endif

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

void FillQuadrantPlaceholder(std::vector<std::uint8_t>& output,
                             std::size_t slot_index,
                             std::uint8_t r,
                             std::uint8_t g,
                             std::uint8_t b)
{
  const std::size_t origin_x = (slot_index % 2U) * kQuadrantWidth;
  const std::size_t origin_y = (slot_index / 2U) * kQuadrantHeight;
  for (std::size_t y = 0; y < kQuadrantHeight; ++y) {
    auto* row = output.data() + (origin_y + y) * kViewComposedStride + origin_x * 4U;
    for (std::size_t x = 0; x < kQuadrantWidth; ++x) {
      row[x * 4U + 0U] = r;
      row[x * 4U + 1U] = g;
      row[x * 4U + 2U] = b;
      row[x * 4U + 3U] = UINT8_C(255);
    }
  }

  // Draw deterministic diagonal marker for easy quadrant identification.
  for (std::size_t y = 0; y < kQuadrantHeight; ++y) {
    const std::size_t x = (y * kQuadrantWidth) / kQuadrantHeight;
    auto* pixel = output.data() + (origin_y + y) * kViewComposedStride + (origin_x + x) * 4U;
    pixel[0] = UINT8_C(255);
    pixel[1] = UINT8_C(255);
    pixel[2] = UINT8_C(255);
    pixel[3] = UINT8_C(255);
  }
}

void BlitQuadrantFromCache(const ViewSlotFreezeCache& cache,
                           std::vector<std::uint8_t>& output,
                           std::size_t slot_index)
{
  const std::size_t origin_x = (slot_index % 2U) * kQuadrantWidth;
  const std::size_t origin_y = (slot_index / 2U) * kQuadrantHeight;
  for (std::size_t y = 0; y < kQuadrantHeight; ++y) {
    auto* dst = output.data() + (origin_y + y) * kViewComposedStride + origin_x * 4U;
    const auto* src = cache.rgba.data() + y * kQuadrantStride;
    std::memcpy(dst, src, kQuadrantStride);
  }
}

bool CopyAndScaleRgbaToQuadrant(
  GstSample* sample,
  std::vector<std::uint8_t>& output,
  std::size_t slot_index,
  ViewSlotFreezeCache& freeze_cache)
{
  if (sample == nullptr) {
    return false;
  }

  auto* caps = gst_sample_get_caps(sample);
  if (caps == nullptr || gst_caps_is_empty(caps)) {
    return false;
  }
  const auto* structure = gst_caps_get_structure(caps, 0);
  const auto* format = gst_structure_get_string(structure, "format");
  if (format == nullptr || g_ascii_strcasecmp(format, "RGBA") != 0) {
    return false;
  }

  int src_width = 0;
  int src_height = 0;
  if (!gst_structure_get_int(structure, "width", &src_width)
      || !gst_structure_get_int(structure, "height", &src_height)
      || src_width <= 0
      || src_height <= 0) {
    return false;
  }

  auto* buffer = gst_sample_get_buffer(sample);
  if (buffer == nullptr) {
    return false;
  }

  GstMapInfo map{};
  if (!gst_buffer_map(buffer, &map, GST_MAP_READ)) {
    return false;
  }

  const std::size_t src_stride = static_cast<std::size_t>(src_width) * 4U;
  const std::size_t required_bytes = src_stride * static_cast<std::size_t>(src_height);
  if (map.size < required_bytes) {
    gst_buffer_unmap(buffer, &map);
    return false;
  }

  const std::size_t origin_x = (slot_index % 2U) * kQuadrantWidth;
  const std::size_t origin_y = (slot_index / 2U) * kQuadrantHeight;
  for (std::size_t y = 0; y < kQuadrantHeight; ++y) {
    const std::size_t source_y = y * static_cast<std::size_t>(src_height) / kQuadrantHeight;
    const auto* source_row = map.data + source_y * src_stride;
    auto* destination_row = output.data() + (origin_y + y) * kViewComposedStride + origin_x * 4U;
    auto* cache_row = freeze_cache.rgba.data() + y * kQuadrantStride;

    for (std::size_t x = 0; x < kQuadrantWidth; ++x) {
      const std::size_t source_x = x * static_cast<std::size_t>(src_width) / kQuadrantWidth;
      const auto* source_pixel = source_row + source_x * 4U;
      auto* destination_pixel = destination_row + x * 4U;
      auto* cache_pixel = cache_row + x * 4U;
      destination_pixel[0] = source_pixel[0];
      destination_pixel[1] = source_pixel[1];
      destination_pixel[2] = source_pixel[2];
      destination_pixel[3] = source_pixel[3];
      cache_pixel[0] = source_pixel[0];
      cache_pixel[1] = source_pixel[1];
      cache_pixel[2] = source_pixel[2];
      cache_pixel[3] = source_pixel[3];
    }
  }

  gst_buffer_unmap(buffer, &map);
  freeze_cache.has_frame = true;
  return true;
}

void PublishComposedFrame(ViewState& state)
{
  auto* buffer = gst_buffer_new_allocate(nullptr, state.composed_pixels.size(), nullptr);
  if (buffer == nullptr) {
    return;
  }

  gst_buffer_fill(buffer, 0, state.composed_pixels.data(), state.composed_pixels.size());
  GST_BUFFER_PTS(buffer) = MonotonicTimeNs();
  auto* sample = gst_sample_new(buffer, state.composed_caps, nullptr, nullptr);
  gst_buffer_unref(buffer);
  if (sample == nullptr) {
    return;
  }

  state.latest_composed_frame.Publish(sample);
  gst_sample_unref(sample);
}

void UpdateRenderDurationStats(ViewState& state, std::uint32_t duration_us)
{
  std::lock_guard lock(state.stats_mutex);
  state.recent_render_durations_us.push_back(duration_us);
  constexpr std::size_t max_window = 256;
  if (state.recent_render_durations_us.size() > max_window) {
    state.recent_render_durations_us.pop_front();
  }

  std::uint64_t sum = 0;
  std::vector<std::uint32_t> sorted;
  sorted.reserve(state.recent_render_durations_us.size());
  for (const auto sample : state.recent_render_durations_us) {
    sum += sample;
    sorted.push_back(sample);
  }
  std::sort(sorted.begin(), sorted.end());

  state.stats.last_render_duration_us = duration_us;
  state.stats.average_render_duration_us =
    sorted.empty() ? 0U : static_cast<std::uint32_t>(sum / sorted.size());
  if (!sorted.empty()) {
    const auto p95_index = static_cast<std::size_t>((sorted.size() - 1U) * 95U / 100U);
    state.stats.p95_render_duration_us = sorted[p95_index];
  } else {
    state.stats.p95_render_duration_us = 0U;
  }
}

void RenderViewLoop(const std::shared_ptr<ViewState>& state)
{
  state->render_running.store(true, std::memory_order_release);
  state->fps_window_start = std::chrono::steady_clock::now();
  state->fps_window_frames = 0;

  constexpr auto frame_period = std::chrono::microseconds(1000000 / kViewTargetFps);
  const auto frame_budget_us = static_cast<std::uint64_t>(frame_period.count());
  auto next_tick = std::chrono::steady_clock::now();

  while (!state->stop_requested.load(std::memory_order_acquire)) {
    const auto tick_start = std::chrono::steady_clock::now();

    std::array<std::optional<ViewSourceBinding>, kGate3BViewSourceSlots> active_sources{};
    {
      std::lock_guard lock(state->mutex);
      for (std::size_t i = 0; i < active_sources.size(); ++i) {
        active_sources[i] = state->sources[i];
      }
    }

    std::fill(state->composed_pixels.begin(), state->composed_pixels.end(), UINT8_C(0));
    ViewRenderStats tick_stats{};

    for (std::size_t slot = 0; slot < active_sources.size(); ++slot) {
      auto& freeze_cache = state->slot_freeze_cache[slot];
      auto& slot_diagnostics = state->slot_diagnostics[slot];
      const auto& binding = active_sources[slot];
      if (!binding.has_value()) {
        freeze_cache.camera_id.clear();
        freeze_cache.has_frame = false;
        freeze_cache.last_sequence = 0;
        slot_diagnostics.source_state.store(
          RCH_VIEW_SOURCE_STATE_UNBOUND,
          std::memory_order_release);
        slot_diagnostics.latest_sequence.store(0, std::memory_order_release);
        slot_diagnostics.freeze_cache_has_frame.store(0, std::memory_order_release);
        FillQuadrantPlaceholder(state->composed_pixels, slot, UINT8_C(32), UINT8_C(32), UINT8_C(32));
        continue;
      }

      ++tick_stats.bound_source_count;
      if (freeze_cache.camera_id != binding->camera_id) {
        freeze_cache.camera_id = binding->camera_id;
        freeze_cache.has_frame = false;
        freeze_cache.last_sequence = 0;
        slot_diagnostics.latest_sequence.store(0, std::memory_order_release);
        slot_diagnostics.freeze_cache_has_frame.store(0, std::memory_order_release);
      }

      auto camera = binding->camera.lock();
      if (camera == nullptr || camera->ingest == nullptr || camera->removed.load(std::memory_order_acquire)) {
        ++tick_stats.stale_or_missing_source_count;
        ++tick_stats.stale_source_frame_count;
        slot_diagnostics.latest_sequence.store(freeze_cache.last_sequence, std::memory_order_release);
        slot_diagnostics.freeze_cache_has_frame.store(
          freeze_cache.has_frame ? 1U : 0U,
          std::memory_order_release);
        slot_diagnostics.source_state.store(
          RCH_VIEW_SOURCE_STATE_MISSING_OR_STALE,
          std::memory_order_release);
        if (freeze_cache.has_frame) {
          BlitQuadrantFromCache(freeze_cache, state->composed_pixels, slot);
        } else {
          FillQuadrantPlaceholder(state->composed_pixels, slot, UINT8_C(48), UINT8_C(24), UINT8_C(24));
        }
        continue;
      }

      rch_camera_status_v1 camera_status{};
      camera_status.struct_size = static_cast<std::uint32_t>(sizeof(camera_status));
      camera_status.struct_version = RCH_CAMERA_STATUS_VERSION;
      camera->ingest->FillStatus(camera_status);

      const auto lease = camera->ingest->AcquireLatestFrameLease();
      if (lease.has_frame && lease.sample() != nullptr
          && CopyAndScaleRgbaToQuadrant(lease.sample(), state->composed_pixels, slot, freeze_cache)) {
        ++tick_stats.sources_with_frame_count;
        ++tick_stats.sources_contributing_count;
        ++tick_stats.live_source_count;
        freeze_cache.last_sequence = lease.sequence;
        slot_diagnostics.latest_sequence.store(lease.sequence, std::memory_order_release);
        slot_diagnostics.freeze_cache_has_frame.store(1U, std::memory_order_release);
        slot_diagnostics.source_state.store(RCH_VIEW_SOURCE_STATE_LIVE, std::memory_order_release);
        if (lease.sequence > tick_stats.last_observed_source_sequence) {
          tick_stats.last_observed_source_sequence = lease.sequence;
        }
        continue;
      }

      if (freeze_cache.has_frame) {
        ++tick_stats.frozen_source_count;
        slot_diagnostics.source_state.store(
          RCH_VIEW_SOURCE_STATE_FROZEN_LAST_GOOD,
          std::memory_order_release);
        BlitQuadrantFromCache(freeze_cache, state->composed_pixels, slot);
      } else if (camera_status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
                 || camera_status.state == RCH_CAMERA_STATE_STARTING
                 || camera_status.state == RCH_CAMERA_STATE_FAILED) {
        ++tick_stats.reconnecting_source_count;
        slot_diagnostics.source_state.store(
          RCH_VIEW_SOURCE_STATE_RECONNECTING,
          std::memory_order_release);
        FillQuadrantPlaceholder(state->composed_pixels, slot, UINT8_C(48), UINT8_C(40), UINT8_C(18));
      } else {
        ++tick_stats.waiting_for_first_frame_count;
        slot_diagnostics.source_state.store(
          RCH_VIEW_SOURCE_STATE_WAITING_FOR_FIRST_FRAME,
          std::memory_order_release);
        FillQuadrantPlaceholder(state->composed_pixels, slot, UINT8_C(24), UINT8_C(24), UINT8_C(48));
      }

      slot_diagnostics.latest_sequence.store(freeze_cache.last_sequence, std::memory_order_release);
      slot_diagnostics.freeze_cache_has_frame.store(
        freeze_cache.has_frame ? 1U : 0U,
        std::memory_order_release);

      if (!freeze_cache.has_frame) {
        ++tick_stats.stale_or_missing_source_count;
        ++tick_stats.stale_source_frame_count;
      }
    }

    PublishComposedFrame(*state);
    const auto composed_snapshot = state->latest_composed_frame.Snapshot();
    tick_stats.latest_composed_frame_sequence = composed_snapshot.sequence;

    const auto tick_end = std::chrono::steady_clock::now();
    const auto duration_us = static_cast<std::uint32_t>(std::chrono::duration_cast<std::chrono::microseconds>(
      tick_end - tick_start).count());
    if (duration_us > frame_budget_us) {
      ++tick_stats.render_deadline_miss_count;
      tick_stats.last_render_deadline_miss_us = duration_us - frame_budget_us;
      tick_stats.last_render_deadline_miss_sequence = tick_stats.latest_composed_frame_sequence;
    }
    UpdateRenderDurationStats(*state, duration_us);

    {
      std::lock_guard lock(state->stats_mutex);
      ++state->stats.render_frame_count;
      tick_stats.render_frame_count = state->stats.render_frame_count;

      ++state->fps_window_frames;
      const auto fps_window_elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        tick_end - state->fps_window_start).count();
      if (fps_window_elapsed >= 1000) {
        state->stats.render_fps_milli = static_cast<std::uint32_t>(
          (state->fps_window_frames * 1000ULL * 1000ULL)
          / static_cast<std::uint64_t>(fps_window_elapsed));
        state->fps_window_start = tick_end;
        state->fps_window_frames = 0;
      }

      state->stats.bound_source_count = tick_stats.bound_source_count;
      state->stats.sources_with_frame_count = tick_stats.sources_with_frame_count;
      state->stats.stale_or_missing_source_count = tick_stats.stale_or_missing_source_count;
      state->stats.live_source_count = tick_stats.live_source_count;
      state->stats.waiting_for_first_frame_count = tick_stats.waiting_for_first_frame_count;
      state->stats.frozen_source_count = tick_stats.frozen_source_count;
      state->stats.reconnecting_source_count = tick_stats.reconnecting_source_count;
      state->stats.sources_contributing_count = tick_stats.sources_contributing_count;
      state->stats.last_observed_source_sequence = tick_stats.last_observed_source_sequence;
      state->stats.latest_composed_frame_sequence = tick_stats.latest_composed_frame_sequence;
      state->stats.stale_source_frame_count += tick_stats.stale_source_frame_count;
      state->stats.render_deadline_miss_count += tick_stats.render_deadline_miss_count;
      if (tick_stats.render_deadline_miss_count > 0) {
        state->stats.last_render_deadline_miss_us = tick_stats.last_render_deadline_miss_us;
        state->stats.last_render_deadline_miss_sequence = tick_stats.last_render_deadline_miss_sequence;
      }
    }

    next_tick += frame_period;
    const auto now = std::chrono::steady_clock::now();
    if (next_tick > now) {
      std::this_thread::sleep_until(next_tick);
    } else {
      next_tick = now;
    }
  }

  state->render_running.store(false, std::memory_order_release);
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
      view->stop_requested.store(true, std::memory_order_release);
      if (view->render_thread.joinable()) {
        view->render_thread.join();
      }
      view->latest_composed_frame.Clear();
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
      {
        std::lock_guard stats_lock(view_state->stats_mutex);
        view_state->stats = ViewRenderStats{};
      }
      engine->registry_->views_[view_id_utf8] = view_state;
    }

    view_state->stop_requested.store(false, std::memory_order_release);
    try {
      view_state->render_thread = std::thread(&RenderViewLoop, view_state);
    } catch (...) {
      std::lock_guard lock(engine->registry_->view_registry_mutex_);
      auto found = engine->registry_->views_.find(view_id_utf8);
      if (found != engine->registry_->views_.end() && found->second == view_state) {
        engine->registry_->views_.erase(found);
      }
      throw;
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
      view->state->stop_requested.store(true, std::memory_order_release);
      if (view->state->render_thread.joinable()) {
        view->state->render_thread.join();
      }
      auto registry = view->state->registry;
      {
        std::lock_guard lock(registry->view_registry_mutex_);
        auto found = registry->views_.find(view->state->view_id);
        if (found != registry->views_.end() && found->second == view->state) {
          registry->views_.erase(found);
        }
      }
      view->state->removed.store(true, std::memory_order_release);
      view->state->latest_composed_frame.Clear();
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
  if (slot_index >= kGate3BViewSourceSlots || !IsValidCameraIdUtf8(camera_id_utf8)) {
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
  if (slot_index >= kGate3BViewSourceSlots) {
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
  if (out_status == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  constexpr std::uint32_t view_status_v1_size =
    static_cast<std::uint32_t>(offsetof(rch_view_status_v1, render_state));
  constexpr std::uint32_t view_status_v2_size =
    static_cast<std::uint32_t>(offsetof(rch_view_status_v1, live_source_count));
  const bool view_status_v1_ok =
    out_status->struct_version == RCH_VIEW_STATUS_VERSION_V1
    && out_status->struct_size >= view_status_v1_size;
  const bool view_status_v2_ok =
    out_status->struct_version == RCH_VIEW_STATUS_VERSION_V2
    && out_status->struct_size >= view_status_v2_size;
  const bool view_status_v3_ok =
    out_status->struct_version == RCH_VIEW_STATUS_VERSION_V3
    && out_status->struct_size >= sizeof(rch_view_status_v1);
  if (!view_status_v1_ok && !view_status_v2_ok && !view_status_v3_ok) {
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
    status.struct_size = view_status_v3_ok
      ? static_cast<std::uint32_t>(sizeof(status))
      : (view_status_v2_ok ? view_status_v2_size : view_status_v1_size);
    status.struct_version = view_status_v3_ok
      ? RCH_VIEW_STATUS_VERSION_V3
      : (view_status_v2_ok ? RCH_VIEW_STATUS_VERSION_V2 : RCH_VIEW_STATUS_VERSION_V1);

    std::lock_guard source_lock(view->state->mutex);
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
        if (lease.sequence > status.last_observed_source_sequence) {
          status.last_observed_source_sequence = lease.sequence;
        }
      } else {
        ++status.stale_or_missing_source_count;
      }
    }

    status.reserved = 0;
    if (view_status_v3_ok || view_status_v2_ok || view_status_v1_ok) {
      const auto composed = view->state->latest_composed_frame.Snapshot();
      std::lock_guard stats_lock(view->state->stats_mutex);
      status.render_state = view->state->render_running.load(std::memory_order_acquire)
        ? RCH_VIEW_RENDER_STATE_RUNNING
        : RCH_VIEW_RENDER_STATE_STOPPED;
      status.configured_width = kViewComposedWidth;
      status.configured_height = kViewComposedHeight;
      status.target_fps = kViewTargetFps;
      status.render_frame_count = view->state->stats.render_frame_count;
      status.latest_composed_frame_sequence = composed.sequence;
      status.latest_composed_frame_age_ms = composed.has_frame ? composed.age_ms : RCH_NO_FRAME_AGE_MS;
      status.render_fps_milli = view->state->stats.render_fps_milli;
      status.sources_contributing_count = view->state->stats.sources_contributing_count;
      status.output_consumer_count = view->state->output_consumer_count.load(std::memory_order_acquire);
      status.reserved_v2 = 0;
      status.last_render_duration_us = view->state->stats.last_render_duration_us;
      status.average_render_duration_us = view->state->stats.average_render_duration_us;
      status.p95_render_duration_us = view->state->stats.p95_render_duration_us;
      status.stale_source_frame_count = view->state->stats.stale_source_frame_count;
      status.live_source_count = view->state->stats.live_source_count;
      status.waiting_for_first_frame_count = view->state->stats.waiting_for_first_frame_count;
      status.frozen_source_count = view->state->stats.frozen_source_count;
      status.reconnecting_source_count = view->state->stats.reconnecting_source_count;
      status.render_deadline_miss_count = view->state->stats.render_deadline_miss_count;
      status.last_render_deadline_miss_us = view->state->stats.last_render_deadline_miss_us;
      status.last_render_deadline_miss_sequence = view->state->stats.last_render_deadline_miss_sequence;
    }

    const std::size_t bytes_to_copy = view_status_v3_ok
      ? sizeof(rch_view_status_v1)
      : (view_status_v2_ok ? static_cast<std::size_t>(view_status_v2_size)
                          : static_cast<std::size_t>(view_status_v1_size));
    std::memcpy(out_status, &status, bytes_to_copy);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_get_source_status(
  rch_view_handle view,
  uint32_t slot_index,
  rch_view_source_status_v1* out_status) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_status == nullptr || slot_index >= kGate3BViewSourceSlots) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }
  if (out_status->struct_version != RCH_VIEW_SOURCE_STATUS_VERSION
      || out_status->struct_size < sizeof(rch_view_source_status_v1)) {
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

    rch_view_source_status_v1 status{};
    status.struct_size = static_cast<std::uint32_t>(sizeof(status));
    status.struct_version = RCH_VIEW_SOURCE_STATUS_VERSION;
    status.slot_index = slot_index;
    status.source_state = RCH_VIEW_SOURCE_STATE_UNBOUND;
    status.has_binding = 0U;
    status.freeze_cache_has_frame = 0U;
    status.source_live = 0U;
    status.camera_state = RCH_CAMERA_STATE_STOPPED;
    status.latest_observed_sequence = 0U;
    status.latest_source_frame_age_ms = RCH_NO_FRAME_AGE_MS;
    std::memset(status.camera_id_utf8, 0, sizeof(status.camera_id_utf8));

    std::lock_guard lock(view->state->mutex);
    const auto& binding = view->state->sources[slot_index];
    if (!binding.has_value()) {
      std::memcpy(out_status, &status, sizeof(status));
      return RCH_RESULT_OK;
    }

    status.has_binding = 1U;
    const auto copy_length = std::min(
      binding->camera_id.size(),
      sizeof(status.camera_id_utf8) - 1U);
    std::memcpy(status.camera_id_utf8, binding->camera_id.data(), copy_length);
    status.camera_id_utf8[copy_length] = '\0';

    const auto rendered_source_state = view->state->slot_diagnostics[slot_index].source_state.load(
      std::memory_order_acquire);
    const auto rendered_latest_sequence = view->state->slot_diagnostics[slot_index].latest_sequence.load(
      std::memory_order_acquire);
    const auto rendered_has_freeze = view->state->slot_diagnostics[slot_index].freeze_cache_has_frame.load(
      std::memory_order_acquire);

    auto camera = binding->camera.lock();
    if (camera == nullptr || camera->ingest == nullptr || camera->removed.load(std::memory_order_acquire)) {
      status.source_state = RCH_VIEW_SOURCE_STATE_MISSING_OR_STALE;
      if (rendered_source_state == RCH_VIEW_SOURCE_STATE_MISSING_OR_STALE
          || rendered_source_state == RCH_VIEW_SOURCE_STATE_FROZEN_LAST_GOOD) {
        status.source_state = static_cast<std::uint32_t>(rendered_source_state);
      }
      status.freeze_cache_has_frame = rendered_has_freeze;
      status.latest_observed_sequence = rendered_latest_sequence;
      std::memcpy(out_status, &status, sizeof(status));
      return RCH_RESULT_OK;
    }

    const auto lease = camera->ingest->AcquireLatestFrameLease();
    rch_camera_status_v1 camera_status{};
    camera_status.struct_size = static_cast<std::uint32_t>(sizeof(camera_status));
    camera_status.struct_version = RCH_CAMERA_STATUS_VERSION;
    camera->ingest->FillStatus(camera_status);
    status.camera_state = camera_status.state;
    status.latest_observed_sequence = lease.sequence;
    status.latest_source_frame_age_ms = lease.has_frame ? lease.age_ms : RCH_NO_FRAME_AGE_MS;
    status.freeze_cache_has_frame = rendered_has_freeze;
    if (!lease.has_frame && rendered_latest_sequence > 0U) {
      status.latest_observed_sequence = rendered_latest_sequence;
    }
    if (lease.has_frame && lease.sample() != nullptr
        && (rendered_source_state == RCH_VIEW_SOURCE_STATE_UNBOUND
            || rendered_source_state == RCH_VIEW_SOURCE_STATE_WAITING_FOR_FIRST_FRAME)) {
      status.source_state = RCH_VIEW_SOURCE_STATE_LIVE;
    } else {
      status.source_state = static_cast<std::uint32_t>(rendered_source_state);
    }
    status.source_live = status.source_state == RCH_VIEW_SOURCE_STATE_LIVE ? 1U : 0U;

    std::memcpy(out_status, &status, sizeof(status));
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_acquire_latest_frame(
  rch_view_handle view,
  rch_view_frame_lease_handle* out_lease) noexcept
{
  if (view == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_lease == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  *out_lease = nullptr;
  try {
    if (view->destroyed.load(std::memory_order_acquire) || view->state == nullptr) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (!IsRegistryActive(view->state->registry)
        || view->state->removed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    auto* lease = new rch_view_frame_lease();
    lease->owner = view->state;
    lease->lease = view->state->latest_composed_frame.AcquireLease();
    view->state->output_consumer_count.fetch_add(1U, std::memory_order_acq_rel);
    *out_lease = lease;
    return RCH_RESULT_OK;
  } catch (const std::bad_alloc&) {
    return RCH_RESULT_OUT_OF_MEMORY;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_frame_lease_get_status(
  rch_view_frame_lease_handle lease,
  rch_view_frame_lease_status_v1* out_status) noexcept
{
  if (lease == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_status == nullptr || out_status->struct_version != RCH_VIEW_FRAME_LEASE_STATUS_VERSION_V1
      || out_status->struct_size < sizeof(rch_view_frame_lease_status_v1)) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    if (lease->destroyed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }

    rch_view_frame_lease_status_v1 status{};
    status.struct_size = static_cast<std::uint32_t>(sizeof(status));
    status.struct_version = RCH_VIEW_FRAME_LEASE_STATUS_VERSION_V1;
    status.has_frame = lease->lease.has_frame ? 1U : 0U;
    status.width = lease->lease.width;
    status.height = lease->lease.height;
    status.reserved = 0;
    status.composed_frame_count = lease->lease.frame_count;
    status.latest_frame_sequence = lease->lease.sequence;
    status.latest_frame_timestamp_ns = lease->lease.timestamp_ns;
    status.latest_frame_age_ms = lease->lease.has_frame ? lease->lease.age_ms : RCH_NO_FRAME_AGE_MS;
    std::memcpy(out_status, &status, sizeof(status));
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_frame_lease_sample_rgba(
  rch_view_frame_lease_handle lease,
  uint32_t x,
  uint32_t y,
  uint8_t* out_r,
  uint8_t* out_g,
  uint8_t* out_b,
  uint8_t* out_a) noexcept
{
  if (lease == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }
  if (out_r == nullptr || out_g == nullptr || out_b == nullptr || out_a == nullptr) {
    return RCH_RESULT_INVALID_ARGUMENT;
  }

  try {
    if (lease->destroyed.load(std::memory_order_acquire)) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (!lease->lease.has_frame || lease->lease.sample() == nullptr) {
      return RCH_RESULT_INVALID_STATE;
    }

    auto* sample = lease->lease.sample();
    auto* caps = gst_sample_get_caps(sample);
    auto* buffer = gst_sample_get_buffer(sample);
    if (caps == nullptr || buffer == nullptr || gst_caps_is_empty(caps)) {
      return RCH_RESULT_INTERNAL_ERROR;
    }

    const auto* structure = gst_caps_get_structure(caps, 0);
    const auto* format = gst_structure_get_string(structure, "format");
    if (format == nullptr || g_ascii_strcasecmp(format, "RGBA") != 0) {
      return RCH_RESULT_INVALID_STATE;
    }

    int width = 0;
    int height = 0;
    if (!gst_structure_get_int(structure, "width", &width)
        || !gst_structure_get_int(structure, "height", &height)
        || width <= 0
        || height <= 0) {
      return RCH_RESULT_INTERNAL_ERROR;
    }

    if (x >= static_cast<std::uint32_t>(width) || y >= static_cast<std::uint32_t>(height)) {
      return RCH_RESULT_INVALID_ARGUMENT;
    }

    const auto stride = static_cast<std::size_t>(width) * 4U;
    const auto required = stride * static_cast<std::size_t>(height);
    GstMapInfo map{};
    if (!gst_buffer_map(buffer, &map, GST_MAP_READ)) {
      return RCH_RESULT_INTERNAL_ERROR;
    }
    if (map.size < required) {
      gst_buffer_unmap(buffer, &map);
      return RCH_RESULT_INTERNAL_ERROR;
    }

    const auto offset = static_cast<std::size_t>(y) * stride + static_cast<std::size_t>(x) * 4U;
    *out_r = map.data[offset + 0U];
    *out_g = map.data[offset + 1U];
    *out_b = map.data[offset + 2U];
    *out_a = map.data[offset + 3U];
    gst_buffer_unmap(buffer, &map);
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}

extern "C" rch_result rch_view_frame_lease_destroy(
  rch_view_frame_lease_handle lease) noexcept
{
  if (lease == nullptr) {
    return RCH_RESULT_INVALID_HANDLE;
  }

  try {
    if (lease->destroyed.exchange(true, std::memory_order_acq_rel)) {
      return RCH_RESULT_INVALID_HANDLE;
    }
    if (lease->owner != nullptr) {
      DecrementIfPositive(lease->owner->output_consumer_count);
    }
    lease->lease = robocamhub::frames::LatestFrameLease{};
    lease->owner.reset();
    delete lease;
    return RCH_RESULT_OK;
  } catch (...) {
    return RCH_RESULT_INTERNAL_ERROR;
  }
}
