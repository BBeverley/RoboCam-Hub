#include "robocamhub_native.h"

#include <array>
#include <chrono>
#include <cstdint>
#include <filesystem>
#include <fstream>
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

rch_view_scene_element_v1 Element(
  const char* id,
  rch_view_scene_element_kind kind,
  double x = 0.0,
  double y = 0.0,
  double width = 1.0,
  double height = 1.0,
  std::int32_t z = 0)
{
  rch_view_scene_element_v1 element{};
  element.struct_size = sizeof(element);
  element.struct_version = RCH_VIEW_SCENE_ELEMENT_VERSION;
  element.kind = kind;
  element.element_id_utf8 = id;
  element.x = x;
  element.y = y;
  element.width = width;
  element.height = height;
  element.z_order = z;
  element.fit_mode = RCH_VIEW_CAMERA_FIT_STRETCH;
  element.visible = 1U;
  element.enabled = 1U;
  element.opacity = 1.0;
  return element;
}

bool WaitAndSample(
  rch_view_handle view,
  std::uint32_t x,
  std::uint32_t y,
  std::array<std::uint8_t, 4>& color)
{
  for (int attempt = 0; attempt < 100; ++attempt) {
    rch_view_frame_lease_handle lease = nullptr;
    if (rch_view_acquire_latest_frame(view, &lease) == RCH_RESULT_OK && lease != nullptr) {
      const auto result = rch_view_frame_lease_sample_rgba(
        lease, x, y, &color[0], &color[1], &color[2], &color[3]);
      (void)rch_view_frame_lease_destroy(lease);
      if (result == RCH_RESULT_OK) {
        return true;
      }
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
  return false;
}

void WaitForRender(rch_view_handle view)
{
  rch_view_status_v1 baseline{};
  baseline.struct_size = sizeof(baseline);
  baseline.struct_version = RCH_VIEW_STATUS_VERSION;
  if (rch_view_get_status(view, &baseline) != RCH_RESULT_OK) {
    return;
  }
  for (int attempt = 0; attempt < 250; ++attempt) {
    rch_view_status_v1 current{};
    current.struct_size = sizeof(current);
    current.struct_version = RCH_VIEW_STATUS_VERSION;
    if (rch_view_get_status(view, &current) == RCH_RESULT_OK
        && current.latest_composed_frame_sequence >= baseline.latest_composed_frame_sequence + 2U) {
      return;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
  }
}

template <std::size_t Size>
void WriteFixture(const std::filesystem::path& path, const std::array<std::uint8_t, Size>& bytes)
{
  std::ofstream output(path, std::ios::binary);
  output.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
}

void WriteBase64Fixture(const std::filesystem::path& path, const std::string& encoded)
{
  static const std::string alphabet =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  std::ofstream output(path, std::ios::binary);
  std::uint32_t accumulator = 0U;
  int bits = 0;
  for (const char value : encoded) {
    if (value == '=') {
      break;
    }
    const auto index = alphabet.find(value);
    if (index == std::string::npos) {
      continue;
    }
    accumulator = (accumulator << 6U) | static_cast<std::uint32_t>(index);
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      output.put(static_cast<char>((accumulator >> static_cast<unsigned>(bits)) & UINT32_C(255)));
    }
  }
}

constexpr std::array<std::uint8_t, 97> kAlphaPng{
  0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,0x00,0x00,0x00,0x0d,
  0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x02,0x00,0x00,0x00,0x02,
  0x08,0x06,0x00,0x00,0x00,0x72,0xb6,0x0d,0x24,0x00,0x00,0x00,
  0x09,0x70,0x48,0x59,0x73,0x00,0x00,0x00,0x01,0x00,0x00,0x00,
  0x01,0x00,0x4f,0x25,0xc4,0xd6,0x00,0x00,0x00,0x13,0x49,0x44,
  0x41,0x54,0x78,0x9c,0x63,0xfc,0xcf,0xc0,0x50,0xcf,0x00,0x04,
  0x2c,0x0c,0x50,0x00,0x00,0x18,0x29,0x01,0x84,0x12,0x10,0x8d,
  0xc1,0x00,0x00,0x00,0x00,0x49,0x45,0x4e,0x44,0xae,0x42,0x60,
  0x82
};

}  // namespace

int main()
{
  rch_engine_handle engine = nullptr;
  rch_view_handle view = nullptr;
  if (!Expect(rch_engine_create(&engine) == RCH_RESULT_OK, "engine create")
      || !Expect(rch_view_create(engine, "visual-view", &view) == RCH_RESULT_OK, "view create")) {
    return 1;
  }

  bool passed = true;
  auto background = Element("background", RCH_VIEW_SCENE_ELEMENT_RECTANGLE);
  background.primary_rgba = UINT32_C(0x0000FFFF);
  auto foreground = Element("foreground", RCH_VIEW_SCENE_ELEMENT_RECTANGLE, 0.25, 0.25, 0.5, 0.5, 1);
  foreground.primary_rgba = UINT32_C(0xFF0000FF);
  foreground.secondary_rgba = UINT32_C(0x00FF00FF);
  foreground.secondary_enabled = 1U;
  foreground.stroke_width = 8.0;
  std::array scene{background, foreground};
  passed &= Expect(rch_view_apply_scene(view, scene.data(), scene.size()) == RCH_RESULT_OK, "shape scene applies");
  WaitForRender(view);
  std::array<std::uint8_t, 4> color{};
  passed &= Expect(WaitAndSample(view, 960, 540, color) && color[0] > 240 && color[1] < 10, "rectangle fill renders");
  passed &= Expect(WaitAndSample(view, 481, 271, color) && color[1] > 240, "rectangle outline renders");

  auto tie_a = Element("a", RCH_VIEW_SCENE_ELEMENT_RECTANGLE);
  tie_a.primary_rgba = UINT32_C(0xFF0000FF);
  auto tie_b = Element("b", RCH_VIEW_SCENE_ELEMENT_RECTANGLE);
  tie_b.primary_rgba = UINT32_C(0x00FF00FF);
  std::array tie_scene{tie_b, tie_a};
  passed &= Expect(rch_view_apply_scene(view, tie_scene.data(), tie_scene.size()) == RCH_RESULT_OK, "tie scene applies");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 960, 540, color) && color[1] > 240, "element ID deterministically breaks equal-Z ties");

  auto rotated = Element("rotated", RCH_VIEW_SCENE_ELEMENT_RECTANGLE, 0.3, 0.45, 0.4, 0.1);
  rotated.primary_rgba = UINT32_C(0xFFFFFFFF);
  rotated.rotation_degrees = 90.0;
  passed &= Expect(rch_view_apply_scene(view, &rotated, 1) == RCH_RESULT_OK, "rotated shape applies");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 960, 700, color) && color[0] > 240, "shape rotation affects native pixels");
  rotated.visible = 0U;
  passed &= Expect(rch_view_apply_scene(view, &rotated, 1) == RCH_RESULT_OK, "hidden shape applies");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 960, 700, color) && color[0] < 10, "shape visibility removes native pixels");

  auto frame = Element("frame", RCH_VIEW_SCENE_ELEMENT_FRAME, 0.1, 0.1, 0.8, 0.8, 2);
  frame.primary_rgba = UINT32_C(0xFFFFFFFF);
  frame.stroke_width = 12.0;
  std::array frame_scene{background, frame};
  passed &= Expect(rch_view_apply_scene(view, frame_scene.data(), frame_scene.size()) == RCH_RESULT_OK, "frame scene applies");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 960, 540, color) && color[2] > 240 && color[0] < 10, "frame centre stays transparent");
  passed &= Expect(WaitAndSample(view, 193, 109, color) && color[0] > 240, "frame border renders");
  frame.visible = 0U;
  frame_scene[1] = frame;
  passed &= Expect(rch_view_apply_scene(view, frame_scene.data(), frame_scene.size()) == RCH_RESULT_OK, "hidden frame applies");
  WaitForRender(view);
  passed &= Expect(
    WaitAndSample(view, 193, 109, color) && color[0] < 10U && color[2] > 240U,
    "frame visibility removes native pixels");

  auto text = Element("title-\xE2\x9C\x93", RCH_VIEW_SCENE_ELEMENT_TEXT, 0.1, 0.1, 0.8, 0.3, 3);
  text.text_utf8 = "RoboCam \xE2\x9C\x93 \xE6\x97\xA5\xE6\x9C\xAC";
  text.font_family_utf8 = "Definitely Missing Font";
  text.font_size = 80.0;
  text.primary_rgba = UINT32_C(0xFFFFFFFF);
  text.text_alignment = RCH_VIEW_TEXT_ALIGN_CENTER;
  text.text_weight = RCH_VIEW_TEXT_WEIGHT_BOLD;
  text.text_style = RCH_VIEW_TEXT_STYLE_NORMAL;
  passed &= Expect(rch_view_apply_scene(view, &text, 1) == RCH_RESULT_OK, "UTF-8 text and fallback font apply");
  WaitForRender(view);
  bool found_text_pixel = false;
  std::uint32_t text_pixel_x = 0U;
  std::uint32_t text_pixel_y = 0U;
  for (std::uint32_t y = 110; y < 400 && !found_text_pixel; y += 8) {
    for (std::uint32_t x = 200; x < 1720; x += 8) {
      if (WaitAndSample(view, x, y, color) && color[0] > 32U) {
        found_text_pixel = true;
        text_pixel_x = x;
        text_pixel_y = y;
        break;
      }
    }
  }
  passed &= Expect(found_text_pixel, "text produces visible native pixels");
  text.visible = 0U;
  passed &= Expect(rch_view_apply_scene(view, &text, 1) == RCH_RESULT_OK, "hidden text applies");
  WaitForRender(view);
  passed &= Expect(
    found_text_pixel && WaitAndSample(view, text_pixel_x, text_pixel_y, color) && color[0] < 10U,
    "text visibility removes native pixels");
  text.visible = 1U;

  const auto fixture_root = std::filesystem::temp_directory_path() / "robocamhub-gate6d";
  std::filesystem::create_directories(fixture_root);
  const auto png_path = fixture_root / "alpha.png";
  const auto jpeg_path = fixture_root / "green.jpg";
  WriteFixture(png_path, kAlphaPng);
  WriteBase64Fixture(
    jpeg_path,
    "/9j/4AAQSkZJRgABAgAAAQABAAD//gAPTGF2YzYzLjEuMTAxAP/bAEMACAQEBAQEBQUFBQUFBgYGBgYGBgYGBgYGBgcHBwgICAcHBwYGBwcICAgICQkJCAgICAkJCgoKDAwLCw4ODhERFP/EAEsAAQEAAAAAAAAAAAAAAAAAAAAGAQEAAAAAAAAAAAAAAAAAAAAGEAEAAAAAAAAAAAAAAAAAAAAAEQEAAAAAAAAAAAAAAAAAAAAA/8AAEQgAAgACAwEiAAIRAAMRAP/aAAwDAQACEQMRAD8AiwBQJf/Z");
  const auto png_string = png_path.string();
  const auto jpeg_string = jpeg_path.string();
  auto image = Element("logo", RCH_VIEW_SCENE_ELEMENT_IMAGE, 0.0, 0.0, 1.0, 1.0, 1);
  image.image_asset_id_utf8 = "asset-logo";
  image.image_source_utf8 = png_string.c_str();
  std::array image_scene{background, image};
  image.fit_mode = RCH_VIEW_CAMERA_FIT_CONTAIN;
  image_scene[1] = image;
  passed &= Expect(rch_view_apply_scene(view, image_scene.data(), image_scene.size()) == RCH_RESULT_OK, "PNG image applies");
  WaitForRender(view);
  (void)WaitAndSample(view, 960, 540, color);
  if (!(color[0] > 100 && color[2] > 100)) {
    std::cerr << "PNG blended pixel: " << static_cast<int>(color[0]) << ','
              << static_cast<int>(color[1]) << ',' << static_cast<int>(color[2]) << ','
              << static_cast<int>(color[3]) << '\n';
  }
  passed &= Expect(WaitAndSample(view, 960, 540, color) && color[0] > 100 && color[2] > 100, "PNG alpha blends over lower layer");
  passed &= Expect(WaitAndSample(view, 10, 540, color) && color[0] < 10 && color[2] > 240, "image Contain preserves transparent letterbox");
  image.fit_mode = RCH_VIEW_CAMERA_FIT_COVER;
  image_scene[1] = image;
  passed &= Expect(rch_view_apply_scene(view, image_scene.data(), image_scene.size()) == RCH_RESULT_OK, "image Cover applies");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 10, 540, color) && color[0] > 100 && color[2] > 100, "image Cover fills destination");
  image.visible = 0U;
  image_scene[1] = image;
  passed &= Expect(rch_view_apply_scene(view, image_scene.data(), image_scene.size()) == RCH_RESULT_OK, "hidden image applies");
  WaitForRender(view);
  passed &= Expect(
    WaitAndSample(view, 960, 540, color) && color[0] < 10U && color[2] > 240U,
    "image visibility removes native pixels");
  image.visible = 1U;
  image.rotation_degrees = 90.0;
  image.x = 0.3;
  image.y = 0.45;
  image.width = 0.4;
  image.height = 0.1;
  image_scene[1] = image;
  passed &= Expect(rch_view_apply_scene(view, image_scene.data(), image_scene.size()) == RCH_RESULT_OK, "rotated image applies");
  WaitForRender(view);
  passed &= Expect(
    WaitAndSample(view, 960, 700, color) && color[0] > 100U && color[2] > 100U,
    "image rotation affects native pixels");
  std::filesystem::remove(png_path);
  std::this_thread::sleep_for(std::chrono::milliseconds(100));
  passed &= Expect(WaitAndSample(view, 960, 700, color) && color[0] > 100 && color[2] > 100, "image remains retained after source removal");

  image.x = 0.0;
  image.y = 0.0;
  image.width = 1.0;
  image.height = 1.0;
  image.rotation_degrees = 0.0;
  image.image_source_utf8 = jpeg_string.c_str();
  const auto jpeg_result = rch_view_apply_scene(view, &image, 1);
  if (jpeg_result != RCH_RESULT_OK) {
    std::cerr << "JPEG apply result: " << jpeg_result << '\n';
  }
  passed &= Expect(jpeg_result == RCH_RESULT_OK, "JPEG image applies");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 960, 540, color) && color[1] > color[0] && color[1] > color[2], "JPEG pixels render");

  auto stable = Element("stable", RCH_VIEW_SCENE_ELEMENT_RECTANGLE);
  stable.primary_rgba = UINT32_C(0xFF00FFFF);
  passed &= Expect(rch_view_apply_scene(view, &stable, 1) == RCH_RESULT_OK, "stable scene applies");
  WaitForRender(view);
  auto missing = Element("missing", RCH_VIEW_SCENE_ELEMENT_IMAGE);
  missing.image_asset_id_utf8 = "missing-asset";
  missing.image_source_utf8 = "/path/that/does/not/exist.png";
  passed &= Expect(rch_view_apply_scene(view, &missing, 1) == RCH_RESULT_NOT_CONFIGURED, "missing image fails clearly");
  WaitForRender(view);
  passed &= Expect(WaitAndSample(view, 960, 540, color) && color[0] > 240 && color[2] > 240, "failed apply preserves prior scene");
  auto invalid_text = text;
  invalid_text.font_size = 0.0;
  std::array malformed_scene{background, invalid_text};
  passed &= Expect(
    rch_view_apply_scene(view, malformed_scene.data(), malformed_scene.size()) == RCH_RESULT_INVALID_ARGUMENT,
    "malformed text rejects complete scene");
  WaitForRender(view);
  passed &= Expect(
    WaitAndSample(view, 960, 540, color) && color[0] > 240U && color[2] > 240U,
    "malformed mixed scene preserves prior pixels");

  rch_camera_config_v1 camera_config{};
  camera_config.struct_size = sizeof(camera_config);
  camera_config.struct_version = RCH_CAMERA_CONFIG_VERSION;
  camera_config.camera_id_utf8 = "camera-1";
  camera_config.rtsp_url_utf8 = "rtsp://127.0.0.1:1/profile2/media.smp";
  camera_config.connect_timeout_ms = 250U;
  passed &= Expect(rch_camera_add(engine, &camera_config) == RCH_RESULT_OK, "camera configuration for atomic test");
  auto camera = Element("camera", RCH_VIEW_SCENE_ELEMENT_CAMERA);
  camera.camera_id_utf8 = "camera-1";
  passed &= Expect(rch_view_apply_scene(view, &camera, 1) == RCH_RESULT_OK, "camera-only mixed scene applies");
  text.z_order = 1;
  std::array camera_text_scene{camera, text};
  passed &= Expect(
    rch_view_apply_scene(view, camera_text_scene.data(), camera_text_scene.size()) == RCH_RESULT_OK,
    "text and camera share one ordered scene");
  WaitForRender(view);
  passed &= Expect(
    found_text_pixel && WaitAndSample(view, text_pixel_x, text_pixel_y, color) && color[0] > 32U,
    "text renders above a camera element by Z-order");
  passed &= Expect(rch_view_apply_scene(view, &camera, 1) == RCH_RESULT_OK, "camera scene restored for rollback check");
  std::array invalid_mixed{camera, missing};
  passed &= Expect(rch_view_apply_scene(view, invalid_mixed.data(), invalid_mixed.size()) == RCH_RESULT_NOT_CONFIGURED,
                   "camera plus missing image fails atomically");
  rch_view_status_v1 view_status{};
  view_status.struct_size = sizeof(view_status);
  view_status.struct_version = RCH_VIEW_STATUS_VERSION;
  passed &= Expect(rch_view_get_status(view, &view_status) == RCH_RESULT_OK
                   && view_status.bound_source_count == 1U,
                   "failed mixed apply preserves previous camera binding");

  struct ExtendedSceneElement final {
    rch_view_scene_element_v1 element;
    std::array<std::uint64_t, 2> future;
  };
  std::array extended{
    ExtendedSceneElement{stable, {UINT64_C(0x1122334455667788), UINT64_C(0x8877665544332211)}},
    ExtendedSceneElement{background, {UINT64_C(0xAABBCCDDEEFF0011), UINT64_C(0x1100FFEEDDCCBBAA)}},
  };
  extended[0].element.element_id_utf8 = "extended-a";
  extended[1].element.element_id_utf8 = "extended-b";
  extended[0].element.struct_size = sizeof(ExtendedSceneElement);
  extended[1].element.struct_size = sizeof(ExtendedSceneElement);
  passed &= Expect(
    rch_view_apply_scene(view, &extended[0].element, extended.size()) == RCH_RESULT_OK,
    "larger caller-sized scene element array applies with caller stride");
  passed &= Expect(
    extended[0].future[0] == UINT64_C(0x1122334455667788)
      && extended[0].future[1] == UINT64_C(0x8877665544332211)
      && extended[1].future[0] == UINT64_C(0xAABBCCDDEEFF0011)
      && extended[1].future[1] == UINT64_C(0x1100FFEEDDCCBBAA),
    "scene ABI ignores and preserves future caller bytes");

  rch_engine_diagnostics_v1 diagnostics{};
  diagnostics.struct_size = sizeof(diagnostics);
  diagnostics.struct_version = RCH_ENGINE_DIAGNOSTICS_VERSION;
  passed &= Expect(rch_engine_get_diagnostics(engine, &diagnostics) == RCH_RESULT_OK, "diagnostics query");
  passed &= Expect(diagnostics.active_rtsp_session_total == 0U && diagnostics.active_decoder_total == 0U,
                   "visual elements create no ingest ownership");
  passed &= Expect(diagnostics.view_count == 1U && diagnostics.total_bound_view_source_count == 0U,
                   "visual elements remain in the existing single compositor");

  std::filesystem::remove(jpeg_path);
  std::filesystem::remove(fixture_root);
  passed &= Expect(rch_view_destroy(view) == RCH_RESULT_OK, "view destroy");
  passed &= Expect(rch_engine_destroy(engine) == RCH_RESULT_OK, "engine destroy");
  return passed ? 0 : 1;
}
