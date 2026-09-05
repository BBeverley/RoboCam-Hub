#include "robocamhub_native.h"

#include <chrono>
#include <cstdint>
#include <cstring>
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

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK,
              "engine creation must succeed for a sender smoke test")) {
    return 1;
  }

  rch_view_handle view = nullptr;
  if (!Expect(rch_view_create(engine, "gate4a-view", &view) == RCH_RESULT_OK,
              "view creation must succeed before sender creation")) {
    rch_engine_destroy(engine);
    return 1;
  }

  rch_ndi_sender_handle sender = nullptr;
  if (!Expect(rch_ndi_sender_create(nullptr, "ROBOCAM - Gate4A", &sender) == RCH_RESULT_INVALID_HANDLE,
              "sender creation must reject a null View handle")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_create(view, "ROBOCAM - Gate4A", &sender) == RCH_RESULT_OK,
              "sender creation must attach to an existing native View")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_start(sender) == RCH_RESULT_OK,
              "sender start must begin the bounded view-frame worker")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_start(sender) == RCH_RESULT_ALREADY_STARTED,
              "re-starting an active sender must be rejected without rebuilding the View")) {
    rch_ndi_sender_stop(sender);
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  bool observed_sequence = false;
  bool observed_sender_activity = false;
  for (int iteration = 0; iteration < 80; ++iteration) {
    rch_view_status_v1 view_status{};
    view_status.struct_size = sizeof(view_status);
    view_status.struct_version = RCH_VIEW_STATUS_VERSION;
    const auto view_result = rch_view_get_status(view, &view_status);
    if (view_result == RCH_RESULT_OK && view_status.latest_composed_frame_sequence > 0ULL) {
      observed_sequence = true;
    }

    rch_ndi_sender_status_v1 sender_status{};
    sender_status.struct_size = sizeof(sender_status);
    sender_status.struct_version = RCH_NDI_SENDER_STATUS_VERSION;
    const auto sender_result = rch_ndi_sender_get_status(sender, &sender_status);
    if (sender_result == RCH_RESULT_OK
        && (sender_status.sent_frame_count > 0ULL || sender_status.state == RCH_NDI_SENDER_STATE_WAITING_FOR_VIEW_FRAME)) {
      observed_sender_activity = true;
    }

    if (observed_sequence && observed_sender_activity) {
      break;
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  if (!Expect(observed_sequence,
              "view compositor must continue publishing composed frames while sender is active")
      || !Expect(observed_sender_activity,
                 "sender status must observe advancing composed-frame ownership without blocking the View")) {
    rch_ndi_sender_stop(sender);
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_stop(sender) == RCH_RESULT_OK,
              "sender stop must release its worker deterministically")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_destroy(sender) == RCH_RESULT_OK,
              "sender destroy must release sender ownership cleanly")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_view_destroy(view) == RCH_RESULT_OK,
              "view destroy must tear down its render loop without leaving the sender attached")) {
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
              "engine destroy must release all native objects cleanly")) {
    return 1;
  }

  return 0;
}
