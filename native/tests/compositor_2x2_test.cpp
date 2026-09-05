#include "robocamhub_native.h"

#include <gst/rtsp-server/rtsp-server.h>

#include <chrono>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr std::uint32_t kFixtureWidth = 960;
constexpr std::uint32_t kFixtureHeight = 540;
constexpr std::uint32_t kViewTargetFps = 60;
constexpr std::uint64_t kProgressAdvanceFrames = 4;
constexpr auto kProgressTimeout = std::chrono::seconds(20);

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

class LoopbackRtspFixture final {
public:
  bool Start(const std::string& pattern, std::uint32_t fps)
  {
    Stop();

    pattern_ = pattern;
    fps_ = fps;
    context_ = g_main_context_new();
    loop_ = g_main_loop_new(context_, FALSE);
    server_ = gst_rtsp_server_new();
    gst_rtsp_server_set_address(server_, "127.0.0.1");
    const auto service = fixed_port_ == 0 ? std::string("0") : std::to_string(fixed_port_);
    gst_rtsp_server_set_service(server_, service.c_str());

    auto* mounts = gst_rtsp_server_get_mount_points(server_);
    factory_ = gst_rtsp_media_factory_new();
    const auto launch = "( videotestsrc is-live=true pattern=" + pattern + " ! "
      "video/x-raw,format=RGBA,width=" + std::to_string(kFixtureWidth)
      + ",height=" + std::to_string(kFixtureHeight)
      + ",framerate=" + std::to_string(fps_) + "/1 "
      "! videoconvert "
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
    if (server_ != nullptr) {
      const auto removed_count = gst_rtsp_server_client_filter(
        server_,
        [](GstRTSPServer*, GstRTSPClient*, gpointer) { return GST_RTSP_FILTER_REMOVE; },
        nullptr);
      (void)removed_count;
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

  [[nodiscard]] const std::string& Pattern() const
  {
    return pattern_;
  }

private:
  GMainContext* context_{nullptr};
  GMainLoop* loop_{nullptr};
  GstRTSPServer* server_{nullptr};
  GstRTSPMediaFactory* factory_{nullptr};
  guint source_id_{0};
  std::thread thread_;
  std::string url_;
  std::string pattern_;
  std::uint32_t fps_{30};
  guint fixed_port_{0};
};

rch_camera_status_v1 QueryCameraStatus(rch_engine_handle engine, const std::string& camera_id, bool& ok)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  ok = rch_camera_get_status_by_id(engine, camera_id.c_str(), &status) == RCH_RESULT_OK;
  return status;
}

rch_engine_diagnostics_v1 QueryEngineDiagnostics(rch_engine_handle engine, bool& ok)
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

bool ValidateViewStatusVersionCompatibility(rch_view_handle view,
                                          uint32_t version,
                                          uint32_t expected_size,
                                          const char* label)
{
  alignas(rch_view_status_v1) std::uint8_t canary_buffer[sizeof(rch_view_status_v1) + 32];
  std::memset(canary_buffer, 0xA5, sizeof(canary_buffer));
  auto* status = reinterpret_cast<rch_view_status_v1*>(canary_buffer);
  status->struct_size = expected_size;
  status->struct_version = version;

  const auto ok = rch_view_get_status(view, status) == RCH_RESULT_OK;
  if (!ok || status->struct_size != expected_size || status->struct_version != version) {
    return false;
  }

  for (std::size_t i = expected_size; i < sizeof(canary_buffer); ++i) {
    if (canary_buffer[i] != static_cast<std::uint8_t>(0xA5)) {
      std::cerr << "FAILED: " << label << " canary bytes were overwritten\n";
      return false;
    }
  }

  return true;
}

rch_view_frame_lease_status_v1 QueryViewLeaseStatus(rch_view_frame_lease_handle lease, bool& ok)
{
  rch_view_frame_lease_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_VIEW_FRAME_LEASE_STATUS_VERSION;
  ok = rch_view_frame_lease_get_status(lease, &status) == RCH_RESULT_OK;
  return status;
}

bool WaitForReceiving(rch_engine_handle engine,
                      const std::string& camera_id,
                      std::chrono::milliseconds timeout)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    bool ok = false;
    const auto status = QueryCameraStatus(engine, camera_id, ok);
    if (!ok) {
      return false;
    }
    if (status.state == RCH_CAMERA_STATE_RECEIVING
        && status.active_rtsp_session_count == 1
        && status.active_decoder_count == 1
        && status.has_latest_frame == 1) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }

  return false;
}

bool WaitForViewSequenceAdvance(rch_view_handle view,
                                std::uint64_t baseline,
                                std::uint64_t advance,
                                std::chrono::milliseconds timeout,
                                rch_view_status_v1& out_status)
{
  const auto target_sequence = baseline + advance;
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    bool ok = false;
    out_status = QueryViewStatus(view, ok);
    if (!ok) {
      return false;
    }
    if (out_status.latest_composed_frame_sequence >= target_sequence
        && out_status.render_frame_count >= target_sequence) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(15));
  }
  return false;
}

