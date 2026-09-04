#include "robocamhub_native.h"

#include <gst/rtsp-server/rtsp-server.h>

#include <chrono>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>

namespace {

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

class LoopbackRtspFixture final {
public:
  bool Start(std::uint32_t fps)
  {
    Stop();

    context_ = g_main_context_new();
    loop_ = g_main_loop_new(context_, FALSE);
    server_ = gst_rtsp_server_new();
    gst_rtsp_server_set_address(server_, "127.0.0.1");
    const auto service = fixed_port_ == 0 ? std::string("0") : std::to_string(fixed_port_);
    gst_rtsp_server_set_service(server_, service.c_str());

    auto* mounts = gst_rtsp_server_get_mount_points(server_);
    factory_ = gst_rtsp_media_factory_new();
    const auto launch = "( videotestsrc is-live=true pattern=ball ! "
      "video/x-raw,format=I420,width=128,height=72,framerate=" + std::to_string(fps) + "/1 "
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
  guint fixed_port_{0};
};

rch_camera_status_v1 CameraStatus(rch_engine_handle engine, const char* camera_id, bool& ok)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  ok = rch_camera_get_status_by_id(engine, camera_id, &status) == RCH_RESULT_OK;
  return status;
}

rch_engine_diagnostics_v1 EngineDiagnostics(rch_engine_handle engine, bool& ok)
{
  rch_engine_diagnostics_v1 diagnostics{};
  diagnostics.struct_size = static_cast<std::uint32_t>(sizeof(diagnostics));
  diagnostics.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  ok = rch_engine_get_diagnostics(engine, &diagnostics) == RCH_RESULT_OK;
  return diagnostics;
}

rch_frame_lease_status_v1 LeaseStatus(rch_frame_lease_handle lease, bool& ok)
{
  rch_frame_lease_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_FRAME_LEASE_STATUS_VERSION;
  ok = rch_frame_lease_get_status(lease, &status) == RCH_RESULT_OK;
  return status;
}

bool WaitForReceiving(rch_engine_handle engine,
                      const char* camera_id,
                      std::chrono::milliseconds timeout,
                      rch_camera_status_v1& out_status)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  bool ok = false;
  while (std::chrono::steady_clock::now() < deadline) {
    out_status = CameraStatus(engine, camera_id, ok);
    if (!ok) {
      return false;
    }

    if (out_status.state == RCH_CAMERA_STATE_RECEIVING
        && out_status.has_latest_frame == 1
        && out_status.active_rtsp_session_count == 1
        && out_status.active_decoder_count == 1) {
      return true;
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }

  out_status = CameraStatus(engine, camera_id, ok);
  return false;
}

bool WaitForRetryState(rch_engine_handle engine,
                       const char* camera_id,
                       std::chrono::milliseconds timeout)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  bool ok = false;
  while (std::chrono::steady_clock::now() < deadline) {
    const auto status = CameraStatus(engine, camera_id, ok);
    if (!ok) {
      return false;
    }

    if (status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
        || status.state == RCH_CAMERA_STATE_STARTING
        || status.state == RCH_CAMERA_STATE_FAILED) {
      return true;
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }

  return false;
}

bool WaitForSequenceAdvance(rch_frame_consumer_handle consumer,
                            std::uint64_t baseline,
                            std::uint64_t increment,
                            std::chrono::milliseconds timeout,
                            std::uint64_t& out_sequence)
{
  const auto target = baseline + increment;
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    rch_frame_lease_handle lease = nullptr;
    if (rch_frame_consumer_acquire_latest(consumer, &lease) != RCH_RESULT_OK || lease == nullptr) {
      return false;
    }

    bool ok = false;
    const auto status = LeaseStatus(lease, ok);
    rch_frame_lease_destroy(lease);
    if (!ok) {
      return false;
    }

    if (status.has_frame == 1 && status.latest_frame_sequence >= target) {
      out_sequence = status.latest_frame_sequence;
      return true;
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  return false;
}

}  // namespace

int main()
{
  LoopbackRtspFixture fixture;
  if (!Expect(fixture.Start(30), "loopback fixture must start")) {
    return 1;
  }

  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine create must succeed")) {
    return 1;
  }

