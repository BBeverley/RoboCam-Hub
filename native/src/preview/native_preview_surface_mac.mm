#include "preview/native_preview_surface.h"

#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#include <dispatch/dispatch.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <new>

@interface RCHNativePreviewView : NSView {
@private
  robocamhub::preview::PreviewFrameSource _frameSource;
  NSTimer* _presentationTimer;
  NSSize _lastReportedSize;
}

- (instancetype)initWithFrameSource:(robocamhub::preview::PreviewFrameSource)source
                           targetFps:(std::uint32_t)targetFps;
- (void)invalidatePreview;

@end

@implementation RCHNativePreviewView

- (instancetype)initWithFrameSource:(robocamhub::preview::PreviewFrameSource)source
                           targetFps:(std::uint32_t)targetFps
{
  self = [super initWithFrame:NSMakeRect(0.0, 0.0, 1.0, 1.0)];
  if (self != nil) {
    _frameSource = source;
    _lastReportedSize = NSZeroSize;
    [self setAutoresizingMask:NSViewWidthSizable | NSViewHeightSizable];
    [self setWantsLayer:YES];
    const auto interval = 1.0 / static_cast<double>(std::max<std::uint32_t>(1U, targetFps));
    _presentationTimer = [NSTimer timerWithTimeInterval:interval
                                                target:self
                                              selector:@selector(presentationTick:)
                                              userInfo:nil
                                               repeats:YES];
    [[NSRunLoop mainRunLoop] addTimer:_presentationTimer forMode:NSRunLoopCommonModes];
  }
  return self;
}

- (BOOL)isFlipped
{
  return YES;
}

- (void)presentationTick:(NSTimer*)timer
{
  static_cast<void>(timer);
  [self setNeedsDisplay:YES];
}

- (void)setFrameSize:(NSSize)newSize
{
  [super setFrameSize:newSize];
  if (!NSEqualSizes(newSize, _lastReportedSize)) {
    _lastReportedSize = newSize;
    if (_frameSource.report_surface_recreated != nullptr) {
      _frameSource.report_surface_recreated(_frameSource.context);
    }
  }
}

- (void)drawRect:(NSRect)dirtyRect
{
  static_cast<void>(dirtyRect);
  CGContextRef context = [[NSGraphicsContext currentContext] CGContext];
  if (context == nullptr) {
    return;
  }

  const NSRect bounds = [self bounds];
  CGContextSetRGBFillColor(context, 0.0, 0.0, 0.0, 1.0);
  CGContextFillRect(context, NSRectToCGRect(bounds));

  auto lease = _frameSource.acquire_latest == nullptr
    ? robocamhub::frames::LatestFrameLease{}
    : _frameSource.acquire_latest(_frameSource.context);
  if (!lease.has_frame || lease.sample() == nullptr || lease.width == 0U || lease.height == 0U) {
    if (_frameSource.report_waiting != nullptr) {
      _frameSource.report_waiting(_frameSource.context);
    }
    return;
  }

  GstBuffer* buffer = gst_sample_get_buffer(lease.sample());
  GstMapInfo map{};
  if (buffer == nullptr || !gst_buffer_map(buffer, &map, GST_MAP_READ)) {
    if (_frameSource.report_error != nullptr) {
      _frameSource.report_error(_frameSource.context, RCH_RESULT_INTERNAL_ERROR);
    }
    return;
  }

  const auto stride = static_cast<std::size_t>(lease.width) * 4U;
  const auto required_size = stride * static_cast<std::size_t>(lease.height);
  if (map.size < required_size) {
    gst_buffer_unmap(buffer, &map);
    if (_frameSource.report_error != nullptr) {
      _frameSource.report_error(_frameSource.context, RCH_RESULT_INTERNAL_ERROR);
    }
    return;
  }

  CGColorSpaceRef color_space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
  CGDataProviderRef provider = CGDataProviderCreateWithData(nullptr, map.data, required_size, nullptr);
  CGImageRef image = nullptr;
  if (color_space != nullptr && provider != nullptr) {
    image = CGImageCreate(
      lease.width,
      lease.height,
      8U,
      32U,
      stride,
      color_space,
      static_cast<CGBitmapInfo>(
        static_cast<std::uint32_t>(kCGImageAlphaLast)
        | static_cast<std::uint32_t>(kCGBitmapByteOrder32Big)),
      provider,
      nullptr,
      false,
      kCGRenderingIntentDefault);
  }

  if (image == nullptr) {
    if (_frameSource.report_error != nullptr) {
      _frameSource.report_error(_frameSource.context, RCH_RESULT_INTERNAL_ERROR);
    }
  } else {
    const double scale = std::min(
      static_cast<double>(bounds.size.width) / static_cast<double>(lease.width),
      static_cast<double>(bounds.size.height) / static_cast<double>(lease.height));
    const auto destination_width = static_cast<CGFloat>(static_cast<double>(lease.width) * scale);
    const auto destination_height = static_cast<CGFloat>(static_cast<double>(lease.height) * scale);
    const auto destination_x = (bounds.size.width - destination_width) / 2.0;
    const auto destination_y = (bounds.size.height - destination_height) / 2.0;

    CGContextSaveGState(context);
    CGContextTranslateCTM(context, destination_x, destination_y + destination_height);
    CGContextScaleCTM(
      context,
      destination_width / static_cast<CGFloat>(lease.width),
      -destination_height / static_cast<CGFloat>(lease.height));
    CGContextDrawImage(
      context,
      CGRectMake(0.0, 0.0, static_cast<CGFloat>(lease.width), static_cast<CGFloat>(lease.height)),
      image);
    CGContextRestoreGState(context);

    if (_frameSource.report_presented != nullptr) {
      _frameSource.report_presented(_frameSource.context, lease.sequence, lease.age_ms);
    }
  }

  if (image != nullptr) {
    CGImageRelease(image);
  }
  if (provider != nullptr) {
    CGDataProviderRelease(provider);
  }
  if (color_space != nullptr) {
    CGColorSpaceRelease(color_space);
  }
  gst_buffer_unmap(buffer, &map);
}

