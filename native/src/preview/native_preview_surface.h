#ifndef ROBOCAMHUB_NATIVE_PREVIEW_SURFACE_H
#define ROBOCAMHUB_NATIVE_PREVIEW_SURFACE_H

#include "frames/latest_frame.h"
#include "robocamhub_native.h"

#include <cstdint>
#include <memory>

namespace robocamhub::preview {

struct PreviewFrameSource final {
  void* context{nullptr};
  frames::LatestFrameLease (*acquire_latest)(void* context) noexcept{nullptr};
  void (*report_waiting)(void* context) noexcept{nullptr};
  void (*report_presented)(
    void* context,
    std::uint64_t sequence,
    std::uint64_t frame_age_ms) noexcept{nullptr};
  void (*report_surface_recreated)(void* context) noexcept{nullptr};
  void (*report_error)(void* context, rch_result result) noexcept{nullptr};
};

class NativePreviewSurface {
public:
  virtual ~NativePreviewSurface() = default;

  NativePreviewSurface(const NativePreviewSurface&) = delete;
  NativePreviewSurface& operator=(const NativePreviewSurface&) = delete;

protected:
  NativePreviewSurface() = default;
};

[[nodiscard]] std::unique_ptr<NativePreviewSurface> CreateNativePreviewSurface(
  rch_view_preview_platform platform,
  std::uint64_t host_native_handle,
  std::uint32_t target_fps,
  PreviewFrameSource source) noexcept;

}  // namespace robocamhub::preview

#endif