  const std::string camera_id = "fanout-cam";
  const rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    camera_id.c_str(),
    fixture.Url().c_str(),
    2500,
    0,
  };

  if (!Expect(rch_camera_add(engine, &config) == RCH_RESULT_OK, "camera add must succeed")
      || !Expect(rch_camera_start_by_id(engine, camera_id.c_str()) == RCH_RESULT_OK,
                 "camera start must succeed")) {
    rch_engine_destroy(engine);
    return 1;
  }

  bool ok = false;
  rch_camera_status_v1 camera_status{};
  if (!Expect(WaitForReceiving(engine, camera_id.c_str(), std::chrono::seconds(8), camera_status),
              "camera must reach receiving with one session and one decoder")) {
    rch_engine_destroy(engine);
    return 1;
  }

  rch_frame_consumer_handle consumer_a = nullptr;
  rch_frame_consumer_handle consumer_b = nullptr;
  if (!Expect(rch_frame_consumer_create(engine, camera_id.c_str(), &consumer_a) == RCH_RESULT_OK,
              "consumer A attach must succeed")
      || !Expect(consumer_a != nullptr, "consumer A handle must be returned")) {
    rch_engine_destroy(engine);
    return 1;
  }

  camera_status = CameraStatus(engine, camera_id.c_str(), ok);
  if (!Expect(ok, "camera status query must succeed after consumer A attach")
      || !Expect(camera_status.active_rtsp_session_count == 1 && camera_status.active_decoder_count == 1,
                 "consumer A must not change session or decoder count")
      || !Expect(camera_status.direct_frame_consumer_count == 1,
                 "camera status must report one direct frame consumer")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_frame_consumer_create(engine, camera_id.c_str(), &consumer_b) == RCH_RESULT_OK,
              "consumer B attach must succeed")
      || !Expect(consumer_b != nullptr, "consumer B handle must be returned")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  camera_status = CameraStatus(engine, camera_id.c_str(), ok);
  if (!Expect(ok, "camera status query must succeed after consumer B attach")
      || !Expect(camera_status.active_rtsp_session_count == 1 && camera_status.active_decoder_count == 1,
                 "consumer B must not change session or decoder count")
      || !Expect(camera_status.direct_frame_consumer_count == 2,
                 "camera status must report two direct frame consumers")) {
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_handle view = nullptr;
  if (!Expect(rch_view_create(engine, "view-a", &view) == RCH_RESULT_OK,
              "view create must succeed")
      || !Expect(rch_view_bind_camera_source(view, 0, camera_id.c_str()) == RCH_RESULT_OK,
                 "view source bind must succeed")) {
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  camera_status = CameraStatus(engine, camera_id.c_str(), ok);
  if (!Expect(ok, "camera status query must succeed after view bind")
      || !Expect(camera_status.active_rtsp_session_count == 1 && camera_status.active_decoder_count == 1,
                 "view binding must not change session or decoder count")
      || !Expect(camera_status.bound_view_source_count == 1,
                 "camera status must report one bound view source")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  auto diagnostics = EngineDiagnostics(engine, ok);
  if (!Expect(ok, "engine diagnostics must succeed")
      || !Expect(diagnostics.view_count == 1, "engine diagnostics must report one active view")
      || !Expect(diagnostics.active_rtsp_session_total == 1 && diagnostics.active_decoder_total == 1,
                 "diagnostics totals must remain one session and one decoder for one camera")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_frame_lease_handle held_by_a = nullptr;
  if (!Expect(rch_frame_consumer_acquire_latest(consumer_a, &held_by_a) == RCH_RESULT_OK,
              "consumer A must acquire a latest-frame lease")
      || !Expect(held_by_a != nullptr, "consumer A lease handle must be returned")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  const auto held_status = LeaseStatus(held_by_a, ok);
  if (!Expect(ok && held_status.has_frame == 1, "held lease must include frame metadata")) {
    rch_frame_lease_destroy(held_by_a);
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  std::uint64_t advanced_sequence = 0;
  if (!Expect(WaitForSequenceAdvance(
                consumer_b,
                held_status.latest_frame_sequence,
                20,
                std::chrono::seconds(6),
                advanced_sequence),
              "fast consumer must observe advancing sequence while slow consumer is paused")
      || !Expect(advanced_sequence > held_status.latest_frame_sequence,
                 "sequence must continue advancing independently of delayed consumer")) {
    rch_frame_lease_destroy(held_by_a);
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_frame_lease_destroy(held_by_a);
  held_by_a = nullptr;

  rch_frame_lease_handle resumed_a = nullptr;
  if (!Expect(rch_frame_consumer_acquire_latest(consumer_a, &resumed_a) == RCH_RESULT_OK,
              "resumed consumer must reacquire latest frame")
      || !Expect(resumed_a != nullptr, "resumed consumer lease handle must be returned")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  const auto resumed_status = LeaseStatus(resumed_a, ok);
  rch_frame_lease_destroy(resumed_a);
  if (!Expect(ok && resumed_status.has_frame == 1, "resumed consumer must observe a frame")
      || !Expect(resumed_status.latest_frame_sequence >= advanced_sequence,
                 "delayed consumer must resume on the newest frame without backlog")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_frame_consumer_destroy(consumer_b) == RCH_RESULT_OK,
              "destroying one consumer must succeed")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }
  consumer_b = nullptr;

  camera_status = CameraStatus(engine, camera_id.c_str(), ok);
  if (!Expect(ok, "camera status must remain queryable after consumer B destroy")
      || !Expect(camera_status.state == RCH_CAMERA_STATE_RECEIVING,
                 "destroying one consumer must not stop ingest")
      || !Expect(camera_status.direct_frame_consumer_count == 1,
                 "remaining direct consumer count must decrement")) {
    rch_view_destroy(view);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_view_destroy(view) == RCH_RESULT_OK, "destroying view must succeed")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }
  view = nullptr;

  camera_status = CameraStatus(engine, camera_id.c_str(), ok);
  if (!Expect(ok, "camera status must remain queryable after view destroy")
      || !Expect(camera_status.state == RCH_CAMERA_STATE_RECEIVING,
                 "destroying view must not stop ingest")
      || !Expect(camera_status.bound_view_source_count == 0,
                 "view binding count must return to zero")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_camera_stop_by_id(engine, camera_id.c_str()) == RCH_RESULT_OK,
              "stop while consumer remains attached must succeed")
      || !Expect(rch_camera_start_by_id(engine, camera_id.c_str()) == RCH_RESULT_OK,
                 "restart while consumer remains attached must succeed")
      || !Expect(WaitForReceiving(engine, camera_id.c_str(), std::chrono::seconds(8), camera_status),
                 "camera must return to receiving after restart")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  fixture.DropClients();
  fixture.Stop();
  if (!Expect(WaitForRetryState(engine, camera_id.c_str(), std::chrono::seconds(8)),
              "camera outage must enter retry/failure lifecycle with consumer attached")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(fixture.Start(30), "fixture restart must succeed for reconnect")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(WaitForReceiving(engine, camera_id.c_str(), std::chrono::seconds(8), camera_status),
              "camera must recover with the same attached consumer after reconnect")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_camera_remove(engine, camera_id.c_str()) == RCH_RESULT_OK,
              "camera removal must succeed with active consumer")
      || !Expect(rch_frame_consumer_acquire_latest(consumer_a, &held_by_a) == RCH_RESULT_NOT_CONFIGURED,
                 "active consumer must become stale after camera removal")
      || !Expect(rch_camera_get_status_by_id(engine, camera_id.c_str(), &camera_status) == RCH_RESULT_NOT_CONFIGURED,
                 "removed camera status must return not-configured")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  const rch_camera_config_v1 readd_config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    camera_id.c_str(),
    fixture.Url().c_str(),
    2500,
    0,
  };

  if (!Expect(rch_camera_add(engine, &readd_config) == RCH_RESULT_OK,
              "re-adding logical camera ID must succeed")
      || !Expect(rch_camera_start_by_id(engine, camera_id.c_str()) == RCH_RESULT_OK,
                 "re-added camera must start")
      || !Expect(WaitForReceiving(engine, camera_id.c_str(), std::chrono::seconds(8), camera_status),
                 "re-added camera must return to receiving")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_handle view_b = nullptr;
  if (!Expect(rch_view_create(engine, "view-b", &view_b) == RCH_RESULT_OK,
              "second view create must succeed")
      || !Expect(rch_view_bind_camera_source(view_b, 0, camera_id.c_str()) == RCH_RESULT_OK,
                 "second view bind must succeed")) {
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_frame_consumer_handle consumer_c = nullptr;
  if (!Expect(rch_frame_consumer_create(engine, camera_id.c_str(), &consumer_c) == RCH_RESULT_OK,
              "consumer C attach after re-add must succeed")) {
    rch_view_destroy(view_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  camera_status = CameraStatus(engine, camera_id.c_str(), ok);
  if (!Expect(ok, "status must succeed after re-add + rebind")
      || !Expect(camera_status.active_rtsp_session_count == 1 && camera_status.active_decoder_count == 1,
                 "re-add/rebind must not create duplicate ingest ownership")) {
    rch_frame_consumer_destroy(consumer_c);
    rch_view_destroy(view_b);
    rch_frame_consumer_destroy(consumer_a);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
              "engine destroy must be safe with active view/consumer handles")) {
    rch_frame_consumer_destroy(consumer_c);
    rch_view_destroy(view_b);
    rch_frame_consumer_destroy(consumer_a);
    return 1;
  }
  engine = nullptr;

  rch_view_status_v1 stale_view_status{};
  stale_view_status.struct_size = static_cast<std::uint32_t>(sizeof(stale_view_status));
  stale_view_status.struct_version = RCH_VIEW_STATUS_VERSION;

  if (!Expect(rch_frame_consumer_acquire_latest(consumer_c, &held_by_a) == RCH_RESULT_INVALID_HANDLE,
              "consumer handle must become invalid after engine destroy")
      || !Expect(rch_view_get_status(view_b, &stale_view_status) == RCH_RESULT_INVALID_HANDLE,
                 "view status must fail safely after engine destroy")
      || !Expect(rch_frame_consumer_destroy(consumer_c) == RCH_RESULT_OK,
                 "consumer destroy must remain safe after engine teardown")
      || !Expect(rch_view_destroy(view_b) == RCH_RESULT_OK,
                 "view destroy must remain safe after engine teardown")
      || !Expect(rch_frame_consumer_destroy(consumer_a) == RCH_RESULT_OK,
                 "stale direct consumer destroy must remain safe")) {
    return 1;
  }

  return 0;
}
