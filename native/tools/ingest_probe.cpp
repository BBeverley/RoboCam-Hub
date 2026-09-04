#include "robocamhub_native.h"

#include <chrono>
#include <cerrno>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <limits>
#include <thread>

namespace {

const char* StateName(rch_camera_state state)
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

void PrintStatus(const rch_camera_status_v1& status)
{
  std::cout << "state=" << StateName(status.state)
            << " configured_transport=udp"
            << " sessions=" << status.active_rtsp_session_count
            << " decoders=" << status.active_decoder_count
            << " frames=" << status.decoded_frame_count
            << " latest=" << status.latest_frame_sequence
            << " size=" << status.latest_frame_width << 'x' << status.latest_frame_height;
  if (status.has_latest_frame != 0) {
    std::cout << " age_ms=" << status.latest_frame_age_ms;
  } else {
    std::cout << " age_ms=n/a";
  }
  std::cout << " last_result=" << status.last_result << std::endl;
}

}  // namespace

int main(int argc, char** argv)
{
  if (argc < 3 || argc > 4) {
    std::cerr << "Usage: robocamhub_ingest_probe <camera-id> <rtsp-url> [duration-seconds]\n";
    return 2;
  }

  std::uint32_t duration_seconds = 60;
  if (argc == 4 && !ParseDuration(argv[3], duration_seconds)) {
    std::cerr << "duration-seconds must be a positive 32-bit integer\n";
    return 2;
  }

  rch_engine_handle engine = nullptr;
  auto result = rch_engine_create(&engine);
  if (result != RCH_RESULT_OK) {
    std::cerr << "Engine creation failed: " << result << '\n';
    return 1;
  }

  const rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    argv[1],
    argv[2],
    10000,
    0,
  };

  result = rch_camera_configure(engine, &config);
  if (result == RCH_RESULT_OK) {
    result = rch_camera_start(engine);
  }

  bool received_frame = false;
  for (std::uint32_t second = 0; result == RCH_RESULT_OK && second < duration_seconds; ++second) {
    rch_camera_status_v1 status{};
    status.struct_size = static_cast<std::uint32_t>(sizeof(rch_camera_status_v1));
    status.struct_version = RCH_CAMERA_STATUS_VERSION;
    result = rch_camera_get_status(engine, &status);
    if (result != RCH_RESULT_OK) {
      break;
    }

    PrintStatus(status);
    received_frame = received_frame || status.has_latest_frame != 0;
    if (status.state == RCH_CAMERA_STATE_FAILED) {
      result = status.last_result;
      break;
    }
    std::this_thread::sleep_for(std::chrono::seconds(1));
  }

  const auto stop_result = rch_camera_stop(engine);
  if (stop_result == RCH_RESULT_OK) {
    rch_camera_status_v1 stopped_status{};
    stopped_status.struct_size = static_cast<std::uint32_t>(sizeof(rch_camera_status_v1));
    stopped_status.struct_version = RCH_CAMERA_STATUS_VERSION;
    if (rch_camera_get_status(engine, &stopped_status) == RCH_RESULT_OK) {
      PrintStatus(stopped_status);
    }
  }
  const auto destroy_result = rch_engine_destroy(engine);
  if (result != RCH_RESULT_OK || stop_result != RCH_RESULT_OK || destroy_result != RCH_RESULT_OK) {
    std::cerr << "Probe failed: camera=" << result
              << " stop=" << stop_result
              << " destroy=" << destroy_result << '\n';
    return 1;
  }

  if (!received_frame) {
    std::cerr << "Probe completed without receiving a decoded frame\n";
    return 1;
  }

  return 0;
}
