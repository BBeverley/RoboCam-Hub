#include "preview/preview_test_hooks.h"
#include "robocamhub_native.h"

#include <algorithm>
#include <array>
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

rch_view_preview_config_v1 PreviewConfig(std::uint32_t target_fps = 30U)
{
  rch_view_preview_config_v1 config{};
  config.struct_size = sizeof(config);
  config.struct_version = RCH_VIEW_PREVIEW_CONFIG_VERSION_V1;
  config.host_native_handle = 1U;
  config.platform = RCH_VIEW_PREVIEW_PLATFORM_MACOS_NSVIEW;
  config.target_fps = target_fps;
  return config;
}

rch_engine_diagnostics_v1 EngineStatus(rch_engine_handle engine, bool& ok)
{
  rch_engine_diagnostics_v1 status{};
  status.struct_size = sizeof(status);
  status.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  ok = rch_engine_get_diagnostics(engine, &status) == RCH_RESULT_OK;
  return status;
}

rch_view_status_v1 ViewStatus(rch_view_handle view, bool& ok)
{
  rch_view_status_v1 status{};
  status.struct_size = sizeof(status);
  status.struct_version = RCH_VIEW_STATUS_VERSION;
  ok = rch_view_get_status(view, &status) == RCH_RESULT_OK;
  return status;
}

rch_view_preview_status_v1 PreviewStatus(rch_view_preview_handle preview, bool& ok)
{
  rch_view_preview_status_v1 status{};
  status.struct_size = sizeof(status);
  status.struct_version = RCH_VIEW_PREVIEW_STATUS_VERSION;
  ok = rch_view_preview_get_status(preview, &status) == RCH_RESULT_OK;
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

template <typename Predicate>
bool WaitUntil(Predicate predicate, std::chrono::milliseconds timeout)
{
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    if (predicate()) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
  }
  return predicate();
}

template <std::size_t Size>
bool IsFilledWith(const std::array<std::uint8_t, Size>& bytes, std::uint8_t value)
{
  return std::all_of(bytes.begin(), bytes.end(), [value](std::uint8_t item) {
    return item == value;
  });
}

bool TestCallerSizeCanaries()
{
  struct ConfigWithCanary final {
    rch_view_preview_config_v1 value;
    std::array<std::uint8_t, 32> canary;
  } config{};
  config.value = PreviewConfig();
  config.value.struct_size = sizeof(config);
  config.canary.fill(0xA5U);

  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  rch_view_preview_handle preview = nullptr;
  const bool created = rch_engine_create(&engine) == RCH_RESULT_OK
    && rch_view_create(engine, "canary-view", &view) == RCH_RESULT_OK
    && rch_view_preview_create(view, &config.value, &preview) == RCH_RESULT_OK;

  struct StatusWithCanary final {
    rch_view_preview_status_v1 value;
    std::array<std::uint8_t, 32> canary;
  } status{};
  status.value.struct_size = sizeof(status);
  status.value.struct_version = RCH_VIEW_PREVIEW_STATUS_VERSION_V1;
  status.canary.fill(0x5AU);
  const bool queried = created
    && rch_view_preview_get_status(preview, &status.value) == RCH_RESULT_OK;

  const bool passed =
    Expect(created && queried, "preview must accept larger caller-owned v1 buffers")
    && Expect(status.value.struct_size == sizeof(rch_view_preview_status_v1),
              "status must report the exact v1 size written")
    && Expect(status.value.struct_version == RCH_VIEW_PREVIEW_STATUS_VERSION_V1,
              "status must preserve the requested v1 version")
    && Expect(IsFilledWith(status.canary, 0x5AU),
              "status must not overwrite bytes beyond the v1 structure")
    && Expect(IsFilledWith(config.canary, 0xA5U),
              "preview creation must not overwrite the caller config buffer");

  if (preview != nullptr) rch_view_preview_destroy(preview);
  if (view != nullptr) rch_view_destroy(view);
  if (engine != nullptr) rch_engine_destroy(engine);
  return passed;
}

