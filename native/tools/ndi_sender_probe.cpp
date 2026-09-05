#include "robocamhub_native.h"

#include <array>
#include <chrono>
#include <cerrno>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <string>
#include <thread>

namespace {

constexpr std::size_t kCameraCount = 4U;

const char* CameraStateName(rch_camera_state state)
{
  switch (state) {
    case RCH_CAMERA_STATE_STOPPED:
      return "Stopped";
    case RCH_CAMERA_STATE_STARTING:
      return "Starting";
    case RCH_CAMERA_STATE_RECEIVING:
      return "Receiving";
    case RCH_CAMERA_STATE_FAILED:
      return "Failed";
    case RCH_CAMERA_STATE_STOPPING:
      return "Stopping";
    case RCH_CAMERA_STATE_WAITING_TO_RETRY:
      return "WaitingToRetry";
    default:
      return "Unknown";
  }
}

const char* SenderStateName(rch_ndi_sender_state state)
{
  switch (state) {
    case RCH_NDI_SENDER_STATE_STOPPED:
      return "Stopped";
    case RCH_NDI_SENDER_STATE_STARTING:
      return "Starting";
    case RCH_NDI_SENDER_STATE_RUNNING:
      return "Running";
    case RCH_NDI_SENDER_STATE_WAITING_FOR_VIEW_FRAME:
      return "WaitingForViewFrame";
    case RCH_NDI_SENDER_STATE_FAILED:
      return "Failed";
    default:
      return "Unknown";
  }
}

bool ParseDuration(const char* value, std::uint32_t& duration_seconds)
{
  char* end = nullptr;
  errno = 0;
  const auto parsed = std::strtoul(value, &end, 10);
  if (value[0] == '-' || end == value || *end != '\0' || errno == ERANGE || parsed == 0
      || parsed > std::numeric_limits<std::uint32_t>::max()) {
    return false;
  }
  duration_seconds = static_cast<std::uint32_t>(parsed);
  return true;
}

rch_engine_diagnostics_v1 QueryEngine(rch_engine_handle engine, bool& ok)
{
  rch_engine_diagnostics_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  ok = rch_engine_get_diagnostics(engine, &status) == RCH_RESULT_OK;
  return status;
}

rch_camera_status_v1 QueryCamera(rch_engine_handle engine, const char* camera_id, bool& ok)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  ok = rch_camera_get_status_by_id(engine, camera_id, &status) == RCH_RESULT_OK;
  return status;
}

rch_view_status_v1 QueryView(rch_view_handle view, bool& ok)
{
  rch_view_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_VIEW_STATUS_VERSION;
  ok = rch_view_get_status(view, &status) == RCH_RESULT_OK;
  return status;
}

rch_ndi_sender_status_v1 QuerySender(rch_ndi_sender_handle sender, bool& ok)
{
  rch_ndi_sender_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(status));
  status.struct_version = RCH_NDI_SENDER_STATUS_VERSION;
  ok = rch_ndi_sender_get_status(sender, &status) == RCH_RESULT_OK;
  return status;
}

}  // namespace

