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

rch_camera_status_v1 Status(rch_engine_handle engine)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  if (rch_camera_get_status(engine, &status) != RCH_RESULT_OK) {
    status.state = RCH_CAMERA_STATE_FAILED;
  }
  return status;
}

bool ExerciseCamera(rch_engine_handle engine, const std::string& url)
{
  const rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    "loopback-profile2",
    url.c_str(),
    5000,
    0,
  };
  if (!Expect(rch_camera_configure(engine, &config) == RCH_RESULT_OK,
              "loopback source configuration must succeed")) {
    return false;
  }

  for (int cycle = 0; cycle < 3; ++cycle) {
    const auto previous_count = Status(engine).decoded_frame_count;
    if (!Expect(rch_camera_start(engine) == RCH_RESULT_OK, "production RTSP pipeline must start")
        || !Expect(rch_camera_start(engine) == RCH_RESULT_ALREADY_STARTED,
                   "repeated start must not duplicate the live pipeline")) {
      return false;
    }

    auto status = Status(engine);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(8);
    while (status.decoded_frame_count < previous_count + 12
           && status.state != RCH_CAMERA_STATE_FAILED
           && std::chrono::steady_clock::now() < deadline) {
      if (!Expect(status.active_rtsp_session_count <= 1 && status.active_decoder_count <= 1,
                  "ownership counts must never exceed one")) {
        return false;
      }
      std::this_thread::sleep_for(std::chrono::milliseconds(20));
      status = Status(engine);
    }

    if (!Expect(status.state == RCH_CAMERA_STATE_RECEIVING, "production pipeline must decode RTSP frames")
        || !Expect(status.decoded_frame_count >= previous_count + 12,
                   "frames must advance without a downstream frame consumer")
        || !Expect(status.active_rtsp_session_count == 1 && status.active_decoder_count == 1,
                   "one live source must own exactly one session and decoder")
        || !Expect(status.has_latest_frame == 1 && status.latest_frame_width == 128
                     && status.latest_frame_height == 72,
                   "decoded frame metadata must reach the native latest-frame slot")
        || !Expect(rch_camera_start(engine) == RCH_RESULT_ALREADY_STARTED,
                   "repeated start while receiving must be rejected")
        || !Expect(rch_camera_stop(engine) == RCH_RESULT_OK, "live pipeline must stop cleanly")) {
      std::cerr << "state=" << status.state << " result=" << status.last_result
                << " frames=" << status.decoded_frame_count << '\n';
      return false;
    }

    status = Status(engine);
    if (!Expect(status.state == RCH_CAMERA_STATE_STOPPED
                  && status.active_rtsp_session_count == 0 && status.active_decoder_count == 0
                  && status.has_latest_frame == 0,
                "stop must release pipeline ownership and its retained frame")) {
      return false;
    }
  }
  return true;
}

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine must initialise GStreamer")) {
    return 1;
  }

  auto* context = g_main_context_new();
  auto* loop = g_main_loop_new(context, FALSE);
  auto* server = gst_rtsp_server_new();
  gst_rtsp_server_set_address(server, "127.0.0.1");
  gst_rtsp_server_set_service(server, "0");
  auto* mounts = gst_rtsp_server_get_mount_points(server);
  auto* factory = gst_rtsp_media_factory_new();
  gst_rtsp_media_factory_set_launch(factory,
    "( videotestsrc is-live=true ! video/x-raw,format=I420,width=128,height=72,framerate=30/1 "
    "! x264enc tune=zerolatency speed-preset=ultrafast key-int-max=1 "
    "! rtph264pay name=pay0 pt=96 config-interval=1 )");
  gst_rtsp_media_factory_set_protocols(factory, GST_RTSP_LOWER_TRANS_UDP);
  gst_rtsp_mount_points_add_factory(mounts, "/profile2/media.smp", factory);
  g_object_unref(mounts);

  const auto source_id = gst_rtsp_server_attach(server, context);
  const auto port = gst_rtsp_server_get_bound_port(server);
  if (!Expect(source_id != 0 && port > 0, "loopback RTSP fixture must bind an ephemeral port")) {
    rch_engine_destroy(engine);
    g_object_unref(server);
    g_main_loop_unref(loop);
    g_main_context_unref(context);
    return 1;
  }

  std::thread server_thread([loop] { g_main_loop_run(loop); });
  const auto url = "rtsp://127.0.0.1:" + std::to_string(port) + "/profile2/media.smp";
  const bool succeeded = ExerciseCamera(engine, url);
  rch_engine_destroy(engine);
  g_main_loop_quit(loop);
  server_thread.join();
  if (auto* source = g_main_context_find_source_by_id(context, source_id); source != nullptr) {
    g_source_destroy(source);
  }
  g_object_unref(server);
  g_main_loop_unref(loop);
  g_main_context_unref(context);
  return succeeded ? 0 : 1;
}
