#include "robocamhub_native.h"

#include <gst/rtsp-server/rtsp-server.h>

#include <chrono>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>

namespace {

class LoopbackRtspFixture final {
public:
  bool Start(std::uint32_t fps)
  {
    Stop();
    fps_ = fps;

    context_ = g_main_context_new();
    loop_ = g_main_loop_new(context_, FALSE);
    server_ = gst_rtsp_server_new();
    gst_rtsp_server_set_address(server_, "127.0.0.1");
    const auto service = fixed_port_ == 0 ? std::string("0") : std::to_string(fixed_port_);
    gst_rtsp_server_set_service(server_, service.c_str());

    auto* mounts = gst_rtsp_server_get_mount_points(server_);
    factory_ = gst_rtsp_media_factory_new();
    const auto launch = "( videotestsrc is-live=true pattern=ball ! "
      "video/x-raw,format=I420,width=128,height=72,framerate=" + std::to_string(fps_) + "/1 "
      "! x264enc tune=zerolatency speed-preset=ultrafast key-int-max=1 "
      "! rtph264pay name=pay0 pt=96 config-interval=1 )";
    gst_rtsp_media_factory_set_launch(factory_, launch.c_str());
    gst_rtsp_media_factory_set_protocols(factory_, GST_RTSP_LOWER_TRANS_UDP);
    gst_rtsp_mount_points_add_factory(mounts, "/profile2/media.smp", factory_);
    g_object_unref(mounts);

    source_id_ = gst_rtsp_server_attach(server_, context_);
    const auto port = gst_rtsp_server_get_bound_port(server_);
    if (source_id_ == 0 || port == 0) {
      Stop();
      return false;
    }
    if (fixed_port_ == 0) {
      fixed_port_ = port;
    }

    url_ = "rtsp://127.0.0.1:" + std::to_string(port) + "/profile2/media.smp";
    thread_ = std::thread([this] { g_main_loop_run(loop_); });
    return true;
  }

  void Stop()
  {
    DropClients();
    if (loop_ != nullptr) {
      g_main_loop_quit(loop_);
    }
    if (thread_.joinable()) {
      thread_.join();
    }
    if (context_ != nullptr && source_id_ != 0) {
      if (auto* source = g_main_context_find_source_by_id(context_, source_id_); source != nullptr) {
        g_source_destroy(source);
      }
    }
    if (server_ != nullptr) {
      g_object_unref(server_);
    }
    if (loop_ != nullptr) {
      g_main_loop_unref(loop_);
    }
    if (context_ != nullptr) {
      g_main_context_unref(context_);
    }

    context_ = nullptr;
    loop_ = nullptr;
    server_ = nullptr;
    factory_ = nullptr;
    source_id_ = 0;
    url_.clear();
  }

  ~LoopbackRtspFixture()
  {
    Stop();
  }

  void DropClients()
  {
    if (server_ == nullptr) {
      return;
    }
    const auto removed_count = gst_rtsp_server_client_filter(
      server_,
      [](GstRTSPServer*, GstRTSPClient*, gpointer) { return GST_RTSP_FILTER_REMOVE; },
      nullptr);
    (void)removed_count;
  }

  [[nodiscard]] const std::string& Url() const
  {
    return url_;
  }

private:
  GMainContext* context_{nullptr};
  GMainLoop* loop_{nullptr};
  GstRTSPServer* server_{nullptr};
  GstRTSPMediaFactory* factory_{nullptr};
  guint source_id_{0};
  std::thread thread_;
  std::string url_;
  std::uint32_t fps_{30};
  guint fixed_port_{0};
};

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

rch_camera_status_v1 Status(rch_engine_handle engine)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  if (rch_camera_get_status(engine, &status) != RCH_RESULT_OK) {
    status.state = RCH_CAMERA_STATE_FAILED;
    status.last_result = RCH_RESULT_INTERNAL_ERROR;
  }
  return status;
}

bool WaitForState(rch_engine_handle engine,
                  rch_camera_state target,
                  std::chrono::milliseconds timeout,
                  rch_camera_status_v1& out_status)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    out_status = Status(engine);
    if (out_status.active_rtsp_session_count > 1 || out_status.active_decoder_count > 1) {
      return false;
    }
    if (out_status.state == target) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  out_status = Status(engine);
  return false;
}

