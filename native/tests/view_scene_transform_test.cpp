#include "robocamhub_native.h"

#include <gst/rtsp-server/rtsp-server.h>

#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr auto kTimeout = std::chrono::seconds(12);

bool Expect(bool condition, const char* message)
{
  if (!condition) {
    std::cerr << "FAILED: " << message << '\n';
  }
  return condition;
}

class LoopbackRtspFixture final {
public:
  bool Start(const std::string& pattern)
  {
    Stop();
    context_ = g_main_context_new();
    loop_ = g_main_loop_new(context_, FALSE);
    server_ = gst_rtsp_server_new();
    gst_rtsp_server_set_address(server_, "127.0.0.1");
    const auto service = fixed_port_ == 0U ? std::string("0") : std::to_string(fixed_port_);
    gst_rtsp_server_set_service(server_, service.c_str());

    auto* mounts = gst_rtsp_server_get_mount_points(server_);
    factory_ = gst_rtsp_media_factory_new();
    const auto launch = "( videotestsrc is-live=true pattern=" + pattern + " ! "
      "video/x-raw,format=RGBA,width=320,height=180,framerate=30/1 "
      "! videoconvert ! x264enc tune=zerolatency speed-preset=ultrafast key-int-max=1 "
      "! rtph264pay name=pay0 pt=96 config-interval=1 )";
    gst_rtsp_media_factory_set_launch(factory_, launch.c_str());
    gst_rtsp_media_factory_set_protocols(factory_, GST_RTSP_LOWER_TRANS_UDP);
    gst_rtsp_mount_points_add_factory(mounts, "/scene", factory_);
    g_object_unref(mounts);

    source_id_ = gst_rtsp_server_attach(server_, context_);
    const auto port = gst_rtsp_server_get_bound_port(server_);
    if (source_id_ == 0U || port == 0U) {
      return false;
    }
    if (fixed_port_ == 0U) {
      fixed_port_ = port;
    }
    url_ = "rtsp://127.0.0.1:" + std::to_string(port) + "/scene";
    thread_ = std::thread([this] { g_main_loop_run(loop_); });
    return true;
  }

