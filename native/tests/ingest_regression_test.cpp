#include "ingest/single_camera_ingest.h"

#include <chrono>
#include <iostream>
#include <thread>

namespace {

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

rch_camera_status_v1 Status(const robocamhub::ingest::SingleCameraIngest& camera)
{
  rch_camera_status_v1 status{};
  camera.FillStatus(status);
  return status;
}

const rch_camera_config_v1 config{
  static_cast<uint32_t>(sizeof(rch_camera_config_v1)),
  RCH_CAMERA_CONFIG_VERSION,
  "startup-regression",
  "rtsp://127.0.0.1:1/profile2/media.smp",
  100, // Minimum supported first-frame timeout.
  0,
};

bool startup_checks_passed = true;

}  // namespace

namespace robocamhub::ingest {

struct SingleCameraIngestTestAccess {
  static void PauseBeforePlaying(SingleCameraIngest& camera)
  {
    // Initial startup runs before monitor creation. Retry attempts reuse the
    // existing monitor thread and therefore remain joinable by design.
    if (camera.reconnect_attempt_count_.load(std::memory_order_acquire) == 0) {
      startup_checks_passed &= Expect(!camera.monitor_thread_.joinable(),
        "monitor must not exist before the initial PLAYING request");
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(300));
    const auto status = Status(camera);
    startup_checks_passed &= Expect(status.state == RCH_CAMERA_STATE_STARTING
      && status.last_result == RCH_RESULT_OK
      && status.active_rtsp_session_count == 1 && status.active_decoder_count == 1,
      "a pre-PLAYING scheduling pause longer than 100 ms must not fail/clear ownership");
    GstState state = GST_STATE_VOID_PENDING;
    gst_element_get_state(camera.pipeline_, &state, nullptr, 0);
    startup_checks_passed &= Expect(state == GST_STATE_NULL,
      "the scheduling seam must run before playback is requested");
  }

  static void InstallStartupPause(SingleCameraIngest& camera)
  {
    camera.before_playing_for_test_ = &PauseBeforePlaying;
  }

  static bool PipelineIsNull(SingleCameraIngest& camera)
  {
    if (camera.pipeline_ == nullptr) {
      return true;
    }
    GstState state = GST_STATE_VOID_PENDING;
    gst_element_get_state(camera.pipeline_, &state, nullptr, 0);
    return state == GST_STATE_NULL;
  }

  static bool ExerciseTimeout(SingleCameraIngest& camera, bool fail_pad_link)
  {
    if (!Expect(camera.BuildPipeline() == RCH_RESULT_OK, "real pipeline must build")) {
      return false;
    }
    camera.state_.store(RCH_CAMERA_STATE_STARTING);
    camera.active_session_count_.store(1);
    camera.active_decoder_count_.store(1);
    camera.stop_requested_.store(false);

    if (fail_pad_link) {
      // Advertise H264 to the real pad-added callback, but give the pad an
      // incompatible media type so linking to rtph264depay cannot succeed.
      auto* caps = gst_caps_new_simple("application/x-incompatible",
        "media", G_TYPE_STRING, "video", "encoding-name", G_TYPE_STRING, "H264", nullptr);
      auto* pad_template = gst_pad_template_new("test-source", GST_PAD_SRC, GST_PAD_ALWAYS, caps);
      auto* pad = gst_pad_new_from_template(pad_template, "test-source");
      SingleCameraIngest::OnRtspPadAdded(nullptr, pad, &camera);
      gst_object_unref(pad);
      gst_object_unref(pad_template);
      gst_caps_unref(caps);
      if (!Expect(Status(camera).last_result == RCH_RESULT_RTSP_FAILURE,
                  "the actual pad-link failure must record the RTSP-specific category")) {
        return false;
      }
    }

    // Expire the monitor deadline and request stop shortly after, ensuring the
    // reconnect loop remains interruptible and ownership never duplicates.
    camera.start_time_ = std::chrono::steady_clock::now() - std::chrono::seconds(1);
    std::thread monitor([&camera] { camera.MonitorBus(); });
    std::this_thread::sleep_for(std::chrono::milliseconds(150));
    camera.stop_requested_.store(true, std::memory_order_release);
    monitor.join();

    const auto status = Status(camera);
    const auto expected = fail_pad_link ? RCH_RESULT_RTSP_FAILURE : RCH_RESULT_CONNECTION_TIMEOUT;
    bool passed = Expect(status.last_result == expected,
      "timeout must preserve an existing RTSP cause, or report timeout if none exists");
    passed &= Expect(status.active_rtsp_session_count == 0 && status.active_decoder_count == 0
                       && PipelineIsNull(camera),
      "reconnect attempts must tear down ownership before retries");
    passed &= Expect(status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
                       || status.state == RCH_CAMERA_STATE_STOPPED
                       || status.state == RCH_CAMERA_STATE_STARTING,
      "timeout path should progress into retry lifecycle until interrupted");
    if (fail_pad_link) {
      camera.SetFailure(RCH_RESULT_GSTREAMER_ERROR);
      passed &= Expect(Status(camera).last_result == RCH_RESULT_RTSP_FAILURE,
        "generic bus/start failures must not overwrite the specific cause either");
    }
    return passed;
  }
};

}  // namespace robocamhub::ingest

int main()
{
  GError* error = nullptr;
  if (!gst_init_check(nullptr, nullptr, &error)) {
    std::cerr << "GStreamer initialization failed: " << (error == nullptr ? "unknown" : error->message) << '\n';
    g_clear_error(&error);
    return 1;
  }

  using robocamhub::ingest::SingleCameraIngest;
  using robocamhub::ingest::SingleCameraIngestTestAccess;
  bool passed = true;
  {
    SingleCameraIngest camera;
    passed &= Expect(camera.Configure(config) == RCH_RESULT_OK, "100 ms configuration must be accepted");
    SingleCameraIngestTestAccess::InstallStartupPause(camera);
    for (int cycle = 0; cycle < 3; ++cycle) {
      passed &= Expect(camera.Start() == RCH_RESULT_OK, "playback request must succeed after the pause");
      const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
      auto status = Status(camera);
      while (status.state != RCH_CAMERA_STATE_WAITING_TO_RETRY && std::chrono::steady_clock::now() < deadline) {
        passed &= Expect(status.active_rtsp_session_count <= 1 && status.active_decoder_count <= 1,
                         "startup must not duplicate session/decoder ownership");
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        status = Status(camera);
      }
      passed &= Expect(status.last_result == RCH_RESULT_CONNECTION_TIMEOUT
                         || status.last_result == RCH_RESULT_RTSP_FAILURE,
        "post-request failure must publish a timeout/RTSP failure result");
      passed &= Expect(status.active_rtsp_session_count <= 1 && status.active_decoder_count <= 1,
        "post-request retry lifecycle must preserve single-ownership limits");
      passed &= Expect(camera.Stop() == RCH_RESULT_OK && Status(camera).state == RCH_CAMERA_STATE_STOPPED,
                       "stop must restore deterministic lifecycle after failed startup");
    }
    passed &= startup_checks_passed;
  }
  for (const bool fail_pad_link : {false, true}) {
    SingleCameraIngest camera;
    passed &= Expect(camera.Configure(config) == RCH_RESULT_OK, "timeout test must configure");
    passed &= SingleCameraIngestTestAccess::ExerciseTimeout(camera, fail_pad_link);
  }
  gst_deinit();
  return passed ? 0 : 1;
}
