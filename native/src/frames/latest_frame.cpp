#include "frames/latest_frame.h"

#include <algorithm>

namespace robocamhub::frames {
namespace {

std::uint64_t MonotonicTimeNs()
{
  return static_cast<std::uint64_t>(g_get_monotonic_time()) * UINT64_C(1000);
}

}  // namespace

LatestFrame::~LatestFrame()
{
  Clear();
}

void LatestFrame::Publish(GstSample* sample)
{
  if (sample == nullptr) {
    return;
  }

  auto* retained_sample = gst_sample_ref(sample);
  std::uint32_t width = 0;
  std::uint32_t height = 0;

  if (auto* caps = gst_sample_get_caps(sample); caps != nullptr && !gst_caps_is_empty(caps)) {
    const auto* structure = gst_caps_get_structure(caps, 0);
    int parsed_width = 0;
    int parsed_height = 0;
    if (gst_structure_get_int(structure, "width", &parsed_width) && parsed_width > 0) {
      width = static_cast<std::uint32_t>(parsed_width);
    }
    if (gst_structure_get_int(structure, "height", &parsed_height) && parsed_height > 0) {
      height = static_cast<std::uint32_t>(parsed_height);
    }
  }

  std::uint64_t timestamp_ns = 0;
  if (auto* buffer = gst_sample_get_buffer(sample); buffer != nullptr) {
    if (GST_BUFFER_PTS_IS_VALID(buffer)) {
      timestamp_ns = GST_BUFFER_PTS(buffer);
    } else if (GST_BUFFER_DTS_IS_VALID(buffer)) {
      timestamp_ns = GST_BUFFER_DTS(buffer);
    }
  }

  GstSample* replaced_sample = nullptr;
  {
    const std::scoped_lock lock(mutex_);
    replaced_sample = sample_;
    sample_ = retained_sample;
    width_ = width;
    height_ = height;
    ++frame_count_;
    sequence_ = frame_count_;
    timestamp_ns_ = timestamp_ns;
    arrival_time_ns_ = MonotonicTimeNs();
  }

  if (replaced_sample != nullptr) {
    gst_sample_unref(replaced_sample);
  }
}

void LatestFrame::Clear()
{
  GstSample* released_sample = nullptr;
  {
    const std::scoped_lock lock(mutex_);
    released_sample = sample_;
    sample_ = nullptr;
    width_ = 0;
    height_ = 0;
    timestamp_ns_ = 0;
    arrival_time_ns_ = 0;
  }

  if (released_sample != nullptr) {
    gst_sample_unref(released_sample);
  }
}

LatestFrameSnapshot LatestFrame::Snapshot() const
{
  const auto now_ns = MonotonicTimeNs();
  const std::scoped_lock lock(mutex_);

  LatestFrameSnapshot snapshot{};
  snapshot.has_frame = sample_ != nullptr;
  snapshot.width = width_;
  snapshot.height = height_;
  snapshot.frame_count = frame_count_;
  snapshot.sequence = sequence_;
  snapshot.timestamp_ns = timestamp_ns_;
  snapshot.age_ms = sample_ == nullptr
    ? 0
    : (now_ns - std::min(now_ns, arrival_time_ns_)) / UINT64_C(1000000);
  return snapshot;
}

std::uint32_t LatestFrame::RetainedFrameCount() const
{
  const std::scoped_lock lock(mutex_);
  return sample_ == nullptr ? 0U : 1U;
}

}  // namespace robocamhub::frames