  void Stop()
  {
    if (server_ != nullptr) {
      const auto removed = gst_rtsp_server_client_filter(
        server_,
        [](GstRTSPServer*, GstRTSPClient*, gpointer) { return GST_RTSP_FILTER_REMOVE; },
        nullptr);
      (void)removed;
    }
    if (loop_ != nullptr) {
      g_main_loop_quit(loop_);
    }
    if (thread_.joinable()) {
      thread_.join();
    }
    if (context_ != nullptr && source_id_ != 0U) {
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
    source_id_ = 0U;
  }

  ~LoopbackRtspFixture()
  {
    Stop();
  }

  [[nodiscard]] const std::string& Url() const
  {
    return url_;
  }

private:
  GMainContext* context_{nullptr};
  GMainLoop* loop_{nullptr};
  GstRTSPServer* server_{nullptr};
  GstRTSPMediaFactory* factory_{nullptr};
  guint source_id_{0};
  std::thread thread_;
  std::string url_;
  guint fixed_port_{0};
};

rch_view_camera_element_v1 MakeElement(
  const char* element_id,
  const char* camera_id,
  double x = 0.0,
  double y = 0.0,
  double width = 1.0,
  double height = 1.0,
  std::int32_t z_order = 0)
{
  rch_view_camera_element_v1 element{};
  element.struct_size = sizeof(element);
  element.struct_version = RCH_VIEW_CAMERA_ELEMENT_VERSION;
  element.element_id_utf8 = element_id;
  element.camera_id_utf8 = camera_id;
  element.x = x;
  element.y = y;
  element.width = width;
  element.height = height;
  element.z_order = z_order;
  element.fit_mode = RCH_VIEW_CAMERA_FIT_STRETCH;
  element.visible = 1U;
  element.enabled = 1U;
  return element;
}

bool AddAndStartCamera(
  rch_engine_handle engine,
  const char* camera_id,
  const std::string& url)
{
  const rch_camera_config_v1 config{
    sizeof(rch_camera_config_v1),
    RCH_CAMERA_CONFIG_VERSION,
    camera_id,
    url.c_str(),
    3000,
    0,
  };
  if (rch_camera_add(engine, &config) != RCH_RESULT_OK
      || rch_camera_start_by_id(engine, camera_id) != RCH_RESULT_OK) {
    return false;
  }

  const auto deadline = std::chrono::steady_clock::now() + kTimeout;
  while (std::chrono::steady_clock::now() < deadline) {
    rch_camera_status_v1 status{};
    status.struct_size = sizeof(status);
    status.struct_version = RCH_CAMERA_STATUS_VERSION;
    if (rch_camera_get_status_by_id(engine, camera_id, &status) != RCH_RESULT_OK) {
      return false;
    }
    if (status.state == RCH_CAMERA_STATE_RECEIVING && status.has_latest_frame == 1U) {
      return status.active_rtsp_session_count == 1U && status.active_decoder_count == 1U;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

bool WaitForReceiving(rch_engine_handle engine, const char* camera_id)
{
  const auto deadline = std::chrono::steady_clock::now() + kTimeout;
  while (std::chrono::steady_clock::now() < deadline) {
    rch_camera_status_v1 status{};
    status.struct_size = sizeof(status);
    status.struct_version = RCH_CAMERA_STATUS_VERSION;
    if (rch_camera_get_status_by_id(engine, camera_id, &status) != RCH_RESULT_OK) {
      return false;
    }
    if (status.state == RCH_CAMERA_STATE_RECEIVING && status.has_latest_frame == 1U) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

rch_view_status_v1 ViewStatus(rch_view_handle view);

bool WaitForFrozenElement(rch_view_handle view)
{
  const auto deadline = std::chrono::steady_clock::now() + kTimeout;
  while (std::chrono::steady_clock::now() < deadline) {
    const auto status = ViewStatus(view);
    if (status.frozen_source_count >= 1U) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

rch_view_status_v1 ViewStatus(rch_view_handle view)
{
  rch_view_status_v1 status{};
  status.struct_size = sizeof(status);
  status.struct_version = RCH_VIEW_STATUS_VERSION;
  (void)rch_view_get_status(view, &status);
  return status;
}

bool WaitForNewComposition(rch_view_handle view, std::uint64_t previous_sequence)
{
  const auto deadline = std::chrono::steady_clock::now() + kTimeout;
  while (std::chrono::steady_clock::now() < deadline) {
    const auto status = ViewStatus(view);
    if (status.latest_composed_frame_sequence >= previous_sequence + 3U) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(15));
  }
  return false;
}

bool WaitForSenderFrame(rch_ndi_sender_handle sender)
{
  const auto deadline = std::chrono::steady_clock::now() + kTimeout;
  while (std::chrono::steady_clock::now() < deadline) {
    rch_ndi_sender_status_v1 status{};
    status.struct_size = sizeof(status);
    status.struct_version = RCH_NDI_SENDER_STATUS_VERSION;
    if (rch_ndi_sender_get_status(sender, &status) != RCH_RESULT_OK) {
      return false;
    }
    if (status.sent_frame_count > 0U && status.latest_sent_sequence > 0U) {
      return true;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

bool ApplyAndWait(
  rch_view_handle view,
  const rch_view_camera_element_v1* elements,
  std::uint32_t count)
{
  const auto sequence = ViewStatus(view).latest_composed_frame_sequence;
  return rch_view_apply_camera_scene(view, elements, count) == RCH_RESULT_OK
    && WaitForNewComposition(view, sequence);
}

using Pixel = std::array<std::uint8_t, 4>;

Pixel Sample(rch_view_handle view, std::uint32_t x, std::uint32_t y, bool& ok)
{
  rch_view_frame_lease_handle lease = nullptr;
  ok = rch_view_acquire_latest_frame(view, &lease) == RCH_RESULT_OK && lease != nullptr;
  Pixel pixel{};
  if (ok) {
    ok = rch_view_frame_lease_sample_rgba(
      lease, x, y, &pixel[0], &pixel[1], &pixel[2], &pixel[3]) == RCH_RESULT_OK;
  }
  if (lease != nullptr) {
    ok = rch_view_frame_lease_destroy(lease) == RCH_RESULT_OK && ok;
  }
  return pixel;
}

bool PixelsNear(const Pixel& left, const Pixel& right, int tolerance = 8)
{
  for (std::size_t channel = 0; channel < 4U; ++channel) {
    if (std::abs(static_cast<int>(left[channel]) - static_cast<int>(right[channel])) > tolerance) {
      return false;
    }
  }
  return true;
}

bool MostlyRed(const Pixel& pixel)
{
  return pixel[0] > 150U && pixel[1] < 110U && pixel[2] < 110U;
}

bool MostlyGreen(const Pixel& pixel)
{
  return pixel[1] > 150U && pixel[0] < 110U && pixel[2] < 110U;
}

bool MostlyBlack(const Pixel& pixel)
{
  return pixel[0] < 24U && pixel[1] < 24U && pixel[2] < 24U;
}

bool SceneInputCanaryIsPreserved(rch_view_handle view, const char* camera_id)
{
  alignas(rch_view_camera_element_v1)
    std::uint8_t storage[sizeof(rch_view_camera_element_v1) + 32U];
  std::memset(storage, 0xA5, sizeof(storage));
  auto element = MakeElement("canary-element", camera_id);
  element.struct_size = sizeof(storage);
  std::memcpy(storage, &element, sizeof(element));
  const auto result = rch_view_apply_camera_scene(
    view,
    reinterpret_cast<const rch_view_camera_element_v1*>(storage),
    1U);
  if (result != RCH_RESULT_OK) {
    return false;
  }
  for (std::size_t index = sizeof(element); index < sizeof(storage); ++index) {
    if (storage[index] != UINT8_C(0xA5)) {
      return false;
    }
  }
  return true;
}

}  // namespace

int main()
{
  LoopbackRtspFixture asymmetric_fixture;
  LoopbackRtspFixture red_fixture;
  LoopbackRtspFixture green_fixture;
  if (!Expect(asymmetric_fixture.Start("smpte"), "asymmetric fixture must start")
      || !Expect(red_fixture.Start("red"), "red fixture must start")
      || !Expect(green_fixture.Start("green"), "green fixture must start")) {
    return 1;
  }

  rch_engine_handle engine = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine must be created")
      || !Expect(AddAndStartCamera(engine, "scene-asymmetric", asymmetric_fixture.Url()),
                 "asymmetric camera must receive")
      || !Expect(AddAndStartCamera(engine, "scene-red", red_fixture.Url()),
                 "red camera must receive")
      || !Expect(AddAndStartCamera(engine, "scene-green", green_fixture.Url()),
                 "green camera must receive")) {
    if (engine != nullptr) {
      rch_engine_destroy(engine);
    }
    return 1;
  }

  rch_view_handle view = nullptr;
  if (!Expect(rch_view_create(engine, "scene-transform-view", &view) == RCH_RESULT_OK,
              "scene View must be created")) {
    rch_engine_destroy(engine);
    return 1;
  }

  bool ok = false;
  auto red = MakeElement("red-background", "scene-red");
  if (!Expect(ApplyAndWait(view, &red, 1U), "baseline red scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto red_centre = Sample(view, 960, 540, ok);
  if (!Expect(ok && MostlyRed(red_centre), "baseline scene must render red")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  auto invalid = MakeElement("invalid", "scene-green");
  invalid.width = 0.0;
  const auto invalid_result = rch_view_apply_camera_scene(view, &invalid, 1U);
  const auto after_invalid = Sample(view, 960, 540, ok);
  std::array<rch_view_camera_element_v1, 2> duplicates{
    MakeElement("duplicate", "scene-red"),
    MakeElement("duplicate", "scene-green"),
  };
  auto overcrop = MakeElement("overcrop", "scene-red");
  overcrop.crop_left = 0.6;
  overcrop.crop_right = 0.4;
  auto not_finite = MakeElement("not-finite", "scene-red");
  not_finite.x = std::numeric_limits<double>::quiet_NaN();
  auto absurd = MakeElement("absurd", "scene-red");
  absurd.x = 17.0;
  auto unsupported_rotation = MakeElement("rotation", "scene-red");
  unsupported_rotation.rotation_degrees = 361.0;
  auto negative_size = MakeElement("negative", "scene-red");
  negative_size.height = -0.5;
  auto invalid_boolean = MakeElement("boolean", "scene-red");
  invalid_boolean.visible = 2U;
  auto invalid_fit = MakeElement("fit", "scene-red");
  invalid_fit.fit_mode = UINT32_C(99);
  auto unknown_camera = MakeElement("unknown", "does-not-exist");

  if (!Expect(invalid_result == RCH_RESULT_INVALID_ARGUMENT,
              "zero-size scene apply must be rejected")
      || !Expect(ok && MostlyRed(after_invalid),
                 "invalid scene apply must retain the previous rendered scene")
      || !Expect(ViewStatus(view).bound_source_count == 1U,
                 "invalid scene apply must retain previous bindings atomically")
      || !Expect(rch_view_apply_camera_scene(
                   view,
                   duplicates.data(),
                   static_cast<std::uint32_t>(duplicates.size()))
                   == RCH_RESULT_INVALID_ARGUMENT,
                 "duplicate element IDs must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &overcrop, 1U) == RCH_RESULT_INVALID_ARGUMENT,
                 "over-crop must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &not_finite, 1U) == RCH_RESULT_INVALID_ARGUMENT,
                 "non-finite geometry must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &absurd, 1U) == RCH_RESULT_INVALID_ARGUMENT,
                 "absurd normalized coordinates must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &unsupported_rotation, 1U)
                   == RCH_RESULT_INVALID_ARGUMENT,
                 "out-of-range rotation must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &negative_size, 1U)
                   == RCH_RESULT_INVALID_ARGUMENT,
                 "negative size must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &invalid_boolean, 1U)
                   == RCH_RESULT_INVALID_ARGUMENT,
                 "non-boolean flags must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &invalid_fit, 1U)
                   == RCH_RESULT_INVALID_ARGUMENT,
                 "unknown fit mode must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, &unknown_camera, 1U)
                   == RCH_RESULT_NOT_CONFIGURED,
                 "unknown logical camera ID must be rejected")
      || !Expect(rch_view_apply_camera_scene(view, nullptr, RCH_VIEW_MAX_SCENE_ELEMENTS + 1U)
                   == RCH_RESULT_INVALID_ARGUMENT,
                 "absurd element count must be rejected")
      || !Expect(SceneInputCanaryIsPreserved(view, "scene-red"),
                 "scene input caller-size canary must remain unchanged")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  auto geometry = MakeElement("geometry", "scene-red", 0.25, 0.25, 0.25, 0.25);
  if (!Expect(ApplyAndWait(view, &geometry, 1U), "positioned scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto geometry_inside = Sample(view, 600, 400, ok);
  const auto geometry_outside = Sample(view, 1200, 700, ok);
  if (!Expect(ok && MostlyRed(geometry_inside), "positioned/scaled element must fill its rectangle")
      || !Expect(MostlyBlack(geometry_outside), "positioned element must not draw outside its rectangle")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  auto asymmetric = MakeElement("asymmetric", "scene-asymmetric");
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "asymmetric baseline must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto baseline_left = Sample(view, 100, 100, ok);
  const auto baseline_right = Sample(view, 1819, 100, ok);
  const auto baseline_top = Sample(view, 300, 100, ok);
  const auto baseline_bottom = Sample(view, 300, 979, ok);
  const auto baseline_ninety_source = Sample(view, 690, 540, ok);
  const auto baseline_crop = Sample(view, 528, 100, ok);

  asymmetric.flip_horizontal = 1U;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "horizontal flip scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto flipped_left = Sample(view, 100, 100, ok);
  const auto flipped_right = Sample(view, 1819, 100, ok);
  if (!Expect(ok && PixelsNear(flipped_left, baseline_right),
              "horizontal flip must mirror the right source region to the left")
      || !Expect(PixelsNear(flipped_right, baseline_left),
                 "horizontal flip must mirror the left source region to the right")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  asymmetric.flip_horizontal = 0U;
  asymmetric.flip_vertical = 1U;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "vertical flip scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto flipped_top = Sample(view, 300, 100, ok);
  const auto flipped_bottom = Sample(view, 300, 979, ok);
  if (!Expect(ok && PixelsNear(flipped_top, baseline_bottom),
              "vertical flip must mirror the bottom source region to the top")
      || !Expect(PixelsNear(flipped_bottom, baseline_top),
                 "vertical flip must mirror the top source region to the bottom")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  asymmetric.flip_vertical = 0U;
  asymmetric.crop_left = 0.25;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "cropped scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto cropped_left = Sample(view, 64, 100, ok);
  if (!Expect(ok && PixelsNear(cropped_left, baseline_crop, 12),
              "left crop must remap the retained source region")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  asymmetric.crop_left = 0.0;
  asymmetric.rotation_degrees = 90.0;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "90-degree scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto rotated_ninety = Sample(view, 960, 270, ok);
  asymmetric.rotation_degrees = 0.0;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "baseline must restore after 90-degree scene")
      || !Expect(ok && PixelsNear(rotated_ninety, baseline_ninety_source, 12),
                 "90-degree rotation must map the expected source region")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  asymmetric.rotation_degrees = 180.0;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "180-degree scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto rotated_corner = Sample(view, 1819, 979, ok);
  if (!Expect(ok && PixelsNear(rotated_corner, baseline_left, 16),
              "180-degree rotation must map the opposite source corner")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  asymmetric.rotation_degrees = 45.0;
  if (!Expect(ApplyAndWait(view, &asymmetric, 1U), "arbitrary-angle scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto rotated_outside = Sample(view, 20, 20, ok);
  if (!Expect(ok && MostlyBlack(rotated_outside),
              "arbitrary rotation must clip pixels outside the rotated element")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  auto contained = MakeElement("contained", "scene-red", 0.25, 0.0, 0.25, 1.0);
  contained.fit_mode = RCH_VIEW_CAMERA_FIT_CONTAIN;
  if (!Expect(ApplyAndWait(view, &contained, 1U), "contain-fit scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto contain_centre = Sample(view, 720, 540, ok);
  const auto contain_bar = Sample(view, 720, 100, ok);
  if (!Expect(ok && MostlyRed(contain_centre), "contain fit must preserve visible source content")
      || !Expect(MostlyBlack(contain_bar), "contain fit must preserve aspect ratio with empty bars")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  auto off_canvas = MakeElement("off-canvas", "scene-red", -0.5, 0.0, 1.0, 1.0);
  if (!Expect(ApplyAndWait(view, &off_canvas, 1U), "off-canvas scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto off_canvas_visible = Sample(view, 100, 540, ok);
  const auto off_canvas_clipped = Sample(view, 1500, 540, ok);
  if (!Expect(ok && MostlyRed(off_canvas_visible), "visible off-canvas portion must render")
      || !Expect(MostlyBlack(off_canvas_clipped), "off-canvas portion must clip safely")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  std::array<rch_view_camera_element_v1, 2> layers{
    MakeElement("a-red", "scene-red", 0.0, 0.0, 1.0, 1.0, 5),
    MakeElement("z-green", "scene-green", 0.25, 0.25, 0.5, 0.5, 5),
  };
  if (!Expect(ApplyAndWait(
                view,
                layers.data(),
                static_cast<std::uint32_t>(layers.size())),
              "overlap scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto overlap = Sample(view, 960, 540, ok);
  if (!Expect(ok && MostlyGreen(overlap),
              "equal-z overlap must use deterministic element-ID tie breaking")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  std::swap(layers[0], layers[1]);
  if (!Expect(ApplyAndWait(
                view,
                layers.data(),
                static_cast<std::uint32_t>(layers.size())),
              "reordered equal-z scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto stable_overlap = Sample(view, 960, 540, ok);
  if (!Expect(ok && MostlyGreen(stable_overlap),
              "equal-z result must not depend on caller or dictionary order")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  for (auto& layer : layers) {
    if (std::strcmp(layer.element_id_utf8, "z-green") == 0) {
      layer.visible = 0U;
    }
  }
  if (!Expect(ApplyAndWait(
                view,
                layers.data(),
                static_cast<std::uint32_t>(layers.size())),
              "hidden layer scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto hidden_overlap = Sample(view, 960, 540, ok);
  if (!Expect(ok && MostlyRed(hidden_overlap), "hidden element must be omitted from composition")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  for (auto& layer : layers) {
    if (std::strcmp(layer.element_id_utf8, "z-green") == 0) {
      layer.visible = 1U;
      layer.enabled = 0U;
    }
  }
  if (!Expect(ApplyAndWait(
                view,
                layers.data(),
                static_cast<std::uint32_t>(layers.size())),
              "disabled layer scene must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  const auto disabled_overlap = Sample(view, 960, 540, ok);
  if (!Expect(ok && MostlyRed(disabled_overlap), "disabled element must be omitted from composition")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  std::array<rch_view_camera_element_v1, 2> reused{
    MakeElement("reuse-left", "scene-red", 0.0, 0.0, 0.5, 1.0),
    MakeElement("reuse-right", "scene-red", 0.5, 0.0, 0.5, 1.0),
  };
  if (!Expect(ApplyAndWait(
                view,
                reused.data(),
                static_cast<std::uint32_t>(reused.size())),
              "one camera reused in two elements must apply")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  rch_camera_status_v1 camera_status{};
  camera_status.struct_size = sizeof(camera_status);
  camera_status.struct_version = RCH_CAMERA_STATUS_VERSION;
  if (!Expect(rch_camera_get_status_by_id(engine, "scene-red", &camera_status) == RCH_RESULT_OK,
              "reused camera status must be queryable")
      || !Expect(camera_status.active_rtsp_session_count == 1U,
                 "two elements must retain one RTSP session")
      || !Expect(camera_status.active_decoder_count == 1U,
                 "two elements must retain one decoder")) {
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_view_handle second_view = nullptr;
  auto second_element = MakeElement("second-view-red", "scene-red");
  if (!Expect(rch_view_create(engine, "scene-second-view", &second_view) == RCH_RESULT_OK,
              "second View must be created")
      || !Expect(ApplyAndWait(second_view, &second_element, 1U),
                 "same camera must apply to a second View")) {
    if (second_view != nullptr) {
      rch_view_destroy(second_view);
    }
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  camera_status.struct_size = sizeof(camera_status);
  camera_status.struct_version = RCH_CAMERA_STATUS_VERSION;
  if (!Expect(rch_camera_get_status_by_id(engine, "scene-red", &camera_status) == RCH_RESULT_OK,
              "cross-View camera status must be queryable")
      || !Expect(camera_status.active_rtsp_session_count == 1U,
                 "camera reused across Views must retain one RTSP session")
      || !Expect(camera_status.active_decoder_count == 1U,
                 "camera reused across Views must retain one decoder")) {
    rch_view_destroy(second_view);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  rch_ndi_sender_handle sender = nullptr;
  if (!Expect(rch_ndi_sender_create(view, "ROBOCAM - Gate6A Transform", &sender) == RCH_RESULT_OK,
              "NDI sender must consume the transformed View")
      || !Expect(sender != nullptr, "NDI sender handle must be returned")
      || !Expect(rch_ndi_sender_start(sender) == RCH_RESULT_OK,
                 "NDI sender must start from the transformed View")
      || !Expect(WaitForSenderFrame(sender),
                 "NDI sender must publish the transformed View's composed sequence")) {
    rch_view_destroy(second_view);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  auto outage_element = MakeElement("outage-transform", "scene-red", 0.1, 0.1, 0.6, 0.6);
  outage_element.crop_left = 0.1;
  outage_element.flip_horizontal = 1U;
  outage_element.rotation_degrees = 15.0;
  if (!Expect(ApplyAndWait(view, &outage_element, 1U), "transformed outage scene must apply")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(second_view);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  red_fixture.Stop();
  const auto outage_sequence = ViewStatus(view).latest_composed_frame_sequence;
  outage_element.rotation_degrees = 25.0;
  if (!Expect(WaitForFrozenElement(view), "transformed source outage must retain last-good frame")
      || !Expect(WaitForNewComposition(view, outage_sequence),
                 "scene must continue composing during transformed source outage")
      || !Expect(ApplyAndWait(view, &outage_element, 1U),
                 "transform edit during outage must preserve the bounded last-good frame")
      || !Expect(WaitForFrozenElement(view),
                 "edited transformed element must remain frozen during outage")
      || !Expect(red_fixture.Start("red"), "red fixture must restart on the same endpoint")
      || !Expect(WaitForReceiving(engine, "scene-red"),
                 "transformed source must recover without scene replacement")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(second_view);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }
  rch_engine_diagnostics_v1 diagnostics{};
  diagnostics.struct_size = sizeof(diagnostics);
  diagnostics.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  if (!Expect(rch_engine_get_diagnostics(engine, &diagnostics) == RCH_RESULT_OK,
              "ownership diagnostics must remain available")
      || !Expect(diagnostics.active_rtsp_session_total == 3U,
                 "scene/View/NDI fan-out must not add RTSP sessions")
      || !Expect(diagnostics.active_decoder_total == 3U,
                 "scene/View/NDI fan-out must not add decoders")
      || !Expect(diagnostics.view_count == 2U,
                 "two scene consumers must still own exactly two View compositors")) {
    rch_ndi_sender_destroy(sender);
    rch_view_destroy(second_view);
    rch_view_destroy(view);
    rch_engine_destroy(engine);
    return 1;
  }

  if (!Expect(rch_ndi_sender_destroy(sender) == RCH_RESULT_OK, "sender must destroy cleanly")
      || !Expect(rch_view_destroy(second_view) == RCH_RESULT_OK, "second View must destroy cleanly")
      || !Expect(rch_view_destroy(view) == RCH_RESULT_OK, "scene View must destroy cleanly")
      || !Expect(rch_engine_destroy(engine) == RCH_RESULT_OK, "engine must destroy cleanly")) {
    return 1;
  }

  return 0;
}
