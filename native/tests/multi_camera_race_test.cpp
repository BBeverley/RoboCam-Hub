#include "robocamhub_native.h"

#include <chrono>
#include <cstdint>
#include <functional>
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

[[maybe_unused]] bool WaitForStatus(rch_engine_handle engine,
                   const char* camera_id,
                   std::chrono::milliseconds timeout,
                   std::function<bool(const rch_camera_status_v1&)> predicate)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    rch_camera_status_v1 status{};
    status.struct_size = static_cast<std::uint32_t>(sizeof(status));
    status.struct_version = RCH_CAMERA_STATUS_VERSION;
    if (rch_camera_get_status_by_id(engine, camera_id, &status) == RCH_RESULT_OK && predicate(status)) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  return false;
}

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine creation must succeed")) {
    return 1;
  }

  const std::string camera_id = "race-removed-camera";
  const std::string url = "rtsp://127.0.0.1:1/profile2/media.smp";

  rch_camera_config_v1 config{
    static_cast<std::uint32_t>(sizeof(rch_camera_config_v1)),
    RCH_CAMERA_CONFIG_VERSION,
    camera_id.c_str(),
    url.c_str(),
    2000,
    0,
  };

  if (!Expect(rch_camera_add(engine, &config) == RCH_RESULT_OK,
              "camera add must succeed before race exercise")) {
    rch_engine_destroy(engine);
    return 1;
  }

  std::atomic<bool> removed{false};
  std::atomic<bool> start_after_remove{false};
  std::thread remover([&] {
    std::this_thread::sleep_for(std::chrono::milliseconds(30));
    const auto result = rch_camera_remove(engine, camera_id.c_str());
    removed.store(true, std::memory_order_release);
    start_after_remove.store(result == RCH_RESULT_OK, std::memory_order_release);
  });

  std::thread starter([&] {
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
    const auto result = rch_camera_start_by_id(engine, camera_id.c_str());
    if (removed.load(std::memory_order_acquire)) {
      start_after_remove.store(start_after_remove.load(std::memory_order_acquire)
                                 && result == RCH_RESULT_NOT_CONFIGURED,
                               std::memory_order_release);
    }
  });

  starter.join();
  remover.join();

  const auto final_result = rch_camera_start_by_id(engine, camera_id.c_str());
  const bool passed = Expect(start_after_remove.load(std::memory_order_acquire),
                            "stale start must be rejected after remove")
    && Expect(final_result == RCH_RESULT_NOT_CONFIGURED,
              "removed camera must not accept new lifecycle operations after removal");

  rch_engine_destroy(engine);
  return passed ? 0 : 1;
}