bool WaitForViewForwardProgress(rch_view_handle view,
                                std::uint64_t required_advance,
                                std::chrono::milliseconds timeout,
                                rch_view_status_v1& out_status)
{
  bool ok = false;
  const auto baseline = QueryViewStatus(view, ok);
  if (!ok) {
    return false;
  }

  return WaitForViewSequenceAdvance(
    view,
    baseline.latest_composed_frame_sequence,
    required_advance,
    timeout,
    out_status);
}

bool WaitForCameraOutageState(rch_engine_handle engine,
                              const std::string& camera_id,
                              std::chrono::milliseconds timeout)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    bool ok = false;
    const auto status = QueryCameraStatus(engine, camera_id, ok);
    if (!ok) {
      return false;
    }
    if (status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
        || status.state == RCH_CAMERA_STATE_FAILED
        || status.state == RCH_CAMERA_STATE_STARTING) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

bool IsMostlyRed(std::uint8_t r, std::uint8_t g, std::uint8_t b)
{
  return r > 150U && g < 110U && b < 110U;
}

bool IsMostlyGreen(std::uint8_t r, std::uint8_t g, std::uint8_t b)
{
  return g > 150U && r < 110U && b < 110U;
}

bool IsMostlyBlue(std::uint8_t r, std::uint8_t g, std::uint8_t b)
{
  return b > 150U && r < 110U && g < 110U;
}

bool IsMostlyWhite(std::uint8_t r, std::uint8_t g, std::uint8_t b)
{
  return r > 150U && g > 150U && b > 150U;
}

bool SamplePixel(rch_view_frame_lease_handle lease,
                 std::uint32_t x,
                 std::uint32_t y,
                 std::uint8_t& r,
                 std::uint8_t& g,
                 std::uint8_t& b,
                 std::uint8_t& a)
{
  return rch_view_frame_lease_sample_rgba(lease, x, y, &r, &g, &b, &a) == RCH_RESULT_OK;
}

}  // namespace

