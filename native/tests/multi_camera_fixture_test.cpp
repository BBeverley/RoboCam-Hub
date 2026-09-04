#include "robocamhub_native.h"

#include <gst/rtsp-server/rtsp-server.h>

#include <chrono>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

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
    fps_ = fps;

    context_ = g_main_context_new();
    loop_ = g_main_loop_new(context_, FALSE);
    server_ = gst_rtsp_server_new();
    gst_rtsp_server_set_address(server_, "127.0.0.1");
    gst_rtsp_server_set_service(server_, "0");

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

  ~LoopbackRtspFixture() { Stop(); }

  [[nodiscard]] const std::string& Url() const { return url_; }

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

rch_camera_status_v1 Status(rch_engine_handle engine, const char* camera_id)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  if (rch_camera_get_status_by_id(engine, camera_id, &status) != RCH_RESULT_OK) {
    status.state = RCH_CAMERA_STATE_FAILED;
    status.last_result = RCH_RESULT_INVALID_ARGUMENT;
  }
  return status;
}

rch_engine_diagnostics_v1 Diagnostics(rch_engine_handle engine)
{
  rch_engine_diagnostics_v1 diagnostics{};
  diagnostics.struct_size = static_cast<std::uint32_t>(sizeof(diagnostics));
  diagnostics.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  if (rch_engine_get_diagnostics(engine, &diagnostics) != RCH_RESULT_OK) {
    diagnostics.configured_camera_count = 0;
    diagnostics.active_rtsp_session_total = 0;
    diagnostics.active_decoder_total = 0;
  }
  return diagnostics;
}

bool WaitForReceiving(rch_engine_handle engine,
                      const char* camera_id,
                      std::chrono::milliseconds timeout,
                      rch_camera_status_v1& out_status)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    out_status = Status(engine, camera_id);
    if (out_status.active_rtsp_session_count <= 1 && out_status.active_decoder_count <= 1
        && out_status.state == RCH_CAMERA_STATE_RECEIVING && out_status.has_latest_frame == 1) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  out_status = Status(engine, camera_id);
  return false;
}

bool Exercise2_4_8CameraScale(const std::vector<LoopbackRtspFixture>& fixtures)
{
  const std::vector<std::size_t> counts{2U, 4U, 8U};
  for (const auto count : counts) {
    if (count > fixtures.size()) {
      return Expect(false, "test fixture suite must include enough local RTSP servers");
    }

    rch_engine_handle engine = nullptr;
    if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine creation must succeed for scale test")) {
      return false;
    }

    bool passed = true;
    std::vector<std::string> camera_ids;
    for (std::size_t i = 0; i < count; ++i) {
      const std::string id = "cam-" + std::to_string(i);
      camera_ids.push_back(id);
      rch_camera_config_v1 config{
        static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
        RCH_CAMERA_CONFIG_VERSION,
        id.c_str(),
        fixtures[i].Url().c_str(),
        2000,
        0,
      };
      if (!Expect(rch_camera_add(engine, &config) == RCH_RESULT_OK,
                  "camera add must succeed for each configured logical camera")) {
        passed = false;
        break;
      }
      if (!Expect(rch_camera_start_by_id(engine, id.c_str()) == RCH_RESULT_OK,
                  "camera start must succeed for each member of the scale set")) {
        passed = false;
        break;
      }
    }

    if (passed) {
      std::uint32_t rtsp_sum = 0;
      std::uint32_t decoder_sum = 0;
      for (std::size_t i = 0; i < count; ++i) {
        rch_camera_status_v1 status{};
        if (!Expect(WaitForReceiving(engine, camera_ids[i].c_str(), std::chrono::seconds(8), status),
                    "each camera in the scale set must reach receiving within timeout")
            || !Expect(status.active_rtsp_session_count <= 1 && status.active_decoder_count <= 1,
                       "per-camera ownership must remain single-session and single-decoder")) {
          passed = false;
          break;
        }
        rtsp_sum += status.active_rtsp_session_count;
        decoder_sum += status.active_decoder_count;
      }

      if (passed) {
        const auto diagnostics = Diagnostics(engine);
        if (!Expect(diagnostics.configured_camera_count == count,
                    "aggregate diagnostics must reflect configured camera count")
            || !Expect(diagnostics.active_rtsp_session_total == rtsp_sum,
                       "aggregate diagnostics RTSP total must match per-camera ownership sum")
            || !Expect(diagnostics.active_decoder_total == decoder_sum,
                       "aggregate diagnostics decoder total must match per-camera ownership sum")
            || !Expect(diagnostics.active_rtsp_session_total <= diagnostics.configured_camera_count,
                       "aggregate RTSP ownership must never exceed configured camera count")
            || !Expect(diagnostics.active_decoder_total <= diagnostics.configured_camera_count,
                       "aggregate decoder ownership must never exceed configured camera count")) {
          passed = false;
        }
      }
    }

    if (passed) {
      for (std::size_t i = 0; i < count; ++i) {
        if (!Expect(rch_camera_stop_by_id(engine, camera_ids[i].c_str()) == RCH_RESULT_OK,
                    "removing/cycling camera must stop cleanly")) {
          passed = false;
          break;
        }
      }
    }

    for (const auto& camera_id : camera_ids) {
      rch_camera_remove(engine, camera_id.c_str());
    }

    if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
                "engine destroy must release the scale-set registry")) {
      return false;
    }

    if (!passed) {
      return false;
    }
  }

  return true;
}