bool TestOwnershipAndRepeatedAttachDetach()
{
  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  rch_ndi_sender_handle sender = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "ownership engine create")
      || !Expect(rch_view_create(engine, "ownership-view", &view) == RCH_RESULT_OK,
                 "ownership View create")
      || !Expect(rch_ndi_sender_create(view, "ownership-sender", &sender) == RCH_RESULT_OK,
                 "ownership sender create")) {
    return false;
  }

  bool before_engine_ok = false;
  bool before_view_ok = false;
  const auto before_engine = EngineStatus(engine, before_engine_ok);
  const auto before_view = ViewStatus(view, before_view_ok);
  bool passed = true;
  for (std::uint32_t iteration = 0; iteration < 50U; ++iteration) {
    auto config = PreviewConfig();
    rch_view_preview_handle preview = nullptr;
    passed = Expect(rch_view_preview_create(view, &config, &preview) == RCH_RESULT_OK,
                    "repeated preview attach must succeed") && passed;
    bool during_engine_ok = false;
    bool during_view_ok = false;
    const auto during_engine = EngineStatus(engine, during_engine_ok);
    const auto during_view = ViewStatus(view, during_view_ok);
    passed = Expect(during_engine_ok && during_view_ok,
                    "ownership diagnostics must remain queryable") && passed;
    passed = Expect(during_engine.active_rtsp_session_total == before_engine.active_rtsp_session_total
                      && during_engine.active_decoder_total == before_engine.active_decoder_total,
                    "preview must not add RTSP sessions or decoders") && passed;
    passed = Expect(during_engine.view_count == before_engine.view_count
                      && during_engine.view_count == 1U,
                    "preview must not create a second View/compositor") && passed;
    passed = Expect(during_view.output_consumer_count == before_view.output_consumer_count
                      && during_view.output_consumer_count == 1U,
                    "preview must not change NDI sender ownership") && passed;
    passed = Expect(rch_view_preview_destroy(preview) == RCH_RESULT_OK,
                    "repeated preview detach must succeed") && passed;
    passed = Expect(robocamhub::testing::ActivePreviewSurfaceCount() == 0U,
                    "each detach must release its native presentation surface") && passed;
  }

  rch_ndi_sender_destroy(sender);
  rch_view_destroy(view);
  rch_engine_destroy(engine);
  return passed && before_engine_ok && before_view_ok;
}

bool TestSlowPreviewAndSurfaceLifecycle()
{
  robocamhub::testing::SetPreviewPresentationDelayMs(250U);
  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  rch_ndi_sender_handle sender = nullptr;
  rch_view_preview_handle preview = nullptr;
  auto config = PreviewConfig(60U);
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "slow preview engine create")
      || !Expect(rch_view_create(engine, "slow-preview-view", &view) == RCH_RESULT_OK,
                 "slow preview View create")
      || !Expect(rch_ndi_sender_create(view, "slow-preview-sender", &sender) == RCH_RESULT_OK,
                 "slow preview sender create")
      || !Expect(rch_ndi_sender_start(sender) == RCH_RESULT_OK, "slow preview sender start")
      || !Expect(rch_view_preview_create(view, &config, &preview) == RCH_RESULT_OK,
                 "slow preview attach")) {
    return false;
  }

  const bool warmed = WaitUntil([&] {
    bool preview_ok = false;
    bool sender_ok = false;
    const auto preview_status = PreviewStatus(preview, preview_ok);
    const auto sender_status = SenderStatus(sender, sender_ok);
    return preview_ok && sender_ok
      && preview_status.presented_frame_count >= 2U
      && sender_status.sent_frame_count >= 20U;
  }, std::chrono::seconds(4));

  bool view_before_ok = false;
  bool preview_before_ok = false;
  bool sender_before_ok = false;
  const auto view_before = ViewStatus(view, view_before_ok);
  const auto preview_before = PreviewStatus(preview, preview_before_ok);
  const auto sender_before = SenderStatus(sender, sender_before_ok);
  robocamhub::testing::RequestPreviewSurfaceRecreation();
  std::this_thread::sleep_for(std::chrono::milliseconds(650));
  bool view_after_ok = false;
  bool preview_after_ok = false;
  bool sender_after_ok = false;
  const auto view_after = ViewStatus(view, view_after_ok);
  const auto preview_after = PreviewStatus(preview, preview_after_ok);
  const auto sender_after = SenderStatus(sender, sender_after_ok);

  const bool passed =
    Expect(warmed && view_before_ok && preview_before_ok && sender_before_ok
             && view_after_ok && preview_after_ok && sender_after_ok,
           "slow preview diagnostics must become available")
    && Expect(view_after.latest_composed_frame_sequence
                > view_before.latest_composed_frame_sequence + 5U,
              "slow preview must not stall the View compositor")
    && Expect(sender_after.latest_sent_sequence > sender_before.latest_sent_sequence + 5U,
              "slow preview must not stall the independent NDI sender")
    && Expect(preview_after.latest_presented_sequence
                > preview_before.latest_presented_sequence + 1U,
              "slow preview must keep presenting new frames")
    && Expect(preview_after.latest_presented_sequence
                > preview_before.latest_presented_sequence
                  + (preview_after.presented_frame_count - preview_before.presented_frame_count),
              "slow preview must skip to newest frames instead of draining a backlog")
    && Expect(preview_after.dropped_or_skipped_frame_count
                > preview_before.dropped_or_skipped_frame_count,
              "newest-frame sequence gaps must be reported")
    && Expect(preview_after.surface_recreate_count > preview_before.surface_recreate_count,
              "surface recreation must be safe and observable")
    && Expect(sender_after.latest_sent_sequence <= view_after.latest_composed_frame_sequence,
              "NDI sender cannot advance beyond its View");

  rch_view_preview_destroy(preview);
  rch_ndi_sender_stop(sender);
  rch_ndi_sender_destroy(sender);
  rch_view_destroy(view);
  rch_engine_destroy(engine);
  robocamhub::testing::SetPreviewPresentationDelayMs(0U);
  return passed;
}

