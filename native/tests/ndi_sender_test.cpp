#include "robocamhub_native.h"

#include <gst/rtsp-server/rtsp-server.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr std::uint32_t kFixtureWidth = 960;
constexpr std::uint32_t kFixtureHeight = 540;
constexpr std::uint32_t kViewTargetFps = 60;
constexpr std::uint32_t kCameraCount = 4U;

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

class LoopbackRtspFixture final {
public:
  bool Start(std::uint32_t index, std::uint32_t fps)
  {
    Stop();
    fps_ = fps;

    context_ = g_main_context_new();
    loop_ = g_main_loop_new(context_, FALSE);
    server_ = gst_rtsp_server_new();
    gst_rtsp_server_set_address(server_, "127.0.0.1");
    gst_rtsp_server_set_service(server_, "0");

    auto* mounts = gst_rtsp_server_get_mount_points(server_);
    factory_ = gst_rtsp_media_factory_new();
    const auto path = "/profile" + std::to_string(index) + "/media.smp";
    const auto launch = "( videotestsrc is-live=true pattern=ball ! "
      "video/x-raw,format=I420,width=" + std::to_string(kFixtureWidth)
      + ",height=" + std::to_string(kFixtureHeight)
      + ",framerate=" + std::to_string(fps_) + "/1 "
      "! x264enc tune=zerolatency speed-preset=ultrafast key-int-max=1 "
      "! rtph264pay name=pay0 pt=96 config-interval=1 )";
    gst_rtsp_media_factory_set_launch(factory_, launch.c_str());
    gst_rtsp_media_factory_set_protocols(factory_, GST_RTSP_LOWER_TRANS_UDP);
    gst_rtsp_mount_points_add_factory(mounts, path.c_str(), factory_);
    g_object_unref(mounts);

    source_id_ = gst_rtsp_server_attach(server_, context_);
    const auto port = gst_rtsp_server_get_bound_port(server_);
    if (source_id_ == 0 || port == 0) {
      Stop();
      return false;
    }

    url_ = "rtsp://127.0.0.1:" + std::to_string(port) + path;
    thread_ = std::thread([this] { g_main_loop_run(loop_); });
    return true;
  }

  void Stop()
  {
    if (server_ != nullptr) {
      (void)gst_rtsp_server_client_filter(
        server_,
        [](GstRTSPServer*, GstRTSPClient*, gpointer) { return GST_RTSP_FILTER_REMOVE; },
        nullptr);
    }
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
  std::uint32_t fps_{30};
};

rch_camera_status_v1 QueryCameraStatus(rch_engine_handle engine, const char* camera_id, bool& ok)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  ok = rch_camera_get_status_by_id(engine, camera_id, &status) == RCH_RESULT_OK;
  return status;
}

rch_engine_diagnostics_v1 QueryDiagnostics(rch_engine_handle engine, bool& ok)
{
  rch_engine_diagnostics_v1 diagnostics{};
  diagnostics.struct_size = static_cast<std::uint32_t>(sizeof(diagnostics));
  diagnostics.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  ok = rch_engine_get_diagnostics(engine, &diagnostics) == RCH_RESULT_OK;
  return diagnostics;
}

rch_view_status_v1 QueryViewStatus(rch_view_handle view, bool& ok)
{
  rch_view_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_VIEW_STATUS_VERSION;
  ok = rch_view_get_status(view, &status) == RCH_RESULT_OK;
  return status;
}

bool WaitForReceiving(rch_engine_handle engine,
                      const char* camera_id,
                      std::chrono::milliseconds timeout,
                      rch_camera_status_v1& out_status)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    bool ok = false;
    out_status = QueryCameraStatus(engine, camera_id, ok);
    if (ok && out_status.state == RCH_CAMERA_STATE_RECEIVING
        && out_status.active_rtsp_session_count == 1
        && out_status.active_decoder_count == 1
        && out_status.has_latest_frame == 1) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

rch_ndi_sender_status_v1 QuerySenderStatus(rch_ndi_sender_handle sender, bool& ok)
{
  rch_ndi_sender_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_NDI_SENDER_STATUS_VERSION;
  ok = rch_ndi_sender_get_status(sender, &status) == RCH_RESULT_OK;
  return status;
}