bool WaitForReceivingWithFrames(rch_engine_handle engine,
                                std::uint64_t minimum_increment,
                                std::chrono::milliseconds timeout,
                                rch_camera_status_v1& out_status)
{
  const auto baseline = Status(engine);
  const auto target = baseline.decoded_frame_count + minimum_increment;
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    out_status = Status(engine);
    if (out_status.active_rtsp_session_count > 1 || out_status.active_decoder_count > 1) {
      return false;
    }
    if (out_status.state == RCH_CAMERA_STATE_RECEIVING && out_status.decoded_frame_count >= target
        && out_status.has_latest_frame == 1) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  out_status = Status(engine);
  return false;
}

bool ValidateReconnectCycle(rch_engine_handle engine, LoopbackRtspFixture& fixture)
{
  rch_camera_status_v1 status{};
  if (!Expect(WaitForReceivingWithFrames(engine, 12, std::chrono::seconds(8), status),
              "pipeline must reach receiving with decoded frames")) {
    return false;
  }

  const auto pre_outage_sequence = status.latest_frame_sequence;

  fixture.DropClients();
  fixture.Stop();

  bool reached_outage_retry = false;
  const auto waiting_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(8);
  while (std::chrono::steady_clock::now() < waiting_deadline) {
    status = Status(engine);
    if (status.active_rtsp_session_count > 1 || status.active_decoder_count > 1) {
      return false;
    }
    if ((status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY || status.state == RCH_CAMERA_STATE_STARTING)
        && (status.last_result == RCH_RESULT_RTSP_FAILURE
            || status.last_result == RCH_RESULT_CONNECTION_TIMEOUT)) {
      reached_outage_retry = true;
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }

  if (!Expect(reached_outage_retry, "outage must transition into retry lifecycle")
      || !Expect(status.has_latest_frame == 0, "outage must clear latest-frame availability")
      || !Expect(status.active_rtsp_session_count == 0 && status.active_decoder_count == 0,
                 "outage teardown must release session/decoder ownership before retries")) {
    return false;
  }

  std::uint32_t last_delay = 0;
  bool saw_backoff_growth = false;
  const auto observe_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (std::chrono::steady_clock::now() < observe_deadline) {
    status = Status(engine);
    if (status.active_rtsp_session_count > 1 || status.active_decoder_count > 1) {
      return false;
    }
    if (status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY && status.next_retry_delay_ms > 0) {
      if (status.next_retry_delay_ms > last_delay) {
        saw_backoff_growth = true;
      }
      last_delay = status.next_retry_delay_ms;
    }
    if (status.reconnect_attempt_count >= 2) {
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }

  if (!Expect(status.reconnect_attempt_count >= 2, "outage must perform repeated reconnect attempts")
      || !Expect(saw_backoff_growth, "retry delay must grow under repeated failures")
      || !Expect(last_delay <= 2000, "retry delay must stay within bounded maximum")) {
    return false;
  }

  if (!Expect(fixture.Start(30), "fixture restart must succeed")) {
    return false;
  }

  if (!Expect(WaitForReceivingWithFrames(engine, 12, std::chrono::seconds(8), status),
              "recovery must automatically return to receiving")) {
    return false;
  }

  if (!Expect(status.reconnect_attempt_count == 0, "successful recovery must reset active attempt counter")
      || !Expect(status.successful_reconnect_count >= 1,
                 "successful recovery counter must increment")
      || !Expect(status.latest_frame_sequence > pre_outage_sequence,
                 "post-recovery media must advance frame sequence")
      || !Expect(status.has_latest_frame == 1, "receiving state must have a latest frame")
      || !Expect(status.latest_frame_age_ms < 1000, "recovered latest frame must be fresh")) {
    return false;
  }

  return true;
}

bool ExerciseStopDuringBackoff(rch_engine_handle engine, LoopbackRtspFixture& fixture)
{
  rch_camera_status_v1 status{};
  if (!Expect(WaitForReceivingWithFrames(engine, 10, std::chrono::seconds(8), status),
              "pipeline must reach receiving before stop-during-backoff")) {
    return false;
  }

  fixture.DropClients();
  fixture.Stop();
  if (!Expect(WaitForState(engine, RCH_CAMERA_STATE_WAITING_TO_RETRY, std::chrono::seconds(8), status),
              "outage must enter waiting-to-retry before stop")) {
    return false;
  }

  if (!Expect(rch_camera_stop(engine) == RCH_RESULT_OK, "stop during backoff must succeed promptly")) {
    return false;
  }
  status = Status(engine);
  if (!Expect(status.state == RCH_CAMERA_STATE_STOPPED, "stop must end in stopped state")
      || !Expect(status.active_rtsp_session_count == 0 && status.active_decoder_count == 0,
                 "stop must release ownership")
      || !Expect(status.has_latest_frame == 0, "stop must clear latest-frame state")
      || !Expect(status.next_retry_delay_ms == 0, "stop must cancel pending retry delay")) {
    return false;
  }

  std::this_thread::sleep_for(std::chrono::milliseconds(600));
  status = Status(engine);
  if (!Expect(status.state == RCH_CAMERA_STATE_STOPPED,
              "no delayed retry may restart after explicit stop")) {
    return false;
  }

  return true;
}

bool ExerciseDestroyDuringBackoff(LoopbackRtspFixture& fixture)
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine create must succeed")) {
    return false;
  }

  const rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    "loopback-profile2-destroy",
    fixture.Url().c_str(),
    2000,
    0,
  };

  if (!Expect(rch_camera_configure(engine, &config) == RCH_RESULT_OK, "configuration must succeed")
      || !Expect(rch_camera_start(engine) == RCH_RESULT_OK, "camera start must succeed")) {
    rch_engine_destroy(engine);
    return false;
  }

  rch_camera_status_v1 status{};
  if (!Expect(WaitForReceivingWithFrames(engine, 10, std::chrono::seconds(8), status),
              "pipeline must receive before teardown scenario")) {
    rch_engine_destroy(engine);
    return false;
  }

  fixture.DropClients();
  fixture.Stop();
  if (!Expect(WaitForState(engine, RCH_CAMERA_STATE_WAITING_TO_RETRY, std::chrono::seconds(8), status),
              "outage must enter waiting-to-retry before destroy")) {
    rch_engine_destroy(engine);
    return false;
  }

  return Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
                "engine destroy during backoff must tear down cleanly");
}

}  // namespace

