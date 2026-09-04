#ifndef ROBOCAMHUB_SINGLE_CAMERA_INGEST_H
#define ROBOCAMHUB_SINGLE_CAMERA_INGEST_H

#include "frames/latest_frame.h"
#include "robocamhub_native.h"

#include <gst/app/gstappsink.h>
#include <gst/gst.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <string>
#include <thread>

namespace robocamhub::ingest {

class SingleCameraIngest final {
public:
  SingleCameraIngest() = default;
  ~SingleCameraIngest();

  SingleCameraIngest(const SingleCameraIngest&) = delete;
  SingleCameraIngest& operator=(const SingleCameraIngest&) = delete;

  rch_result Configure(const rch_camera_config_v1& config);
  rch_result Start();
  rch_result Stop();
  void FillStatus(rch_camera_status_v1& status) const;

private:
#if defined(RCH_INGEST_TESTING)
  // Only the separately compiled regression-test target has this scheduling seam.
  friend struct SingleCameraIngestTestAccess;
  void (*before_playing_for_test_)(SingleCameraIngest&){nullptr};
#endif

  static void OnRtspPadAdded(GstElement* source, GstPad* new_pad, gpointer user_data);
  static GstFlowReturn OnNewSample(GstAppSink* sink, gpointer user_data);

  rch_result BuildPipeline();
  void MonitorBus();
  void RecordFailure(rch_result result);
  void SetFailure(rch_result result);
  void ResetPipeline();

  std::mutex control_mutex_;
  bool configured_{false};
  std::string camera_id_;
  std::string rtsp_url_;
  std::chrono::milliseconds connect_timeout_{10000};

  GstElement* pipeline_{nullptr};
  GstElement* rtsp_source_{nullptr};
  GstElement* depayloader_{nullptr};
  GstElement* decoder_{nullptr};
  GstElement* app_sink_{nullptr};
  GstBus* bus_{nullptr};

  std::thread monitor_thread_;
  std::chrono::steady_clock::time_point start_time_{};
  std::atomic<bool> stop_requested_{false};
  std::atomic<rch_camera_state> state_{RCH_CAMERA_STATE_STOPPED};
  std::atomic<rch_result> last_result_{RCH_RESULT_OK};
  std::atomic<std::uint32_t> active_session_count_{0};
  std::atomic<std::uint32_t> active_decoder_count_{0};
  frames::LatestFrame latest_frame_;
};

}  // namespace robocamhub::ingest

#endif
