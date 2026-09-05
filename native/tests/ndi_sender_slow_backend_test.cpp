#include "engine/ndi_sender_test_hooks.h"
#include "robocamhub_native.h"

#include <chrono>
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

rch_view_status_v1 ViewStatus(rch_view_handle view, bool& ok)
{
  rch_view_status_v1 status{};
  status.struct_size = sizeof(status);
  status.struct_version = RCH_VIEW_STATUS_VERSION;
  ok = rch_view_get_status(view, &status) == RCH_RESULT_OK;
  return status;
}

rch_ndi_sender_status_v1 SenderStatus(rch_ndi_sender_handle sender, bool& ok)
{
  rch_ndi_sender_status_v1 status{};
  status.struct_size = sizeof(status);
  status.struct_version = RCH_NDI_SENDER_STATUS_VERSION;
  ok = rch_ndi_sender_get_status(sender, &status) == RCH_RESULT_OK;
  return status;
}

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  rch_ndi_sender_handle sender = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK,
              "engine creation must succeed for slow-backend validation")
      || !Expect(rch_view_create(engine, "slow-backend-view", &view) == RCH_RESULT_OK,
                 "View creation must succeed for slow-backend validation")
      || !Expect(rch_ndi_sender_create(view, "ROBOCAM - Slow Backend", &sender) == RCH_RESULT_OK,
                 "sender creation must use the existing composed View output")
      || !Expect(robocamhub::testing::SetNdiSenderBackendDelay(sender, 800U) == RCH_RESULT_OK,
                 "test-only backend delay must configure before sender start")
      || !Expect(rch_ndi_sender_start(sender) == RCH_RESULT_OK,
                 "slow deterministic sender must start")) {
    if (sender != nullptr) rch_ndi_sender_destroy(sender);
    if (view != nullptr) rch_view_destroy(view);
    if (engine != nullptr) rch_engine_destroy(engine);
    return 1;
  }

  bool sender_blocked = false;
  const auto ready_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
  while (std::chrono::steady_clock::now() < ready_deadline) {
    bool ok = false;
    const auto status = SenderStatus(sender, ok);
    if (ok && status.unique_sequence_observed_count == 1U && status.sent_frame_count == 0U) {
      sender_blocked = true;
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  bool blocked_view_ok = false;
  const auto blocked_view = ViewStatus(view, blocked_view_ok);
  std::this_thread::sleep_for(std::chrono::milliseconds(300));
  bool advanced_view_ok = false;
  bool still_blocked_sender_ok = false;
  const auto advanced_view = ViewStatus(view, advanced_view_ok);
  const auto still_blocked_sender = SenderStatus(sender, still_blocked_sender_ok);

  bool first_send_observed = false;
  rch_ndi_sender_status_v1 first_send{};
  const auto first_send_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
  while (std::chrono::steady_clock::now() < first_send_deadline) {
    bool ok = false;
    const auto status = SenderStatus(sender, ok);
    if (ok && status.sent_frame_count >= 1U) {
      first_send = status;
      first_send_observed = true;
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }

  bool second_send_observed = false;
  rch_ndi_sender_status_v1 second_send{};
  const auto second_send_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
  while (std::chrono::steady_clock::now() < second_send_deadline) {
    bool ok = false;
    const auto status = SenderStatus(sender, ok);
    if (ok && status.sent_frame_count >= first_send.sent_frame_count + 1U) {
      second_send = status;
      second_send_observed = true;
      break;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(10));
  }
  bool final_view_ok = false;
  const auto final_view = ViewStatus(view, final_view_ok);

  const bool passed =
    Expect(sender_blocked && blocked_view_ok && advanced_view_ok && still_blocked_sender_ok,
           "sender and View snapshots must be available while the backend is blocked")
    && Expect(advanced_view.latest_composed_frame_sequence
                > blocked_view.latest_composed_frame_sequence,
              "compositor must advance while the sender backend is blocked")
    && Expect(still_blocked_sender.sent_frame_count == 0U
                && still_blocked_sender.unique_sequence_observed_count == 1U,
              "blocked sender must not catch up or queue additional frames")
    && Expect(first_send_observed && second_send_observed && final_view_ok,
              "slow backend must eventually accept two frames")
    && Expect(second_send.sent_frame_count == first_send.sent_frame_count + 1U,
              "sender must accept only one frame per slow backend call")
    && Expect(second_send.latest_sent_sequence > first_send.latest_sent_sequence + 1U,
              "sender must skip directly to a newer composed sequence instead of draining backlog")
    && Expect(second_send.dropped_or_skipped_frame_count > 0U,
              "sequence gaps from newest-frame skipping must be observable")
    && Expect(second_send.latest_sent_sequence <= final_view.latest_composed_frame_sequence,
              "sender cannot report a sequence newer than the View")
    && Expect(second_send.unique_sequence_observed_count <= second_send.sent_frame_count + 1U,
              "slow sender must retain at most one in-flight frame and no backlog");

  const auto stop_result = rch_ndi_sender_stop(sender);
  const auto sender_destroy_result = rch_ndi_sender_destroy(sender);
  const auto view_destroy_result = rch_view_destroy(view);
  const auto engine_destroy_result = rch_engine_destroy(engine);
  return passed
      && Expect(stop_result == RCH_RESULT_OK, "slow sender stop must remain deterministic")
      && Expect(sender_destroy_result == RCH_RESULT_OK, "slow sender destroy must release ownership")
      && Expect(view_destroy_result == RCH_RESULT_OK, "View destroy must remain safe after slow sender")
      && Expect(engine_destroy_result == RCH_RESULT_OK, "engine teardown must remain safe after slow sender")
    ? 0
    : 1;
}
