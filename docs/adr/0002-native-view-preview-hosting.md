# ADR 0002 — Native View Preview Hosting

## Status

Accepted for Gate 5C

## Context

The native Gate 3B compositor publishes one reference-counted, tightly packed
1920×1080 RGBA latest frame. Gate 4A NDI reads that frame on its own bounded
worker. Gate 5C must show the same composed View in Avalonia without creating a
second compositor, moving full frames through managed memory, or allowing local
presentation to back-pressure composition or NDI.

Avalonia 12.1.2 exposes `NativeControlHost` on both required platforms. Its
Windows host accepts an HWND and its macOS host accepts an NSView. The framework
positions and resizes the hosted native region with the Avalonia layout.

Avalonia also exposes lower-level GPU external-memory interop. The current
compositor, however, produces CPU RGBA rather than a shareable GPU texture.
Adopting that path now would require new platform GPU resources, synchronization
and effectively a compositor-output redesign. Gate 5C explicitly excludes a
silent GPU-compositor rewrite.

## Decision

Use an Avalonia `NativeControlHost` as a thin platform-host adapter and render
the existing native composed-frame lease into a native child surface:

```text
View compositor latest RGBA frame
        ├─ existing NDI sender worker
        └─ preview native paint path
             ├─ Windows HWND / GDI presentation
             └─ macOS NSView / Core Graphics presentation
                      ↓
              Avalonia NativeControlHost
```

The Avalonia control creates the framework's default native host and passes its
typed platform descriptor to the application/runtime boundary. A versioned,
additive C ABI creates an opaque preview attachment. NativeInterop owns that
handle through `SafeHandle`; Runtime owns the preview object; the application
ViewModel exposes only state and diagnostics.

The platform presenter acquires a reference-counted lease to the newest composed
frame only when the OS asks it to paint. It maps that lease read-only for the
duration of the synchronous native draw and releases it immediately afterward.
No full-frame payload or unmanaged media pointer crosses into managed code.

Windows uses a child HWND and `StretchDIBits` with explicit RGBA channel masks.
macOS uses an NSView and a transient `CGImage` backed directly by the mapped
native frame for the synchronous draw. RoboCam-Hub performs no application-level
full-frame copy or color conversion in either adapter. The operating system and
display compositor remain free to perform their normal presentation upload or
format work.

## Freshness and independence

The preview is timer-invalidated at a bounded local cadence and acquires only the
current `LatestFrame` lease. Invalidation coalesces when the UI is blocked or
minimized. The presenter never owns a frame queue and never drains missed frames;
sequence gaps are counted as skipped preview frames.

Preview paint does not share a blocking lock with the NDI sender. A slow or
blocked UI can delay only local presentation. View rendering and the NDI sender
continue to acquire the same native latest-frame state independently.

## Ownership and teardown

```text
ShowRuntime
  → ViewRuntime
      ├─ OutputRuntime
      └─ ViewPreviewRuntime
```

Normal shutdown detaches preview before destroying Outputs, Views and the native
engine. The native preview retains shared View state while attached and observes
View/engine removal atomically, so destroying a View or engine first cannot leave
the platform paint path dereferencing freed state. Destroying the preview removes
its timer/native child synchronously on the UI-affine host callback and performs
no media-worker join.

Repeated detach/attach creates a fresh opaque preview handle and native surface.
Resize changes native surface geometry only; they do not recreate a View,
compositor, camera session, decoder or NDI sender.

## Consequences

### Benefits

- works with Avalonia's supported HWND and NSView hosting on the two first-class
  platforms;
- reuses the authoritative native composed View frame;
- introduces no per-frame managed pixel transport or callback;
- avoids a second compositor and preserves NDI timing independence;
- keeps newest-frame/drop behavior explicit and testable;
- fits the current CPU compositor without pre-empting a later GPU compositor.

### Costs and limitations

- native-hosted content has the normal `NativeControlHost` airspace limitation:
  Avalonia content cannot be layered over the preview region;
- Windows GDI and macOS Core Graphics are CPU presentation adapters and may incur
  OS-owned scaling/upload work;
- color-management tuning and HDR are outside Gate 5C;
- a future GPU compositor should replace the platform presenter behind the same
  runtime ownership model with synchronized shared GPU resources.

## Rejected alternatives

### Managed `WriteableBitmap` copy

Rejected because it would copy every 1920×1080 frame into managed-addressed
presentation memory and make the UI part of the per-frame transport path.

### Separate GStreamer/Avalonia viewer pipeline

Rejected because it would create another media path and could violate the single
RTSP/session ownership invariant.

### Window capture

Rejected because preview must consume the clean View frame, not application UI.

### Immediate GPU texture sharing

Deferred because the current View has no native GPU render target to share.
Creating one in Gate 5C would broaden the task into a compositor redesign.
