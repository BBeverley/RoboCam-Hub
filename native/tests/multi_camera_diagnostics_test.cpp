#include "robocamhub_native.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
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

std::vector<std::string> EnumerateCameraIds(rch_engine_handle engine, bool& ok)
{
  std::vector<std::string> ids;

  std::uint32_t required_size = 0;
  std::uint32_t camera_count = 0;
  const auto count_result = rch_camera_enumerate_ids(engine, nullptr, 0, &required_size, &camera_count);
  if (count_result != RCH_RESULT_OK) {
    ok = false;
    return ids;
  }

  std::vector<char> buffer(required_size == 0U ? 1U : required_size, '\0');
  const auto list_result = rch_camera_enumerate_ids(
    engine,
    required_size == 0U ? nullptr : buffer.data(),
    required_size,
    &required_size,
    &camera_count);
  if (list_result != RCH_RESULT_OK) {
    ok = false;
    return ids;
  }

  std::size_t offset = 0;
  for (std::uint32_t i = 0; i < camera_count; ++i) {
    if (offset >= buffer.size()) {
      ok = false;
      return {};
    }

    const auto* current = buffer.data() + offset;
    const auto length = std::strlen(current);
    ids.emplace_back(current, length);
    offset += length + 1U;
  }

  ok = true;
  return ids;
}