int main()
{
  std::vector<LoopbackRtspFixture> fixtures(4);
  if (!Expect(fixtures[0].Start("red", 30), "fixture 0 must start")
      || !Expect(fixtures[1].Start("green", 30), "fixture 1 must start")
      || !Expect(fixtures[2].Start("blue", 30), "fixture 2 must start")
      || !Expect(fixtures[3].Start("white", 8), "fixture 3 must start")) {
    return 1;
  }

  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine create must succeed")) {
    return 1;
  }

  const std::vector<std::string> camera_ids{
    "gate3b-cam-1",
    "gate3b-cam-2",
    "gate3b-cam-3",
    "gate3b-cam-4",
  };

  bool ok = false;
  for (std::size_t i = 0; i < camera_ids.size(); ++i) {
    const rch_camera_config_v1 config{
      static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
      RCH_CAMERA_CONFIG_VERSION,
      camera_ids[i].c_str(),
      fixtures[i].Url().c_str(),
      2500,
      0,
    };

    if (!Expect(rch_camera_add(engine, &config) == RCH_RESULT_OK, "camera add must succeed")
        || !Expect(rch_camera_start_by_id(engine, camera_ids[i].c_str()) == RCH_RESULT_OK,
                   "camera start must succeed")
        || !Expect(WaitForReceiving(engine, camera_ids[i], std::chrono::seconds(8)),
                   "camera must reach receiving state")) {
      rch_engine_destroy(engine);
      return 1;
    }
  }

  auto diagnostics = QueryEngineDiagnostics(engine, ok);
  if (!Expect(ok, "engine diagnostics query must succeed")
      || !Expect(diagnostics.configured_camera_count == 4, "configured camera count must be 4")
      || !Expect(diagnostics.active_rtsp_session_total == 4, "RTSP ownership total must be exactly 4")
      || !Expect(diagnostics.active_decoder_total == 4, "decoder ownership total must be exactly 4")) {
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_handle view = nullptr;
  if (!Expect(rch_view_create(engine, "gate3b-view", &view) == RCH_RESULT_OK, "view create must succeed")) {
    rch_engine_destroy(engine);
    return 1;
  }

  for (std::size_t i = 0; i < camera_ids.size(); ++i) {
    if (!Expect(rch_view_bind_camera_source(view, static_cast<std::uint32_t>(i), camera_ids[i].c_str()) == RCH_RESULT_OK,
                "view source bind must succeed")) {
      rch_view_destroy(view);
      rch_engine_destroy(engine);
      return 1;
    }
  }

  rch_view_source_status_v1 live_slot_status{};
  live_slot_status.struct_size = sizeof(live_slot_status);
  live_slot_status.struct_version = RCH_VIEW_SOURCE_STATUS_VERSION;
  if (!Expect(rch_view_get_source_status(view, 0, &live_slot_status) == RCH_RESULT_OK,
              "source-slot status query must succeed")
      || !Expect(live_slot_status.source_state == RCH_VIEW_SOURCE_STATE_LIVE,
                 "live source slot must report Live state")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_status_v1 view_status{};
  if (!Expect(WaitForViewForwardProgress(view, kProgressAdvanceFrames, kProgressTimeout, view_status),
              "view composed sequence must advance")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  diagnostics = QueryEngineDiagnostics(engine, ok);
  if (!Expect(ok, "engine diagnostics must remain queryable after view bind")
      || !Expect(diagnostics.active_rtsp_session_total == 4, "view bind must not increase RTSP ownership")
      || !Expect(diagnostics.active_decoder_total == 4, "view bind must not increase decoder ownership")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(view_status.configured_width == 1920 && view_status.configured_height == 1080,
              "view configured output must be exactly 1920x1080")
      || !Expect(view_status.render_state == RCH_VIEW_RENDER_STATE_RUNNING,
                 "view render state must be running")
      || !Expect(view_status.bound_source_count == 4,
                 "view must report exactly four bound sources")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_frame_lease_handle composed_lease = nullptr;
  if (!Expect(rch_view_acquire_latest_frame(view, &composed_lease) == RCH_RESULT_OK,
              "view latest composed frame lease must be acquirable")
      || !Expect(composed_lease != nullptr, "view composed lease handle must be returned")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  const auto composed_status = QueryViewLeaseStatus(composed_lease, ok);
  if (!Expect(ok && composed_status.has_frame == 1, "composed lease must report a frame")
      || !Expect(composed_status.width == 1920 && composed_status.height == 1080,
                 "composed lease dimensions must be exactly 1920x1080")) {
    rch_view_frame_lease_destroy(composed_lease);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  std::uint8_t r = 0;
  std::uint8_t g = 0;
  std::uint8_t b = 0;
  std::uint8_t a = 0;

  if (!Expect(SamplePixel(composed_lease, 480, 270, r, g, b, a) && IsMostlyRed(r, g, b),
              "top-left quadrant must map to red source")
      || !Expect(SamplePixel(composed_lease, 1440, 270, r, g, b, a) && IsMostlyGreen(r, g, b),
                 "top-right quadrant must map to green source")
      || !Expect(SamplePixel(composed_lease, 480, 810, r, g, b, a) && IsMostlyBlue(r, g, b),
                 "bottom-left quadrant must map to blue source")
      || !Expect(SamplePixel(composed_lease, 1440, 810, r, g, b, a) && IsMostlyWhite(r, g, b),
                 "bottom-right quadrant must map to white source")) {
    rch_view_frame_lease_destroy(composed_lease);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_view_frame_lease_destroy(composed_lease) == RCH_RESULT_OK,
              "composed lease destroy must succeed")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  fixtures[1].Stop();
  if (!Expect(WaitForCameraOutageState(engine, camera_ids[1], std::chrono::seconds(8)),
              "one source outage must enter retry/failure lifecycle")
      || !Expect(WaitForViewForwardProgress(view, kProgressAdvanceFrames, kProgressTimeout, view_status),
                 "view must continue rendering while one source is down")
      || !Expect(view_status.sources_contributing_count >= 3,
                 "other three sources must continue contributing while one source is down")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_source_status_v1 outage_slot_status{};
  outage_slot_status.struct_size = sizeof(outage_slot_status);
  outage_slot_status.struct_version = RCH_VIEW_SOURCE_STATUS_VERSION;
  if (!Expect(rch_view_get_source_status(view, 1, &outage_slot_status) == RCH_RESULT_OK,
              "source-slot status query must remain valid during outage")
      || !Expect(outage_slot_status.camera_state == RCH_CAMERA_STATE_WAITING_TO_RETRY
                   || outage_slot_status.camera_state == RCH_CAMERA_STATE_STARTING
                   || outage_slot_status.camera_state == RCH_CAMERA_STATE_FAILED,
                 "underlying camera must remain in reconnect lifecycle while outage is active")
      || !Expect(outage_slot_status.source_state == RCH_VIEW_SOURCE_STATE_FROZEN_LAST_GOOD,
                 "prior live frame must render as frozen last-good while reconnecting source is still cached")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(fixtures[1].Start("green", 30), "fixture restart must succeed")
      || !Expect(WaitForReceiving(engine, camera_ids[1], std::chrono::seconds(8)),
                 "camera must recover after fixture restart")
      || !Expect(WaitForViewForwardProgress(view, kProgressAdvanceFrames, kProgressTimeout, view_status),
                 "view must continue and include recovered source")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_camera_remove(engine, camera_ids[2].c_str()) == RCH_RESULT_OK,
              "camera removal while bound must succeed")
      || !Expect(WaitForViewForwardProgress(view, kProgressAdvanceFrames, kProgressTimeout, view_status),
                 "view must stay alive after bound camera removal")
      || !Expect(view_status.stale_or_missing_source_count >= 1,
                 "removed bound source must be reported as stale/missing")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  const rch_camera_config_v1 readd_config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    camera_ids[2].c_str(),
    fixtures[2].Url().c_str(),
    2500,
    0,
  };

  if (!Expect(rch_camera_add(engine, &readd_config) == RCH_RESULT_OK,
              "re-add camera must succeed")
      || !Expect(rch_camera_start_by_id(engine, camera_ids[2].c_str()) == RCH_RESULT_OK,
                 "re-added camera start must succeed")
      || !Expect(WaitForReceiving(engine, camera_ids[2], std::chrono::seconds(8)),
                 "re-added camera must return to receiving")
      || !Expect(rch_view_bind_camera_source(view, 2, camera_ids[2].c_str()) == RCH_RESULT_OK,
                 "explicit rebind after re-add must succeed")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  diagnostics = QueryEngineDiagnostics(engine, ok);
  if (!Expect(ok, "engine diagnostics must succeed after re-add/rebind")
      || !Expect(diagnostics.configured_camera_count == 4, "re-add must keep configured camera count at 4")
      || !Expect(diagnostics.active_rtsp_session_total == 4, "re-add/rebind must keep RTSP total at 4")
      || !Expect(diagnostics.active_decoder_total == 4, "re-add/rebind must keep decoder total at 4")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(ValidateViewStatusVersionCompatibility(view,
                                                    RCH_VIEW_STATUS_VERSION_V1,
                                                    static_cast<uint32_t>(offsetof(rch_view_status_v1, render_state)),
                                                    "view status v1 caller compatibility"),
              "v1 view status caller must remain compatible")
      || !Expect(ValidateViewStatusVersionCompatibility(view,
                                                      RCH_VIEW_STATUS_VERSION_V2,
                                                      static_cast<uint32_t>(offsetof(rch_view_status_v1, live_source_count)),
                                                      "view status v2 caller compatibility"),
                 "v2 view status caller must remain compatible")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_view_destroy(view) == RCH_RESULT_OK, "view destroy must succeed while cameras stay active")) {
    rch_engine_destroy(engine);
    return 1;
  }

  for (const auto& camera_id : camera_ids) {
    if (!Expect(WaitForReceiving(engine, camera_id, std::chrono::seconds(8)),
                "destroying the view must not stop camera ingest")) {
      rch_engine_destroy(engine);
      return 1;
    }
  }

  rch_view_handle teardown_view = nullptr;
  if (!Expect(rch_view_create(engine, "gate3b-teardown", &teardown_view) == RCH_RESULT_OK,
              "teardown view create must succeed")) {
    rch_engine_destroy(engine);
    return 1;
  }

  for (std::size_t i = 0; i < camera_ids.size(); ++i) {
    if (!Expect(rch_view_bind_camera_source(teardown_view,
                                            static_cast<std::uint32_t>(i),
                                            camera_ids[i].c_str()) == RCH_RESULT_OK,
                "teardown view source bind must succeed")) {
      rch_view_destroy(teardown_view);
      rch_engine_destroy(engine);
      return 1;
    }
  }

  rch_view_frame_lease_handle teardown_lease = nullptr;
  if (!Expect(rch_view_acquire_latest_frame(teardown_view, &teardown_lease) == RCH_RESULT_OK,
              "teardown lease acquire must succeed")
      || !Expect(teardown_lease != nullptr, "teardown lease handle must be returned")) {
    rch_view_destroy(teardown_view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
              "engine teardown with active view output lease must be safe")) {
    rch_view_frame_lease_destroy(teardown_lease);
    rch_view_destroy(teardown_view);
    return 1;
  }

  if (!Expect(rch_view_frame_lease_destroy(teardown_lease) == RCH_RESULT_OK,
              "lease destroy after engine teardown must be safe")
      || !Expect(rch_view_destroy(teardown_view) == RCH_RESULT_OK,
                 "view destroy after engine teardown must be safe")) {
    return 1;
  }

  std::cout
    << "Gate3B baseline: target_fps=" << kViewTargetFps
    << " render_fps=" << (view_status.render_fps_milli / 1000.0)
    << " avg_render_us=" << view_status.average_render_duration_us
    << " p95_render_us=" << view_status.p95_render_duration_us
    << " stale_source_frames=" << view_status.stale_source_frame_count
    << " composed_age_ms=" << view_status.latest_composed_frame_age_ms
    << "\n";

  return 0;
}