- (void)invalidatePreview
{
  [_presentationTimer invalidate];
  _presentationTimer = nil;
  _frameSource = {};
}

- (void)dealloc
{
  [self invalidatePreview];
  [super dealloc];
}

@end

namespace robocamhub::preview {
namespace {

void DestroyPreviewView(void* context)
{
  auto* view = static_cast<RCHNativePreviewView*>(context);
  [view invalidatePreview];
  [view removeFromSuperview];
  [view release];
}

class MacPreviewSurface final : public NativePreviewSurface {
public:
  MacPreviewSurface(NSView* host, std::uint32_t target_fps, PreviewFrameSource source) noexcept
  {
    if (host == nil || ![NSThread isMainThread]) {
      return;
    }
    view_ = [[RCHNativePreviewView alloc] initWithFrameSource:source targetFps:target_fps];
    if (view_ == nil) {
      return;
    }
    [view_ setFrame:[host bounds]];
    [host addSubview:view_];
  }

  ~MacPreviewSurface() override
  {
    if (view_ == nil) {
      return;
    }
    if ([NSThread isMainThread]) {
      DestroyPreviewView(view_);
    } else {
      dispatch_sync_f(dispatch_get_main_queue(), view_, DestroyPreviewView);
    }
    view_ = nil;
  }

  [[nodiscard]] bool IsValid() const noexcept
  {
    return view_ != nil;
  }

private:
  RCHNativePreviewView* view_{nil};
};

}  // namespace

std::unique_ptr<NativePreviewSurface> CreateNativePreviewSurface(
  rch_view_preview_platform platform,
  std::uint64_t host_native_handle,
  std::uint32_t target_fps,
  PreviewFrameSource source) noexcept
{
  if (platform != RCH_VIEW_PREVIEW_PLATFORM_MACOS_NSVIEW
      || host_native_handle == 0U
      || ![NSThread isMainThread]) {
    return nullptr;
  }

  auto surface = std::unique_ptr<MacPreviewSurface>(new (std::nothrow) MacPreviewSurface(
    reinterpret_cast<NSView*>(static_cast<std::uintptr_t>(host_native_handle)),
    target_fps,
    source));
  if (surface == nullptr || !surface->IsValid()) {
    return nullptr;
  }
  return surface;
}

}  // namespace robocamhub::preview
