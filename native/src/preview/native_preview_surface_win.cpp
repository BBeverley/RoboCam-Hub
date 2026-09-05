#include "preview/native_preview_surface.h"

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <new>

namespace robocamhub::preview {
namespace {

constexpr wchar_t kPreviewWindowClass[] = L"RoboCamHubNativeViewPreview";
constexpr UINT_PTR kPresentationTimer = 1U;
constexpr UINT kDestroyPreviewMessage = WM_APP + 0x31U;

class WindowsPreviewSurface;
LRESULT CALLBACK PreviewWindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept;

ATOM EnsureWindowClass()
{
  static std::once_flag once;
  static ATOM atom = 0;
  std::call_once(once, [] {
    WNDCLASSEXW window_class{};
    window_class.cbSize = sizeof(window_class);
    window_class.style = CS_HREDRAW | CS_VREDRAW;
    window_class.lpfnWndProc = PreviewWindowProcedure;
    window_class.hInstance = GetModuleHandleW(nullptr);
    window_class.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    window_class.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
    window_class.lpszClassName = kPreviewWindowClass;
    atom = RegisterClassExW(&window_class);
    if (atom == 0 && GetLastError() == ERROR_CLASS_ALREADY_EXISTS) {
      atom = 1;
    }
  });
  return atom;
}

class WindowsPreviewSurface final : public NativePreviewSurface {
public:
  WindowsPreviewSurface(HWND parent, std::uint32_t target_fps, PreviewFrameSource source) noexcept
      : parent_(parent), source_(source)
  {
    if (parent_ == nullptr || !IsWindow(parent_) || EnsureWindowClass() == 0) {
      return;
    }

    window_ = CreateWindowExW(
      0,
      kPreviewWindowClass,
      L"",
      WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
      0,
      0,
      1,
      1,
      parent_,
      nullptr,
      GetModuleHandleW(nullptr),
      this);
    if (window_ == nullptr) {
      return;
    }

    const auto interval_ms = std::max<UINT>(1U, 1000U / std::max<std::uint32_t>(1U, target_fps));
    if (SetTimer(window_, kPresentationTimer, interval_ms, nullptr) == 0) {
      DestroyWindow(window_);
      window_ = nullptr;
      return;
    }
    ResizeToParent();
  }

  ~WindowsPreviewSurface() override
  {
    if (window_ == nullptr) {
      return;
    }
    if (!IsWindow(window_)) {
      window_ = nullptr;
      return;
    }

    DWORD window_thread_id = GetWindowThreadProcessId(window_, nullptr);
    if (window_thread_id == GetCurrentThreadId()) {
      DestroyOnWindowThread();
    } else {
      SendMessageW(window_, kDestroyPreviewMessage, 0, 0);
    }
  }

  [[nodiscard]] bool IsValid() const noexcept
  {
    return window_ != nullptr;
  }

  LRESULT WindowProcedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept
  {
    static_cast<void>(wparam);
    static_cast<void>(lparam);
    switch (message) {
      case WM_TIMER:
        if (wparam == kPresentationTimer) {
          ResizeToParent();
          InvalidateRect(window, nullptr, FALSE);
          return 0;
        }
        break;
      case WM_SIZE:
        if (source_.report_surface_recreated != nullptr) {
          source_.report_surface_recreated(source_.context);
        }
        return 0;
      case WM_ERASEBKGND:
        return 1;
      case WM_PAINT:
        Paint(window);
        return 0;
      case kDestroyPreviewMessage:
        DestroyOnWindowThread();
        return 0;
      case WM_NCDESTROY:
        SetWindowLongPtrW(window, GWLP_USERDATA, 0);
        break;
      default:
        break;
    }
    return DefWindowProcW(window, message, wparam, lparam);
  }

private:
  void DestroyOnWindowThread() noexcept
  {
    if (window_ != nullptr && IsWindow(window_)) {
      KillTimer(window_, kPresentationTimer);
      DestroyWindow(window_);
    }
    window_ = nullptr;
  }

  void ResizeToParent() noexcept
  {
    if (window_ == nullptr || parent_ == nullptr || !IsWindow(parent_)) {
      return;
    }
    RECT bounds{};
    if (GetClientRect(parent_, &bounds) == FALSE) {
      return;
    }
    const auto width = std::max<LONG>(1, bounds.right - bounds.left);
    const auto height = std::max<LONG>(1, bounds.bottom - bounds.top);
    if (width == last_width_ && height == last_height_) {
      return;
    }
    last_width_ = width;
    last_height_ = height;
    MoveWindow(window_, 0, 0, width, height, TRUE);
  }

  void Paint(HWND window) noexcept
  {
    PAINTSTRUCT paint{};
    HDC context = BeginPaint(window, &paint);
    if (context == nullptr) {
      return;
    }

    RECT client{};
    GetClientRect(window, &client);
    FillRect(context, &client, static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));