int main()
{
  if (rch_get_abi_version() == 0) {
    return 1;
  }

  LoopbackRtspFixture fixture;
  if (!Expect(fixture.Start(30), "loopback fixture must start")) {
    return 1;
  }

  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine must initialise GStreamer")) {
    return 1;
  }

  const rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    "loopback-profile2",
    fixture.Url().c_str(),
    2000,
    0,
  };

  if (!Expect(rch_camera_configure(engine, &config) == RCH_RESULT_OK,
              "loopback source configuration must succeed")
      || !Expect(rch_camera_start(engine) == RCH_RESULT_OK, "production RTSP pipeline must start")) {
    rch_engine_destroy(engine);
    return 1;
  }

  bool passed = true;
  for (int cycle = 0; cycle < 3; ++cycle) {
    passed &= ValidateReconnectCycle(engine, fixture);
    if (!passed) {
      break;
    }
  }

  if (passed) {
    passed &= ExerciseStopDuringBackoff(engine, fixture);
  }

  if (passed) {
    fixture.Start(30);
    passed &= Expect(rch_camera_start(engine) == RCH_RESULT_OK, "restart after explicit stop must succeed");
    rch_camera_status_v1 status{};
    passed &= WaitForReceivingWithFrames(engine, 10, std::chrono::seconds(8), status);
  }

  if (passed) {
    passed &= Expect(rch_camera_stop(engine) == RCH_RESULT_OK, "final explicit stop must succeed");
    const auto status = Status(engine);
    passed &= Expect(status.state == RCH_CAMERA_STATE_STOPPED && status.active_rtsp_session_count == 0
                       && status.active_decoder_count == 0 && status.has_latest_frame == 0,
                     "final stop must release ownership and latest frame");
  }

  passed &= Expect(rch_engine_destroy(engine) == RCH_RESULT_OK, "engine destroy must succeed");

  if (passed) {
    fixture.Start(30);
    passed &= ExerciseDestroyDuringBackoff(fixture);
  }

  return passed ? 0 : 1;
}