bool ExerciseIndependentFailureIsolation()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK,
              "engine creation must succeed for independent-failure isolation")) {
    return false;
  }

  LoopbackRtspFixture good_fixture;
  if (!Expect(good_fixture.Start(30), "healthy fixture must start")) {
    rch_engine_destroy(engine);
    return false;
  }

  LoopbackRtspFixture bad_fixture;
  if (!Expect(bad_fixture.Start(30), "unhealthy fixture must start")) {
    good_fixture.Stop();
    rch_engine_destroy(engine);
    return false;
  }

  const std::string good_id = "good-camera";
  const std::string bad_id = "bad-camera";

  rch_camera_config_v1 good_config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    good_id.c_str(),
    good_fixture.Url().c_str(),
    2000,
    0,
  };
  rch_camera_config_v1 bad_config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    bad_id.c_str(),
    bad_fixture.Url().c_str(),
    2000,
    0,
  };

  bool passed = true;
  passed &= Expect(rch_camera_add(engine, &good_config) == RCH_RESULT_OK,
                  "healthy camera must add successfully");
  passed &= Expect(rch_camera_add(engine, &bad_config) == RCH_RESULT_OK,
                  "failed camera must add successfully");
  passed &= Expect(rch_camera_start_by_id(engine, good_id.c_str()) == RCH_RESULT_OK,
                  "healthy camera start must succeed");
  passed &= Expect(rch_camera_start_by_id(engine, bad_id.c_str()) == RCH_RESULT_OK,
                  "failing camera start must succeed");

  rch_camera_status_v1 good_status{};
  rch_camera_status_v1 bad_status{};
  passed &= Expect(WaitForReceiving(engine, good_id.c_str(), std::chrono::seconds(5), good_status),
                  "healthy camera must reach receiving before failure injection");
  passed &= Expect(WaitForReceiving(engine, bad_id.c_str(), std::chrono::seconds(5), bad_status),
                  "failed camera must reach receiving before failure injection");

  bad_fixture.Stop();
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(6);
  while (std::chrono::steady_clock::now() < deadline) {
    good_status = Status(engine, good_id.c_str());
    bad_status = Status(engine, bad_id.c_str());
    if (bad_status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
        || bad_status.state == RCH_CAMERA_STATE_FAILED) {
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }

  passed &= Expect(bad_status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
                    || bad_status.state == RCH_CAMERA_STATE_FAILED,
                  "failing camera must enter retry/fail state when fixture stops");
  passed &= Expect(good_status.state == RCH_CAMERA_STATE_RECEIVING,
                  "healthy camera must remain independent while the other camera fails");

  rch_camera_remove(engine, bad_id.c_str());
  rch_camera_remove(engine, good_id.c_str());
  passed &= Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
                  "destroy must succeed after mixed camera states");

  good_fixture.Stop();
  return passed;
}

}  // namespace

int main()
{
  if (rch_get_abi_version() == 0) {
    return 1;
  }

  std::vector<LoopbackRtspFixture> fixtures(8);
  for (auto& fixture : fixtures) {
    if (!Expect(fixture.Start(30), "RTSP fixture must start for multi-camera validation")) {
      return 1;
    }
  }

  bool passed = Exercise2_4_8CameraScale(fixtures);
  passed &= ExerciseIndependentFailureIsolation();
  return passed ? 0 : 1;
}