uint32_t SumStateCountFromStatuses(const std::vector<rch_camera_status_v1>& statuses,
                                   rch_camera_state state)
{
  uint32_t count = 0;
  for (const auto& status : statuses) {
    if (status.state == state) {
      count += 1U;
    }
  }
  return count;
}

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine creation must succeed")) {
    return 1;
  }

  bool diagnostics_ok = false;
  const auto empty_diagnostics = QueryDiagnostics(engine, diagnostics_ok);
  if (!Expect(diagnostics_ok, "diagnostics query must succeed on an empty engine")
      || !Expect(empty_diagnostics.configured_camera_count == 0,
                 "empty engine must report zero configured cameras")
      || !Expect(empty_diagnostics.active_rtsp_session_total == 0,
                 "empty engine must report zero RTSP session ownership")
      || !Expect(empty_diagnostics.active_decoder_total == 0,
                 "empty engine must report zero decoder ownership")) {
    return 1;
  }

  std::uint32_t required_size = 999U;
  std::uint32_t camera_count = 999U;
  if (!Expect(rch_camera_enumerate_ids(engine, nullptr, 0, &required_size, &camera_count) == RCH_RESULT_OK,
              "count-only enumeration must succeed for an empty registry")
      || !Expect(required_size == 0U && camera_count == 0U,
                 "empty enumeration must report zero IDs and zero bytes")) {
    return 1;
  }

  const std::vector<std::string> add_order{"cam-b", "cam-a", "cam-c"};
  for (const auto& camera_id : add_order) {
    rch_camera_config_v1 config{
      static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
      RCH_CAMERA_CONFIG_VERSION,
      camera_id.c_str(),
      "rtsp://127.0.0.1:1/profile2/media.smp",
      250,
      0,
    };
    if (!Expect(rch_camera_add(engine, &config) == RCH_RESULT_OK,
                "camera add must succeed for Gate 2B diagnostics cases")) {
      return 1;
    }
  }

  required_size = 0;
  camera_count = 0;
  if (!Expect(rch_camera_enumerate_ids(engine, nullptr, 0, &required_size, &camera_count) == RCH_RESULT_OK,
              "count-only enumeration must succeed for configured cameras")
      || !Expect(camera_count == 3U, "enumeration must expose all configured camera IDs")
      || !Expect(required_size > 0U, "enumeration must report byte size for configured IDs")) {
    return 1;
  }

  std::vector<char> undersized(required_size, static_cast<char>(0x5A));
  const auto tiny_result = rch_camera_enumerate_ids(
    engine,
    undersized.data(),
    required_size - 1U,
    &required_size,
    &camera_count);
  if (!Expect(tiny_result == RCH_RESULT_BUFFER_TOO_SMALL,
              "undersized caller buffer must report buffer-too-small")
      || !Expect(undersized.front() == static_cast<char>(0x5A),
                 "buffer-too-small enumeration must not mutate caller memory")) {
    return 1;
  }

  std::vector<char> id_buffer(required_size + 8U, static_cast<char>(0xA5));
  std::uint32_t exact_size = required_size;
  if (!Expect(rch_camera_enumerate_ids(engine,
                                       id_buffer.data(),
                                       exact_size,
                                       &exact_size,
                                       &camera_count) == RCH_RESULT_OK,
              "enumeration with exact sized buffer must succeed")
      || !Expect(camera_count == 3U, "camera count must remain stable across enumeration calls")) {
    return 1;
  }

  std::vector<std::string> enumerated_ids;
  std::size_t offset = 0;
  for (std::uint32_t i = 0; i < camera_count; ++i) {
    const std::string id(id_buffer.data() + offset);
    enumerated_ids.push_back(id);
    offset += id.size() + 1U;
  }

  const std::vector<std::string> expected_sorted_ids{"cam-a", "cam-b", "cam-c"};
  if (!Expect(enumerated_ids == expected_sorted_ids,
              "camera enumeration must be deterministic and lexical")) {
    return 1;
  }

  bool canary_ok = true;
  for (std::size_t i = exact_size; i < id_buffer.size(); ++i) {
    if (id_buffer[i] != static_cast<char>(0xA5)) {
      canary_ok = false;
      break;
    }
  }
  if (!Expect(canary_ok,
              "enumeration must not write beyond the required caller buffer size")) {
    return 1;
  }

  const auto duplicate_url = std::string("rtsp://127.0.0.1:2/profile2/media.smp");
  rch_camera_config_v1 overwrite_config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    "cam-a",
    duplicate_url.c_str(),
    500,
    0,
  };
  if (!Expect(rch_camera_add(engine, &overwrite_config) == RCH_RESULT_OK,
              "duplicate camera add must deterministically overwrite existing config")) {
    return 1;
  }

  bool list_ok = false;
  const auto ids_after_duplicate = EnumerateCameraIds(engine, list_ok);
  if (!Expect(list_ok, "enumeration helper must succeed after duplicate add")
      || !Expect(ids_after_duplicate == expected_sorted_ids,
                 "duplicate add must not duplicate camera IDs in the registry")) {
    return 1;
  }

  if (!Expect(rch_camera_remove(engine, "cam-b") == RCH_RESULT_OK,
              "camera removal must succeed for stale-ID verification")
      || !Expect(rch_camera_start_by_id(engine, "cam-b") == RCH_RESULT_NOT_CONFIGURED,
                 "removed camera start must fail as not configured")
      || !Expect(rch_camera_stop_by_id(engine, "cam-b") == RCH_RESULT_NOT_CONFIGURED,
                 "removed camera stop must fail as not configured")) {
    return 1;
  }

  bool status_ok = false;
  const auto removed_status = QueryCameraStatus(engine, "cam-b", status_ok);
  (void)removed_status;
  if (!Expect(!status_ok, "removed camera status query must fail deterministically")) {
    return 1;
  }

  rch_camera_config_v1 readd_config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    "cam-b",
    "rtsp://127.0.0.1:1/profile2/media.smp",
    250,
    0,
  };
  if (!Expect(rch_camera_add(engine, &readd_config) == RCH_RESULT_OK,
              "re-adding removed camera ID must succeed deterministically")) {
    return 1;
  }

  const auto ids_after_readd = EnumerateCameraIds(engine, list_ok);
  if (!Expect(list_ok, "enumeration helper must succeed after re-add")
      || !Expect(ids_after_readd == expected_sorted_ids,
                 "re-added camera must return to deterministic lexical order")) {
    return 1;
  }

  if (!Expect(rch_camera_start_by_id(engine, "cam-a") == RCH_RESULT_OK,
              "camera start must succeed for aggregate diagnostics checks")
      || !Expect(rch_camera_start_by_id(engine, "cam-c") == RCH_RESULT_OK,
                 "starting multiple cameras must succeed independently")) {
    return 1;
  }

  std::this_thread::sleep_for(std::chrono::milliseconds(250));

  const std::vector<std::string> active_ids{"cam-a", "cam-b", "cam-c"};
  std::vector<rch_camera_status_v1> statuses;
  statuses.reserve(active_ids.size());
  for (const auto& camera_id : active_ids) {
    auto status = QueryCameraStatus(engine, camera_id.c_str(), status_ok);
    if (!Expect(status_ok, "status query must succeed for configured camera IDs")) {
      return 1;
    }
    statuses.push_back(status);
  }

  const auto diagnostics = QueryDiagnostics(engine, diagnostics_ok);
  if (!Expect(diagnostics_ok, "aggregate diagnostics query must succeed for active cameras")
      || !Expect(diagnostics.configured_camera_count == statuses.size(),
                 "aggregate configured count must match enumerated configured cameras")) {
    return 1;
  }

  uint32_t rtsp_sum = 0;
  uint32_t decoder_sum = 0;
  for (const auto& status : statuses) {
    rtsp_sum += status.active_rtsp_session_count;
    decoder_sum += status.active_decoder_count;
  }

  if (!Expect(diagnostics.active_rtsp_session_total == rtsp_sum,
              "aggregate RTSP total must match the per-camera status sum")
      || !Expect(diagnostics.active_decoder_total == decoder_sum,
                 "aggregate decoder total must match the per-camera status sum")
      || !Expect(diagnostics.active_rtsp_session_total <= diagnostics.configured_camera_count,
                 "aggregate RTSP ownership must not exceed configured camera count")
      || !Expect(diagnostics.active_decoder_total <= diagnostics.configured_camera_count,
                 "aggregate decoder ownership must not exceed configured camera count")) {
    return 1;
  }

  const uint32_t starting_sum = SumStateCountFromStatuses(statuses, RCH_CAMERA_STATE_STARTING);
  const uint32_t receiving_sum = SumStateCountFromStatuses(statuses, RCH_CAMERA_STATE_RECEIVING);
  const uint32_t retry_sum = SumStateCountFromStatuses(statuses, RCH_CAMERA_STATE_WAITING_TO_RETRY);
  const uint32_t failed_sum = SumStateCountFromStatuses(statuses, RCH_CAMERA_STATE_FAILED);
  const uint32_t stopped_or_stopping_sum =
    SumStateCountFromStatuses(statuses, RCH_CAMERA_STATE_STOPPED)
    + SumStateCountFromStatuses(statuses, RCH_CAMERA_STATE_STOPPING);

  if (!Expect(diagnostics.cameras_starting_count == starting_sum,
              "aggregate starting count must match per-camera status states")
      || !Expect(diagnostics.cameras_receiving_count == receiving_sum,
                 "aggregate receiving count must match per-camera status states")
      || !Expect(diagnostics.cameras_waiting_to_retry_count == retry_sum,
                 "aggregate retry count must match per-camera status states")
      || !Expect(diagnostics.cameras_failed_count == failed_sum,
                 "aggregate failed count must match per-camera status states")
      || !Expect(diagnostics.cameras_stopped_count == stopped_or_stopping_sum,
                 "aggregate stopped count must match per-camera status states")) {
    return 1;
  }

  std::atomic<bool> keep_running{true};
  std::atomic<bool> worker_failed{false};
  std::thread reader([&] {
    while (keep_running.load(std::memory_order_acquire)) {
      std::uint32_t bytes = 0;
      std::uint32_t count = 0;
      const auto list_result = rch_camera_enumerate_ids(engine, nullptr, 0, &bytes, &count);
      if (list_result != RCH_RESULT_OK) {
        worker_failed.store(true, std::memory_order_release);
        return;
      }

      rch_engine_diagnostics_v1 local{};
      local.struct_size = static_cast<std::uint32_t>(sizeof(local));
      local.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
      const auto diagnostics_result = rch_engine_get_diagnostics(engine, &local);
      if (diagnostics_result != RCH_RESULT_OK
          || local.active_rtsp_session_total > local.configured_camera_count
          || local.active_decoder_total > local.configured_camera_count) {
        worker_failed.store(true, std::memory_order_release);
        return;
      }
    }
  });

  for (int i = 0; i < 100; ++i) {
    const auto remove_result = rch_camera_remove(engine, "cam-c");
    if (remove_result != RCH_RESULT_OK && remove_result != RCH_RESULT_NOT_CONFIGURED) {
      worker_failed.store(true, std::memory_order_release);
      break;
    }

    const auto add_result = rch_camera_add(engine, &readd_config);
    if (add_result != RCH_RESULT_OK) {
      worker_failed.store(true, std::memory_order_release);
      break;
    }
  }

  keep_running.store(false, std::memory_order_release);
  reader.join();

  if (!Expect(!worker_failed.load(std::memory_order_acquire),
              "enumeration and diagnostics queries must remain race-safe during add/remove")) {
    return 1;
  }

  for (const auto& camera_id : expected_sorted_ids) {
    rch_camera_stop_by_id(engine, camera_id.c_str());
    rch_camera_remove(engine, camera_id.c_str());
  }

  const auto empty_after_teardown = QueryDiagnostics(engine, diagnostics_ok);
  if (!Expect(diagnostics_ok, "diagnostics query must succeed after teardown")
      || !Expect(empty_after_teardown.configured_camera_count == 0,
                 "teardown must return configured camera count to zero")
      || !Expect(empty_after_teardown.active_rtsp_session_total == 0,
                 "teardown must return aggregate RTSP ownership to zero")
      || !Expect(empty_after_teardown.active_decoder_total == 0,
                 "teardown must return aggregate decoder ownership to zero")) {
    return 1;
  }

  if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK, "engine teardown must succeed")) {
    return 1;
  }

  return 0;
}