bool WaitForSenderActivity(rch_ndi_sender_handle sender)
{
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
  while (std::chrono::steady_clock::now() < deadline) {
    bool ok = false;
    const auto status = QuerySenderStatus(sender, ok);
    if (ok && (status.state == RCH_NDI_SENDER_STATE_RUNNING
               || status.state == RCH_NDI_SENDER_STATE_WAITING_FOR_VIEW_FRAME)) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  return false;
}

bool VerifySenderStatusCanaries(rch_ndi_sender_handle sender)
{
  constexpr std::size_t canary_size = 32U;
  constexpr auto v1_size = offsetof(rch_ndi_sender_status_v1, worker_tick_count);

  struct StatusWithCanary final {
    rch_ndi_sender_status_v1 status;
    std::array<std::uint8_t, canary_size> canary;
  };

  StatusWithCanary v1_storage{};
  std::memset(&v1_storage, 0xA5, sizeof(v1_storage));
  v1_storage.status.struct_size = static_cast<std::uint32_t>(sizeof(v1_storage));
  v1_storage.status.struct_version = RCH_NDI_SENDER_STATUS_VERSION_V1;
  const auto v1_result = rch_ndi_sender_get_status(sender, &v1_storage.status);
  const auto* v1_bytes = reinterpret_cast<const std::uint8_t*>(&v1_storage);
  const bool v1_canary_intact = std::all_of(
    v1_bytes + v1_size,
    v1_bytes + sizeof(v1_storage),
    [](std::uint8_t value) { return value == UINT8_C(0xA5); });

  StatusWithCanary v2_storage{};
  std::memset(&v2_storage, 0x5A, sizeof(v2_storage));
  v2_storage.status.struct_size = static_cast<std::uint32_t>(sizeof(v2_storage));
  v2_storage.status.struct_version = RCH_NDI_SENDER_STATUS_VERSION_V2;
  const auto v2_result = rch_ndi_sender_get_status(sender, &v2_storage.status);
  const auto* v2_canary = reinterpret_cast<const std::uint8_t*>(&v2_storage.canary);
  const bool v2_canary_intact = std::all_of(
    v2_canary,
    v2_canary + v2_storage.canary.size(),
    [](std::uint8_t value) { return value == UINT8_C(0x5A); });

  return Expect(v1_result == RCH_RESULT_OK,
                "sender status v1 query must accept a larger caller buffer")
    && Expect(v1_storage.status.struct_size == v1_size
              && v1_storage.status.struct_version == RCH_NDI_SENDER_STATUS_VERSION_V1,
              "sender status v1 query must report the v1 prefix size/version")
    && Expect(v1_canary_intact,
              "sender status v1 query must not write beyond the v1 prefix")
    && Expect(v2_result == RCH_RESULT_OK,
              "sender status v2 query must accept a larger caller buffer")
    && Expect(v2_storage.status.struct_size == sizeof(rch_ndi_sender_status_v1)
              && v2_storage.status.struct_version == RCH_NDI_SENDER_STATUS_VERSION_V2,
              "sender status v2 query must report the v2 size/version")
    && Expect(v2_canary_intact,
              "sender status v2 query must not write beyond the v2 structure");
}

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK,
              "engine creation must succeed for a sender smoke test")) {
    return 1;
  }

  std::vector<LoopbackRtspFixture> fixtures(kCameraCount);
  std::vector<std::string> camera_ids;
  camera_ids.reserve(kCameraCount);
  for (std::uint32_t i = 0; i < kCameraCount; ++i) {
    const auto camera_id = "cam-" + std::to_string(i);
    camera_ids.push_back(camera_id);
    if (!Expect(fixtures[i].Start(i + 1U, kViewTargetFps), "fixture startup must succeed for each camera in the 4-camera sender proof")) {
      rch_engine_destroy(engine);
      return 1;
    }

    rch_camera_config_v1 config{};
    config.struct_size = static_cast<std::uint32_t>(sizeof(config));
    config.struct_version = RCH_CAMERA_CONFIG_VERSION;
    config.camera_id_utf8 = camera_id.c_str();
    config.rtsp_url_utf8 = fixtures[i].Url().c_str();
    config.connect_timeout_ms = 2000U;
    config.reserved = 0U;
    if (!Expect(rch_camera_add(engine, &config) == RCH_RESULT_OK,
                "camera configuration must succeed for each logical camera in the 4-camera proof")) {
      rch_engine_destroy(engine);
      return 1;
    }
    if (!Expect(rch_camera_start_by_id(engine, camera_id.c_str()) == RCH_RESULT_OK,
                "camera start must succeed for each logical camera in the 4-camera proof")) {
      rch_engine_destroy(engine);
      return 1;
    }
  }

  for (std::uint32_t i = 0; i < kCameraCount; ++i) {
    bool status_ok = false;
    rch_camera_status_v1 status = QueryCameraStatus(engine, camera_ids[i].c_str(), status_ok);
    if (!Expect(status_ok, "camera status query must succeed before sender validation")
        || !Expect(WaitForReceiving(engine, camera_ids[i].c_str(), std::chrono::seconds(12), status),
                   "each configured camera must reach receiving before starting the sender proof")) {
      rch_engine_destroy(engine);
      return 1;
    }
    if (!Expect(status.active_rtsp_session_count == 1 && status.active_decoder_count == 1,
                "4-camera proof must preserve one RTSP session and one decoder per configured camera")) {
      rch_engine_destroy(engine);
      return 1;
    }
  }

  bool diagnostics_ok = false;
  const auto diagnostics = QueryDiagnostics(engine, diagnostics_ok);
  if (!Expect(diagnostics_ok,
              "engine diagnostics must succeed while the four-camera fixture is receiving")
      || !Expect(diagnostics.configured_camera_count == kCameraCount,
                 "configured camera count must remain exactly four")
      || !Expect(diagnostics.active_rtsp_session_total == kCameraCount,
                 "aggregate RTSP totals must remain exactly four during sender operation")
      || !Expect(diagnostics.active_decoder_total == kCameraCount,
                 "aggregate decoder totals must remain exactly four during sender operation")) {
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_handle view = nullptr;
  if (!Expect(rch_view_create(engine, "gate4a-view", &view) == RCH_RESULT_OK,
              "view creation must succeed before sender creation")) {
    rch_engine_destroy(engine);
    return 1;
  }
  for (std::uint32_t i = 0; i < kCameraCount; ++i) {
    if (!Expect(rch_view_bind_camera_source(view, i, camera_ids[i].c_str()) == RCH_RESULT_OK,
                "all four sources must bind cleanly to the 2×2 View")) {
      rch_view_destroy(view);
      rch_engine_destroy(engine);
      return 1;
    }
  }

  bool initial_view_ok = false;
  auto initial_view_status = QueryViewStatus(view, initial_view_ok);
  if (!Expect(initial_view_ok, "view status query must succeed before sender start")
      || !Expect(initial_view_status.bound_source_count == kCameraCount,
                 "2×2 View must bind exactly four camera sources")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_ndi_sender_handle sender = nullptr;
  if (!Expect(rch_ndi_sender_create(view, "ROBOCAM - Gate4A", &sender) == RCH_RESULT_OK,
              "sender creation must attach to an existing native View")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  if (!Expect(rch_ndi_sender_start(sender) == RCH_RESULT_OK,
              "sender start must begin the bounded View-frame worker")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!VerifySenderStatusCanaries(sender)) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  bool observed_sequence = false;
  bool observed_sender_activity = false;
  bool observed_counter_relationships = false;
  bool observed_receiver_diagnostics = false;
  for (int iteration = 0; iteration < 100; ++iteration) {
    bool view_ok = false;
    const auto view_status = QueryViewStatus(view, view_ok);
    if (view_ok && view_status.latest_composed_frame_sequence > 0ULL) {
      observed_sequence = true;
    }

    rch_ndi_sender_status_v1 sender_status{};
    sender_status.struct_size = sizeof(sender_status);
    sender_status.struct_version = RCH_NDI_SENDER_STATUS_VERSION;
    const auto sender_result = rch_ndi_sender_get_status(sender, &sender_status);
    if (sender_result == RCH_RESULT_OK && (sender_status.sent_frame_count > 0ULL
        || sender_status.state == RCH_NDI_SENDER_STATE_WAITING_FOR_VIEW_FRAME)) {
      observed_sender_activity = true;
    }
    if (sender_result == RCH_RESULT_OK
        && sender_status.worker_tick_count >= sender_status.unique_sequence_observed_count
        && sender_status.unique_sequence_observed_count >= sender_status.sent_frame_count) {
      observed_counter_relationships = true;
    }
    if (sender_result == RCH_RESULT_OK) {
      const bool official_sdk_diagnostics = sender_status.reserved_v2 != 0U
        && sender_status.sent_frame_count > 0U
        && sender_status.receiver_count_known == 1U;
      const bool deterministic_diagnostics = sender_status.reserved_v2 == 0U
        && sender_status.receiver_count_known == 0U
        && sender_status.receiver_count == 0U;
      observed_receiver_diagnostics = official_sdk_diagnostics || deterministic_diagnostics;
    }

    if (observed_sequence
        && observed_sender_activity
        && observed_counter_relationships
        && observed_receiver_diagnostics) {
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(15));
  }

  if (!Expect(observed_sequence,
              "View sequence must advance while sender is active")
      || !Expect(observed_sender_activity,
       "sender status must observe sending or waiting-for-frame while active")
        || !Expect(observed_counter_relationships,
       "worker tick and unique sequence counters must stay internally consistent")
        || !Expect(observed_receiver_diagnostics,
       "sender backend must report receiver diagnostics according to its capability")) {
    rch_ndi_sender_stop(sender);
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  bool active_diag_ok = false;
  const auto active_diagnostics = QueryDiagnostics(engine, active_diag_ok);
  if (!Expect(active_diag_ok,
              "engine diagnostics must remain queryable while the sender is active")
      || !Expect(active_diagnostics.configured_camera_count == kCameraCount,
                 "sender operation must not add/remove configured camera owners")
      || !Expect(active_diagnostics.active_rtsp_session_total == kCameraCount,
                 "sender operation must not create extra RTSP sessions")
      || !Expect(active_diagnostics.active_decoder_total == kCameraCount,
                 "sender operation must not create extra decoders")) {
    rch_ndi_sender_stop(sender);
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_handle destroyed_while_active_view = nullptr;
  rch_ndi_sender_handle destroyed_while_active_sender = nullptr;
  if (!Expect(rch_view_create(engine, "destroy-while-sender-active",
                              &destroyed_while_active_view) == RCH_RESULT_OK,
              "auxiliary View creation must succeed for active-destroy regression")
      || !Expect(rch_ndi_sender_create(destroyed_while_active_view,
                                       "ROBOCAM - Active View Destroy",
                                       &destroyed_while_active_sender) == RCH_RESULT_OK,
                 "auxiliary sender creation must succeed for active-destroy regression")
      || !Expect(rch_ndi_sender_start(destroyed_while_active_sender) == RCH_RESULT_OK,
                 "auxiliary sender must start before destroying its View")
      || !Expect(WaitForSenderActivity(destroyed_while_active_sender),
                 "auxiliary sender must become active before destroying its View")
      || !Expect(rch_view_destroy(destroyed_while_active_view) == RCH_RESULT_OK,
                 "destroying a View with an active sender must stop View ownership safely")) {
    if (destroyed_while_active_sender != nullptr) {
      rch_ndi_sender_destroy(destroyed_while_active_sender);
    }
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  bool removed_view_sender_ok = false;
  const auto removed_view_sender_status =
    QuerySenderStatus(destroyed_while_active_sender, removed_view_sender_ok);
  if (!Expect(removed_view_sender_ok,
              "sender status must remain safely queryable after active View destruction")
      || !Expect(removed_view_sender_status.state == RCH_NDI_SENDER_STATE_FAILED
                 && removed_view_sender_status.last_result == RCH_RESULT_INVALID_HANDLE,
                 "sender must report its destroyed View without using stale ownership")
      || !Expect(rch_ndi_sender_destroy(destroyed_while_active_sender) == RCH_RESULT_OK,
                 "sender destroy must join safely after its active View is destroyed")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  bool after_active_view_destroy_ok = false;
  const auto after_active_view_destroy = QueryDiagnostics(engine, after_active_view_destroy_ok);
  if (!Expect(after_active_view_destroy_ok,
              "engine diagnostics must survive active View/sender destruction")
      || !Expect(after_active_view_destroy.active_rtsp_session_total == kCameraCount
                 && after_active_view_destroy.active_decoder_total == kCameraCount,
                 "active View/sender destruction must preserve exact 4/4 ingest ownership")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_stop(sender) == RCH_RESULT_OK,
              "sender stop must release the worker without disturbing the underlying View")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  bool stopped_diag_ok = false;
  const auto stopped_diagnostics = QueryDiagnostics(engine, stopped_diag_ok);
  if (!Expect(stopped_diag_ok,
              "diagnostics must remain available after sender stop")
      || !Expect(stopped_diagnostics.active_rtsp_session_total == kCameraCount,
                 "sender stop must not disturb the 4-camera RTSP total")
      || !Expect(stopped_diagnostics.active_decoder_total == kCameraCount,
                 "sender stop must not disturb the 4-camera decoder total")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  for (int repeat = 0; repeat < 3; ++repeat) {
    if (!Expect(rch_ndi_sender_start(sender) == RCH_RESULT_OK,
                "re-starting the same sender must succeed over a bounded lifecycle")) {
      rch_ndi_sender_destroy(sender);
      rch_view_destroy(view);
      rch_engine_destroy(engine);
      return 1;
    }
    if (!Expect(rch_ndi_sender_stop(sender) == RCH_RESULT_OK,
                "repeated stop calls must remain deterministic with no sender leak")) {
      rch_ndi_sender_destroy(sender);
      rch_view_destroy(view);
      rch_engine_destroy(engine);
      return 1;
    }
  }

  if (!Expect(rch_ndi_sender_destroy(sender) == RCH_RESULT_OK,
              "sender destruction must release sender ownership cleanly")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_view_destroy(view) == RCH_RESULT_OK,
              "view destroy must be safe when the sender has already been torn down")) {
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
              "engine destroy must release all native objects cleanly")) {
    return 1;
  }

  rch_engine_handle teardown_engine = nullptr;
  rch_view_handle teardown_view = nullptr;
  rch_ndi_sender_handle teardown_sender = nullptr;
  if (!Expect(rch_engine_create(&teardown_engine) == RCH_RESULT_OK,
              "engine creation must succeed for active-sender engine teardown")
      || !Expect(rch_view_create(teardown_engine, "engine-teardown-view",
                                 &teardown_view) == RCH_RESULT_OK,
                 "View creation must succeed for active-sender engine teardown")
      || !Expect(rch_ndi_sender_create(teardown_view, "ROBOCAM - Engine Teardown",
                                       &teardown_sender) == RCH_RESULT_OK,
                 "sender creation must succeed for active-sender engine teardown")
      || !Expect(rch_ndi_sender_start(teardown_sender) == RCH_RESULT_OK,
                 "sender must start before engine teardown")
      || !Expect(WaitForSenderActivity(teardown_sender),
                 "sender must become active before engine teardown")) {
    if (teardown_sender != nullptr) {
      rch_ndi_sender_destroy(teardown_sender);
    }
    if (teardown_view != nullptr) {
      rch_view_destroy(teardown_view);
    }
    if (teardown_engine != nullptr) {
      rch_engine_destroy(teardown_engine);
    }
    return 1;
  }
  if (!Expect(rch_engine_destroy(teardown_engine) == RCH_RESULT_OK,
              "engine teardown must remain safe while a sender is active")) {
    rch_ndi_sender_destroy(teardown_sender);
    rch_view_destroy(teardown_view);
    return 1;
  }

  bool engine_teardown_sender_ok = false;
  const auto engine_teardown_sender_status =
    QuerySenderStatus(teardown_sender, engine_teardown_sender_ok);
  if (!Expect(engine_teardown_sender_ok,
              "sender status must remain safely queryable after engine teardown")
      || !Expect(engine_teardown_sender_status.state == RCH_NDI_SENDER_STATE_FAILED
                 && engine_teardown_sender_status.last_result == RCH_RESULT_INVALID_HANDLE,
                 "active sender must observe engine/View teardown deterministically")
      || !Expect(rch_ndi_sender_destroy(teardown_sender) == RCH_RESULT_OK,
                 "sender destroy must safely join after engine teardown")
      || !Expect(rch_view_destroy(teardown_view) == RCH_RESULT_OK,
                 "View handle cleanup must remain safe after engine teardown")) {
    return 1;
  }

  return 0;
}
