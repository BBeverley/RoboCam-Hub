#include "robocamhub_native.h"

#include <chrono>
#include <cstddef>
#include <cstring>
#include <cstdint>
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

constexpr std::uint32_t StatusV1Size()
{
  return static_cast<std::uint32_t>(offsetof(rch_camera_status_v1, reconnect_attempt_count));
}

rch_camera_status_v1 QueryStatus(rch_engine_handle engine, bool& succeeded)
{
  rch_camera_status_v1 status{};
  status.struct_size = static_cast<std::uint32_t>(sizeof(rch_camera_status_v1));
  status.struct_version = RCH_CAMERA_STATUS_VERSION;
  succeeded = rch_camera_get_status(engine, &status) == RCH_RESULT_OK;
  return status;
}

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine creation must initialise GStreamer")) {
    return 1;
  }

  if (!Expect(rch_camera_start(engine) == RCH_RESULT_NOT_CONFIGURED,
              "an unconfigured camera must not start")) {
    return 1;
  }

  rch_camera_config_v1 invalid_config{};
  if (!Expect(rch_camera_configure(engine, &invalid_config) == RCH_RESULT_INVALID_ARGUMENT,
              "configuration must require a versioned structure")) {
    return 1;
  }

  const rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    "gate-1a-test-camera",
    "rtsp://127.0.0.1:1/profile2/media.smp",
    500,
    0,
  };
  auto bad_config = config;
  bad_config.camera_id_utf8 = "\xff";
  if (!Expect(rch_camera_configure(engine, &bad_config) == RCH_RESULT_INVALID_ARGUMENT,
              "camera ID must be valid UTF-8")) {
    return 1;
  }
  bad_config = config;
  bad_config.reserved = 1;
  if (!Expect(rch_camera_configure(engine, &bad_config) == RCH_RESULT_INVALID_ARGUMENT,
              "reserved configuration fields must be zero")) {
    return 1;
  }
  bad_config = config;
  bad_config.connect_timeout_ms = 99;
  if (!Expect(rch_camera_configure(engine, &bad_config) == RCH_RESULT_INVALID_ARGUMENT,
              "first-frame timeout must be bounded")) {
    return 1;
  }
  rch_camera_status_v1 bad_status{};
  if (!Expect(rch_camera_get_status(engine, &bad_status) == RCH_RESULT_INVALID_ARGUMENT,
              "status must require a versioned caller-provided structure")
      || !Expect(rch_camera_start(nullptr) == RCH_RESULT_INVALID_HANDLE,
                 "camera control must reject null handles")) {
    return 1;
  }
  if (!Expect(rch_camera_configure(engine, &config) == RCH_RESULT_OK,
              "valid RTSP configuration must succeed")) {
    return 1;
  }

  bool queried = false;
  auto status = QueryStatus(engine, queried);
  if (!Expect(queried && status.state == RCH_CAMERA_STATE_STOPPED,
              "configured camera must remain stopped until started")) {
    return 1;
  }

  if (!Expect(rch_camera_start(engine) == RCH_RESULT_OK, "camera start must construct the pipeline")) {
    return 1;
  }
  status = QueryStatus(engine, queried);
  if (!Expect(queried, "active camera status must be queryable")
      || !Expect(status.active_rtsp_session_count <= 1, "RTSP session count must never exceed one")
      || !Expect(status.active_decoder_count <= 1, "decoder count must never exceed one")) {
    return 1;
  }

  const auto failure_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  do {
    status = QueryStatus(engine, queried);
    if (!queried) {
      return 1;
    }
    if (status.last_result == RCH_RESULT_CONNECTION_TIMEOUT
        || status.last_result == RCH_RESULT_RTSP_FAILURE) {
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  } while (std::chrono::steady_clock::now() < failure_deadline);

  if (!Expect(status.state == RCH_CAMERA_STATE_WAITING_TO_RETRY
                || status.state == RCH_CAMERA_STATE_STARTING
                || status.state == RCH_CAMERA_STATE_FAILED,
              "unreachable RTSP source must enter retry/failure lifecycle")
      || !Expect(status.last_result == RCH_RESULT_CONNECTION_TIMEOUT
                   || status.last_result == RCH_RESULT_RTSP_FAILURE,
                 "failure must preserve an RTSP-specific result category")
      || !Expect(status.active_rtsp_session_count == 0,
                 "failed connection must release active RTSP ownership")
      || !Expect(status.active_decoder_count == 0,
                 "failed connection must release active decoder ownership")
      || !Expect(status.reconnect_attempt_count >= 0,
                 "status must expose reconnect attempt diagnostics")) {
    return 1;
  }

  alignas(rch_camera_status_v1) std::uint8_t v1_buffer[StatusV1Size() + 16];
  std::memset(v1_buffer, 0xA5, sizeof(v1_buffer));
  auto* legacy_status = reinterpret_cast<rch_camera_status_v1*>(v1_buffer);
  legacy_status->struct_size = StatusV1Size();
  legacy_status->struct_version = RCH_CAMERA_STATUS_VERSION_V1;
  if (!Expect(rch_camera_get_status(engine, legacy_status) == RCH_RESULT_OK,
              "v1 status callers must remain supported")
      || !Expect(legacy_status->struct_size == StatusV1Size()
                   && legacy_status->struct_version == RCH_CAMERA_STATUS_VERSION_V1,
                 "v1 status call must preserve reported v1 shape")) {
    return 1;
  }

  bool canary_intact = true;
  for (std::size_t offset = StatusV1Size(); offset < sizeof(v1_buffer); ++offset) {
    if (v1_buffer[offset] != static_cast<std::uint8_t>(0xA5)) {
      canary_intact = false;
      break;
    }
  }
  if (!Expect(canary_intact,
              "status query must not write beyond a valid v1-sized caller buffer")) {
    return 1;
  }

  if (!Expect(rch_camera_stop(engine) == RCH_RESULT_OK, "failed camera must stop cleanly")) {
    return 1;
  }

  for (int iteration = 0; iteration < 25; ++iteration) {
    if (!Expect(rch_camera_start(engine) == RCH_RESULT_OK, "repeated lifecycle start must succeed")
        || !Expect(rch_camera_stop(engine) == RCH_RESULT_OK, "repeated lifecycle stop must succeed")) {
      return 1;
    }

    status = QueryStatus(engine, queried);
    if (!Expect(queried && status.state == RCH_CAMERA_STATE_STOPPED,
                "repeated stop must return to stopped")
        || !Expect(status.active_rtsp_session_count == 0,
                   "repeated stop must release RTSP ownership")
        || !Expect(status.active_decoder_count == 0,
                   "repeated stop must release decoder ownership")) {
      return 1;
    }
  }

  if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
              "engine destruction must release the camera component")) {
    return 1;
  }

  return 0;
}
