#ifndef ROBOCAMHUB_LATEST_FRAME_H
#define ROBOCAMHUB_LATEST_FRAME_H

#include <gst/gst.h>

#include <cstdint>
#include <mutex>
#include <memory>

namespace robocamhub::frames {

struct LatestFrameSnapshot {
  bool has_frame{false};
  std::uint32_t width{0};
  std::uint32_t height{0};
  std::uint64_t frame_count{0};
  std::uint64_t sequence{0};
  std::uint64_t timestamp_ns{0};
  std::uint64_t age_ms{0};
};

struct LatestFrameLease {
  bool has_frame{false};
  std::uint32_t width{0};
  std::uint32_t height{0};
  std::uint64_t frame_count{0};
  std::uint64_t sequence{0};
  std::uint64_t timestamp_ns{0};
  std::uint64_t age_ms{0};

  [[nodiscard]] GstSample* sample() const
  {
    return sample_.get();
  }

private:
  friend class LatestFrame;
  std::shared_ptr<GstSample> sample_{};
};

class LatestFrame final {
public:
  LatestFrame() = default;
  ~LatestFrame();

  LatestFrame(const LatestFrame&) = delete;
  LatestFrame& operator=(const LatestFrame&) = delete;

  void Publish(GstSample* sample);
  void Clear();
  [[nodiscard]] LatestFrameSnapshot Snapshot() const;
  [[nodiscard]] LatestFrameLease AcquireLease() const;
  [[nodiscard]] std::uint32_t RetainedFrameCount() const;

private:
  struct PublishedFrame;
  mutable std::mutex mutex_;
  std::shared_ptr<PublishedFrame> latest_{};
  std::uint64_t frame_count_{0};
};

}  // namespace robocamhub::frames

#endif
