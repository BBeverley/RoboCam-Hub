#include "frames/latest_frame.h"

#include <algorithm>

namespace robocamhub::frames {
namespace {

std::uint64_t MonotonicTimeNs()
{
  return static_cast<std::uint64_t>(g_get_monotonic_time()) * UINT64_C(1000);
}

}  // namespace

struct LatestFrame::PublishedFrame final {
  std::shared_ptr<GstSample> sample{};
  std::uint32_t width{0};
  std::uint32_t height{0};
  std::uint64_t frame_count{0};
  std::uint64_t sequence{0};
  std::uint64_t timestamp_ns{0};
  std::uint64_t arrival_time_ns{0};
};

LatestFrame::~LatestFrame()
{
  Clear();
}

void LatestFrame::Publish(GstSample* sample)
{
  if (sample == nullptr) {
    return;
  }

  auto retained_sample = std::shared_ptr<GstSample>(
    gst_sample_ref(sample),
    [](GstSample* retained) {
      if (retained != nullptr) {
        gst_sample_unref(retained);
      }
    });
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

  auto published = std::make_shared<PublishedFrame>();
  published->sample = std::move(retained_sample);
  published->width = width;
  published->height = height;
  published->timestamp_ns = timestamp_ns;
  published->arrival_time_ns = MonotonicTimeNs();

  {
    const std::scoped_lock lock(mutex_);
    ++frame_count_;
    published->frame_count = frame_count_;
    published->sequence = frame_count_;
    latest_ = std::move(published);
  }
}

void LatestFrame::Clear()
{
  {
    const std::scoped_lock lock(mutex_);
    latest_.reset();
  }
}

LatestFrameSnapshot LatestFrame::Snapshot() const
{
  const auto now_ns = MonotonicTimeNs();
  std::shared_ptr<PublishedFrame> published;
  std::uint64_t frame_count = 0;
  {
    const std::scoped_lock lock(mutex_);
    published = latest_;
    frame_count = frame_count_;
  }

  LatestFrameSnapshot snapshot{};
  snapshot.frame_count = frame_count;
  if (published != nullptr) {
    snapshot.has_frame = true;
    snapshot.width = published->width;
    snapshot.height = published->height;
    snapshot.sequence = published->sequence;
    snapshot.timestamp_ns = published->timestamp_ns;
    snapshot.frame_count = published->frame_count;
    snapshot.age_ms = (now_ns - std::min(now_ns, published->arrival_time_ns)) / UINT64_C(1000000);
  }
  return snapshot;
}

LatestFrameLease LatestFrame::AcquireLease() const
{
  const auto now_ns = MonotonicTimeNs();
  std::shared_ptr<PublishedFrame> published;
  {
    const std::scoped_lock lock(mutex_);
    published = latest_;
  }

  LatestFrameLease lease{};
  if (published == nullptr) {
    return lease;
  }

  lease.has_frame = true;
  lease.width = published->width;
  lease.height = published->height;
  lease.frame_count = published->frame_count;
  lease.sequence = published->sequence;
  lease.timestamp_ns = published->timestamp_ns;
  lease.age_ms = (now_ns - std::min(now_ns, published->arrival_time_ns)) / UINT64_C(1000000);
  lease.sample_ = published->sample;
  return lease;
}

std::uint32_t LatestFrame::RetainedFrameCount() const
{
  const std::scoped_lock lock(mutex_);
  return latest_ == nullptr ? 0U : 1U;
}

}  // namespace robocamhub::frames
