#include "preview/native_preview_surface.h"
#include "preview/preview_test_hooks.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <memory>
#include <new>
#include <thread>

namespace {

std::atomic<std::uint32_t> g_presentation_delay_ms{0};
std::atomic<std::uint32_t> g_recreation_generation{0};
std::atomic<std::uint32_t> g_active_surface_count{0};

class FakePreviewSurface final : public robocamhub::preview::NativePreviewSurface {
public:
  FakePreviewSurface(
    std::uint32_t target_fps,
    robocamhub::preview::PreviewFrameSource source)
      : target_fps_(target_fps),
        source_(source),
        observed_recreation_generation_(g_recreation_generation.load(std::memory_order_acquire))
  {
    worker_ = std::thread(&FakePreviewSurface::Run, this);
    g_active_surface_count.fetch_add(1U, std::memory_order_acq_rel);
  }

  ~FakePreviewSurface() override
  {
    stop_requested_.store(true, std::memory_order_release);
    if (worker_.joinable()) {
      worker_.join();
    }
    g_active_surface_count.fetch_sub(1U, std::memory_order_acq_rel);
  }

private:
  void Run() noexcept
  {
    const auto period = std::chrono::microseconds(
      1000000 / static_cast<std::int64_t>(std::max<std::uint32_t>(1U, target_fps_)));
    auto next_tick = std::chrono::steady_clock::now();
    while (!stop_requested_.load(std::memory_order_acquire)) {
      const auto delay_ms = g_presentation_delay_ms.load(std::memory_order_acquire);
      if (delay_ms > 0U) {
        std::this_thread::sleep_for(std::chrono::milliseconds(delay_ms));
      }
      if (stop_requested_.load(std::memory_order_acquire)) {
        break;
      }

      const auto recreation_generation = g_recreation_generation.load(std::memory_order_acquire);
      if (recreation_generation != observed_recreation_generation_) {
        observed_recreation_generation_ = recreation_generation;
        if (source_.report_surface_recreated != nullptr) {
          source_.report_surface_recreated(source_.context);
        }
      }

      auto lease = source_.acquire_latest == nullptr
        ? robocamhub::frames::LatestFrameLease{}
        : source_.acquire_latest(source_.context);
      if (lease.has_frame && lease.sample() != nullptr) {
        if (source_.report_presented != nullptr) {
          source_.report_presented(source_.context, lease.sequence, lease.age_ms);
        }
      } else if (source_.report_waiting != nullptr) {
        source_.report_waiting(source_.context);
      }

      next_tick += period;
      const auto now = std::chrono::steady_clock::now();
      if (next_tick > now) {
        std::this_thread::sleep_until(next_tick);
      } else {
        next_tick = now;
      }
    }
  }

  std::uint32_t target_fps_;
  robocamhub::preview::PreviewFrameSource source_;
  std::uint32_t observed_recreation_generation_;
  std::atomic<bool> stop_requested_{false};
  std::thread worker_;
};

}  // namespace

namespace robocamhub::preview {

std::unique_ptr<NativePreviewSurface> CreateNativePreviewSurface(
  rch_view_preview_platform platform,
  std::uint64_t host_native_handle,
  std::uint32_t target_fps,
  PreviewFrameSource source) noexcept
{
  if (platform != RCH_VIEW_PREVIEW_PLATFORM_MACOS_NSVIEW
      || host_native_handle == 0U
      || target_fps == 0U) {
    return nullptr;
  }
  try {
    return std::make_unique<FakePreviewSurface>(target_fps, source);
  } catch (...) {
    return nullptr;
  }
}

}  // namespace robocamhub::preview

namespace robocamhub::testing {

void SetPreviewPresentationDelayMs(std::uint32_t delay_ms) noexcept
{
  g_presentation_delay_ms.store(delay_ms, std::memory_order_release);
}

void RequestPreviewSurfaceRecreation() noexcept
{
  g_recreation_generation.fetch_add(1U, std::memory_order_acq_rel);
}

std::uint32_t ActivePreviewSurfaceCount() noexcept
{
  return g_active_surface_count.load(std::memory_order_acquire);
}

}  // namespace robocamhub::testing
