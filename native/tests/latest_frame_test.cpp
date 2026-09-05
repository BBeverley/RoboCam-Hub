#include "frames/latest_frame.h"

#include <gst/gst.h>

#include <cstdint>
#include <iostream>

namespace {

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

void OnBufferFinalized(gpointer user_data, GstMiniObject*)
{
  auto* finalization_count = static_cast<std::uint32_t*>(user_data);
  ++(*finalization_count);
}

GstSample* MakeSample(
  std::uint32_t width,
  std::uint32_t height,
  GstClockTime timestamp,
  std::uint32_t& finalization_count)
{
  auto* buffer = gst_buffer_new_allocate(nullptr, width * height * 3U, nullptr);
  GST_BUFFER_PTS(buffer) = timestamp;
  gst_mini_object_weak_ref(
    GST_MINI_OBJECT(buffer),
    &OnBufferFinalized,
    &finalization_count);

  auto* caps = gst_caps_new_simple(
    "video/x-raw",
    "format", G_TYPE_STRING, "RGB",
    "width", G_TYPE_INT, static_cast<int>(width),
    "height", G_TYPE_INT, static_cast<int>(height),
    nullptr);
  auto* sample = gst_sample_new(buffer, caps, nullptr, nullptr);
  gst_buffer_unref(buffer);
  gst_caps_unref(caps);
  return sample;
}

bool RequiredPluginsAreAvailable()
{
  constexpr const char* required_factories[]{
    "rtspsrc",
    "rtph264depay",
    "h264parse",
    "avdec_h264",
    "queue",
    "appsink",
  };

  for (const auto* factory_name : required_factories) {
    auto* factory = gst_element_factory_find(factory_name);
    if (factory == nullptr) {
      std::cerr << "FAILED: required GStreamer factory is unavailable: " << factory_name << '\n';
      return false;
    }
    gst_object_unref(factory);
  }
  return true;
}

}  // namespace

int main()
{
  GError* error = nullptr;
  if (!Expect(gst_init_check(nullptr, nullptr, &error) != FALSE,
              "GStreamer must initialise for deterministic frame tests")) {
    if (error != nullptr) {
      g_error_free(error);
    }
    return 1;
  }
  if (error != nullptr) {
    g_error_free(error);
  }

  if (!RequiredPluginsAreAvailable()) {
    gst_deinit();
    return 1;
  }

  std::uint32_t finalized_buffers = 0;
  {
    robocamhub::frames::LatestFrame latest_frame;
    auto* first = MakeSample(64, 48, 1000, finalized_buffers);
    latest_frame.Publish(first);
    gst_sample_unref(first);

    auto first_lease = latest_frame.AcquireLease();
    if (!Expect(first_lease.has_frame, "acquired lease must expose the current frame")) {
      return 1;
    }

    auto snapshot = latest_frame.Snapshot();
    if (!Expect(latest_frame.RetainedFrameCount() == 1,
                "latest-frame storage must retain exactly one frame")
        || !Expect(snapshot.sequence == 1 && snapshot.width == 64 && snapshot.height == 48,
                   "first frame metadata must be observable")) {
      return 1;
    }

    auto* second = MakeSample(128, 72, 2000, finalized_buffers);
    latest_frame.Publish(second);
    gst_sample_unref(second);
    snapshot = latest_frame.Snapshot();

    if (!Expect(latest_frame.RetainedFrameCount() == 1,
                "publishing must replace rather than queue frames")
        || !Expect(finalized_buffers == 0,
                   "replaced storage must remain alive while a consumer lease exists")
        || !Expect(snapshot.frame_count == 2 && snapshot.sequence == 2,
                   "frame sequence must advance on replacement")
        || !Expect(snapshot.width == 128 && snapshot.height == 72,
                   "latest metadata must replace stale metadata")) {
      return 1;
    }

    first_lease = {};
    if (!Expect(finalized_buffers == 1,
                "replaced frame storage must release when its final lease is dropped")) {
      return 1;
    }

    for (std::uint32_t index = 0; index < 1000; ++index) {
      auto* replacement = MakeSample(128, 72, 3000 + index, finalized_buffers);
      latest_frame.Publish(replacement);
      gst_sample_unref(replacement);
      if (!Expect(latest_frame.RetainedFrameCount() == 1
                    && finalized_buffers == index + 2,
                  "absent/slow consumption must not accumulate retained frames")) {
        return 1;
      }
    }

    latest_frame.Clear();
    if (!Expect(latest_frame.RetainedFrameCount() == 0,
                "clear must release the native latest-frame slot")
        || !Expect(finalized_buffers == 1002, "clear must release the retained frame")) {
      return 1;
    }
  }

  gst_deinit();
  return 0;
}