bool TestViewAndEngineDestructionWhileAttached()
{
  auto config = PreviewConfig();
  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  rch_view_preview_handle preview = nullptr;
  bool passed = Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "teardown engine create")
    && Expect(rch_view_create(engine, "view-destroy-preview", &view) == RCH_RESULT_OK,
              "teardown View create")
    && Expect(rch_view_preview_create(view, &config, &preview) == RCH_RESULT_OK,
              "teardown preview attach")
    && Expect(rch_view_destroy(view) == RCH_RESULT_OK,
              "destroying a View with an attached preview must be safe");
  view = nullptr;
  const bool view_failure_observed = WaitUntil([&] {
    bool ok = false;
    const auto status = PreviewStatus(preview, ok);
    return ok && status.state == RCH_VIEW_PREVIEW_STATE_FAILED
      && status.last_result == RCH_RESULT_INVALID_HANDLE;
  }, std::chrono::seconds(2));
  passed = Expect(view_failure_observed,
                  "preview must report a non-crashing failure after View destruction") && passed;
  passed = Expect(rch_view_preview_destroy(preview) == RCH_RESULT_OK,
                  "preview detach after View destruction must be safe") && passed;
  preview = nullptr;
  passed = Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
                  "engine destroy after preview detach must be safe") && passed;

  engine = nullptr;
  view = nullptr;
  preview = nullptr;
  passed = Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine-active engine create") && passed;
  passed = Expect(rch_view_create(engine, "engine-destroy-preview", &view) == RCH_RESULT_OK,
                  "engine-active View create") && passed;
  passed = Expect(rch_view_preview_create(view, &config, &preview) == RCH_RESULT_OK,
                  "engine-active preview attach") && passed;
  passed = Expect(rch_engine_destroy(engine) == RCH_RESULT_OK,
                  "destroying an engine with an attached preview must be safe") && passed;
  engine = nullptr;
  const bool engine_failure_observed = WaitUntil([&] {
    bool ok = false;
    const auto status = PreviewStatus(preview, ok);
    return ok && status.state == RCH_VIEW_PREVIEW_STATE_FAILED
      && status.last_result == RCH_RESULT_INVALID_HANDLE;
  }, std::chrono::seconds(2));
  passed = Expect(engine_failure_observed,
                  "preview must report a non-crashing failure after engine destruction") && passed;
  passed = Expect(rch_view_preview_destroy(preview) == RCH_RESULT_OK,
                  "preview detach after engine destruction must be safe") && passed;
  passed = Expect(rch_view_destroy(view) == RCH_RESULT_OK,
                  "stale View wrapper teardown after engine destruction must be safe") && passed;
  return passed && Expect(robocamhub::testing::ActivePreviewSurfaceCount() == 0U,
                          "teardown tests must release all fake surfaces");
}

}  // namespace

int main()
{
  const bool passed = TestCallerSizeCanaries()
    && TestOwnershipAndRepeatedAttachDetach()
    && TestSlowPreviewAndSurfaceLifecycle()
    && TestViewAndEngineDestructionWhileAttached();
  return passed ? 0 : 1;
}