int main(int argc, char** argv)
{
  if (argc != 6) {
    std::cerr << "Usage: robocamhub_ndi_sender_probe <duration-seconds> "
                 "<rtsp-url-1> <rtsp-url-2> <rtsp-url-3> <rtsp-url-4>\n";
    return 2;
  }

  std::uint32_t duration_seconds = 0U;
  if (!ParseDuration(argv[1], duration_seconds)) {
    std::cerr << "duration-seconds must be a positive 32-bit integer\n";
    return 2;
  }

  const std::array<std::string, kCameraCount> camera_ids{
    "Gate4A-Camera-1", "Gate4A-Camera-2", "Gate4A-Camera-3", "Gate4A-Camera-4"};
  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  rch_ndi_sender_handle sender = nullptr;
  auto cleanup = [&]() {
    if (sender != nullptr) {
      static_cast<void>(rch_ndi_sender_stop(sender));
      static_cast<void>(rch_ndi_sender_destroy(sender));
      sender = nullptr;
    }
    if (view != nullptr) {
      static_cast<void>(rch_view_destroy(view));
      view = nullptr;
    }
    if (engine != nullptr) {
      static_cast<void>(rch_engine_destroy(engine));
      engine = nullptr;
    }
  };

  auto result = rch_engine_create(&engine);
  if (result != RCH_RESULT_OK) {
    std::cerr << "Engine creation failed: " << result << '\n';
    return 1;
  }

  for (std::size_t index = 0; index < kCameraCount; ++index) {
    rch_camera_config_v1 config{};
    config.struct_size = static_cast<std::uint32_t>(sizeof(config));
    config.struct_version = RCH_CAMERA_CONFIG_VERSION;
    config.camera_id_utf8 = camera_ids[index].c_str();
    config.rtsp_url_utf8 = argv[index + 2U];
    config.connect_timeout_ms = 10000U;
    result = rch_camera_add(engine, &config);
    if (result == RCH_RESULT_OK) {
      result = rch_camera_start_by_id(engine, camera_ids[index].c_str());
    }
    if (result != RCH_RESULT_OK) {
      std::cerr << "Camera " << index + 1U << " setup failed: " << result << '\n';
      cleanup();
      return 1;
    }
  }

  const auto receiving_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(30);
  bool all_receiving = false;
  while (std::chrono::steady_clock::now() < receiving_deadline) {
    all_receiving = true;
    for (const auto& camera_id : camera_ids) {
      bool ok = false;
      const auto status = QueryCamera(engine, camera_id.c_str(), ok);
      all_receiving = all_receiving && ok
        && status.state == RCH_CAMERA_STATE_RECEIVING
        && status.has_latest_frame != 0U
        && status.active_rtsp_session_count == 1U
        && status.active_decoder_count == 1U;
    }
    if (all_receiving) {
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
  }
  if (!all_receiving) {
    std::cerr << "All four cameras did not reach Receiving with exactly one session/decoder each\n";
    cleanup();
    return 1;
  }

  result = rch_view_create(engine, "Gate4A-Validation-View", &view);
  for (std::size_t index = 0; result == RCH_RESULT_OK && index < kCameraCount; ++index) {
    result = rch_view_bind_camera_source(
      view,
      static_cast<std::uint32_t>(index),
      camera_ids[index].c_str());
  }
  if (result != RCH_RESULT_OK) {
    std::cerr << "View setup failed: " << result << '\n';
    cleanup();
    return 1;
  }

  std::this_thread::sleep_for(std::chrono::seconds(3));
  bool baseline_view_ok = false;
  const auto baseline_view = QueryView(view, baseline_view_ok);
  if (!baseline_view_ok || baseline_view.latest_composed_frame_sequence == 0U) {
    std::cerr << "View did not produce a baseline composed frame\n";
    cleanup();
    return 1;
  }
  std::cout << "baseline_view_fps=" << static_cast<double>(baseline_view.render_fps_milli) / 1000.0
            << " baseline_view_sequence=" << baseline_view.latest_composed_frame_sequence
            << " baseline_view_age_ms=" << baseline_view.latest_composed_frame_age_ms
            << std::endl;

  result = rch_ndi_sender_create(view, "ROBOCAM - Gate4A", &sender);
  if (result != RCH_RESULT_OK) {
    std::cerr << "Sender setup failed: " << result << '\n';
    cleanup();
    return 1;
  }

  bool sender_status_ok = false;
  auto sender_status = QuerySender(sender, sender_status_ok);
  if (!sender_status_ok || sender_status.reserved_v2 == 0U) {
    std::cerr << "Official NDI SDK backend is not active; refusing to present deterministic output as live NDI\n";
    cleanup();
    return 1;
  }
  result = rch_ndi_sender_start(sender);
  if (result != RCH_RESULT_OK) {
    std::cerr << "Sender start failed: " << result << '\n';
    cleanup();
    return 1;
  }

  bool invariant_failure = false;
  std::uint64_t previous_sent_sequence = 0U;
  std::cout << "backend=official sender_name=ROBOCAM - Gate4A pixel_format=RGBA "
               "size=1920x1080 target_fps=60 frame_copy=none\n";
  for (std::uint32_t elapsed = 0; elapsed < duration_seconds; ++elapsed) {
    bool engine_ok = false;
    bool view_ok = false;
    sender_status_ok = false;
    const auto engine_status = QueryEngine(engine, engine_ok);
    const auto view_status = QueryView(view, view_ok);
    sender_status = QuerySender(sender, sender_status_ok);
    if (!engine_ok || !view_ok || !sender_status_ok) {
      std::cerr << "Status query failed at elapsed=" << elapsed << '\n';
      invariant_failure = true;
      break;
    }

    invariant_failure = invariant_failure
      || engine_status.configured_camera_count != kCameraCount
      || engine_status.active_rtsp_session_total > kCameraCount
      || engine_status.active_decoder_total > kCameraCount
      || view_status.output_consumer_count != 1U
      || sender_status.state == RCH_NDI_SENDER_STATE_FAILED;
    if (sender_status.latest_sent_sequence > previous_sent_sequence) {
      previous_sent_sequence = sender_status.latest_sent_sequence;
    }

    std::cout << "elapsed_s=" << elapsed
              << " sender_state=" << SenderStateName(sender_status.state)
              << " sender_frames=" << sender_status.sent_frame_count
              << " sender_sequence=" << sender_status.latest_sent_sequence
              << " send_fps=" << static_cast<double>(sender_status.send_fps_milli) / 1000.0
              << " sent_age_ms=" << sender_status.latest_sent_frame_age_ms
              << " send_avg_us=" << sender_status.average_send_duration_us
              << " send_p95_us=" << sender_status.p95_send_duration_us
              << " skipped=" << sender_status.dropped_or_skipped_frame_count
              << " receivers=";
    if (sender_status.receiver_count_known != 0U) {
      std::cout << sender_status.receiver_count;
    } else {
      std::cout << "unknown";
    }
    std::cout << " view_sequence=" << view_status.latest_composed_frame_sequence
              << " view_fps=" << static_cast<double>(view_status.render_fps_milli) / 1000.0
              << " view_age_ms=" << view_status.latest_composed_frame_age_ms
              << " view_live=" << view_status.live_source_count
              << " view_frozen=" << view_status.frozen_source_count
              << " cameras=" << engine_status.configured_camera_count
              << " sessions=" << engine_status.active_rtsp_session_total
              << " decoders=" << engine_status.active_decoder_total;
    for (std::size_t index = 0; index < kCameraCount; ++index) {
      bool camera_ok = false;
      const auto camera_status = QueryCamera(engine, camera_ids[index].c_str(), camera_ok);
      std::cout << " camera" << index + 1U << '='
                << (camera_ok ? CameraStateName(camera_status.state) : "QueryFailed")
                << ':' << camera_status.latest_frame_sequence;
    }
    std::cout << std::endl;
    std::this_thread::sleep_for(std::chrono::seconds(1));
  }

  const auto sender_stop_result = rch_ndi_sender_stop(sender);
  sender_status = QuerySender(sender, sender_status_ok);
  for (const auto& camera_id : camera_ids) {
    const auto camera_stop_result = rch_camera_stop_by_id(engine, camera_id.c_str());
    invariant_failure = invariant_failure || camera_stop_result != RCH_RESULT_OK;
  }
  bool stopped_engine_ok = false;
  const auto stopped_engine = QueryEngine(engine, stopped_engine_ok);
  std::cout << "final_sender_state="
            << (sender_status_ok ? SenderStateName(sender_status.state) : "QueryFailed")
            << " final_sessions=" << (stopped_engine_ok ? stopped_engine.active_rtsp_session_total : 999U)
            << " final_decoders=" << (stopped_engine_ok ? stopped_engine.active_decoder_total : 999U)
            << std::endl;

  invariant_failure = invariant_failure
    || sender_stop_result != RCH_RESULT_OK
    || !sender_status_ok
    || sender_status.state != RCH_NDI_SENDER_STATE_STOPPED
    || previous_sent_sequence == 0U
    || !stopped_engine_ok
    || stopped_engine.active_rtsp_session_total != 0U
    || stopped_engine.active_decoder_total != 0U;
  cleanup();
  return invariant_failure ? 1 : 0;
}