    auto lease = source_.acquire_latest == nullptr
      ? frames::LatestFrameLease{}
      : source_.acquire_latest(source_.context);
    if (!lease.has_frame || lease.sample() == nullptr || lease.width == 0U || lease.height == 0U) {
      if (source_.report_waiting != nullptr) {
        source_.report_waiting(source_.context);
      }
      EndPaint(window, &paint);
      return;
    }

    auto* buffer = gst_sample_get_buffer(lease.sample());
    GstMapInfo map{};
    if (buffer == nullptr || !gst_buffer_map(buffer, &map, GST_MAP_READ)) {
      if (source_.report_error != nullptr) {
        source_.report_error(source_.context, RCH_RESULT_INTERNAL_ERROR);
      }
      EndPaint(window, &paint);
      return;
    }

    const auto source_stride = static_cast<std::size_t>(lease.width) * 4U;
    const auto required_size = source_stride * static_cast<std::size_t>(lease.height);
    if (map.size < required_size) {
      gst_buffer_unmap(buffer, &map);
      if (source_.report_error != nullptr) {
        source_.report_error(source_.context, RCH_RESULT_INTERNAL_ERROR);
      }
      EndPaint(window, &paint);
      return;
    }

    const auto client_width = std::max<LONG>(1, client.right - client.left);
    const auto client_height = std::max<LONG>(1, client.bottom - client.top);
    const double scale = std::min(
      static_cast<double>(client_width) / static_cast<double>(lease.width),
      static_cast<double>(client_height) / static_cast<double>(lease.height));
    const auto destination_width = std::max<LONG>(1, static_cast<LONG>(lease.width * scale));
    const auto destination_height = std::max<LONG>(1, static_cast<LONG>(lease.height * scale));
    const auto destination_x = (client_width - destination_width) / 2;
    const auto destination_y = (client_height - destination_height) / 2;

    BITMAPV4HEADER header{};
    header.bV4Size = sizeof(header);
    header.bV4Width = static_cast<LONG>(lease.width);
    header.bV4Height = -static_cast<LONG>(lease.height);
    header.bV4Planes = 1;
    header.bV4BitCount = 32;
    header.bV4V4Compression = BI_BITFIELDS;
    header.bV4RedMask = 0x000000FFU;
    header.bV4GreenMask = 0x0000FF00U;
    header.bV4BlueMask = 0x00FF0000U;
    header.bV4AlphaMask = 0xFF000000U;
    header.bV4CSType = LCS_sRGB;

    SetStretchBltMode(context, COLORONCOLOR);
    const auto draw_result = StretchDIBits(
      context,
      destination_x,
      destination_y,
      destination_width,
      destination_height,
      0,
      0,
      static_cast<int>(lease.width),
      static_cast<int>(lease.height),
      map.data,
      reinterpret_cast<const BITMAPINFO*>(&header),
      DIB_RGB_COLORS,
      SRCCOPY);
    gst_buffer_unmap(buffer, &map);

    if (draw_result == GDI_ERROR) {
      if (source_.report_error != nullptr) {
        source_.report_error(source_.context, RCH_RESULT_INTERNAL_ERROR);
      }
    } else if (source_.report_presented != nullptr) {
      source_.report_presented(source_.context, lease.sequence, lease.age_ms);
    }
    EndPaint(window, &paint);
  }

  HWND parent_{nullptr};
  HWND window_{nullptr};
  PreviewFrameSource source_{};
  LONG last_width_{0};
  LONG last_height_{0};
};

LRESULT CALLBACK PreviewWindowProcedure(
  HWND window,
  UINT message,
  WPARAM wparam,
  LPARAM lparam) noexcept
{
  auto* surface = reinterpret_cast<WindowsPreviewSurface*>(
    GetWindowLongPtrW(window, GWLP_USERDATA));
  if (message == WM_NCCREATE) {
    const auto* create = reinterpret_cast<const CREATESTRUCTW*>(lparam);
    surface = static_cast<WindowsPreviewSurface*>(create->lpCreateParams);
    SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(surface));
  }
  if (surface != nullptr) {
    return surface->WindowProcedure(window, message, wparam, lparam);
  }
  return DefWindowProcW(window, message, wparam, lparam);
}

}  // namespace

std::unique_ptr<NativePreviewSurface> CreateNativePreviewSurface(
  rch_view_preview_platform platform,
  std::uint64_t host_native_handle,
  std::uint32_t target_fps,
  PreviewFrameSource source) noexcept
{
  if (platform != RCH_VIEW_PREVIEW_PLATFORM_WINDOWS_HWND || host_native_handle == 0U) {
    return nullptr;
  }
  auto surface = std::unique_ptr<WindowsPreviewSurface>(new (std::nothrow) WindowsPreviewSurface(
    reinterpret_cast<HWND>(static_cast<std::uintptr_t>(host_native_handle)),
    target_fps,
    source));
  if (surface == nullptr || !surface->IsValid()) {
    return nullptr;
  }
  return surface;
}

}  // namespace robocamhub::preview
