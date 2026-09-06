# 28 — Gate 6A View Scene and Transform Foundation

## Scope

Gate 6A replaces the fixed compositor layout model with an extensible ordered
View scene. The first concrete element type is `CameraElementDefinition`.
Existing Gate 5 fixed 2×2 workflows remain compatible, while Runtime and the
native compositor can apply a complete freeform camera scene atomically.

This gate is the scene/runtime foundation, not the final Avalonia editor. It
does not add drag/drop, transform handles, undo/redo, persistence, text, image,
shape or group elements, a GPU compositor, another preview path or another NDI
path.

## Configuration model

`ViewDefinition.SceneElements` is an immutable, ordered collection of
`ViewSceneElementDefinition` values. Gate 6A supports only
`CameraElementDefinition`, which contains:

- a stable element ID and logical camera ID;
- normalized X, Y, width and height;
- normalized left, top, right and bottom source crop;
- integer Z-order;
- clockwise rotation in degrees around the element centre;
- horizontal and vertical flip;
- visible and enabled flags;
- Stretch, Contain or Cover fit mode.

Definitions contain persistent desired configuration only. They contain no IP
address, native handle, decoded-frame reference or runtime health state.
Runtime state remains in `ViewRuntime`, native View diagnostics and each logical
camera runtime.

`WorkspaceRuntimeService.ApplyViewSceneAsync` serializes the potentially
blocking native apply away from the UI thread. It replaces the in-memory
immutable `ViewDefinition` snapshot only after the native runtime accepts the
complete scene. A failed apply therefore leaves both persistent desired state
and the previous live native scene unchanged. Gate 6A adds no editor UI that
invokes this coordination yet.

### Canonical coordinate system

All Gate 6A geometry is normalized to the View or source:

```text
View origin:       (0, 0), top-left
Full View bounds:  X=0, Y=0, Width=1, Height=1
Crop:              fraction removed from each source edge
Rotation:          clockwise around the element centre
```

X and Y may be negative or extend beyond 1 for intentional off-canvas
placement. Rendering clips safely to the 1920×1080 View bounds. Width and
height must be positive. Crop pairs must leave non-zero source width and
height. The coordinate model is independent of Avalonia preview size, window
resize and display scaling.

The current validation ceiling is 16 View widths/heights of normalized
magnitude and ±1,000,000 for Z-order. Non-finite values, zero/negative sizes,
out-of-range crop, rotations outside ±360°, unknown fit values, duplicate
element IDs, unknown logical camera IDs and more than 256 elements are rejected.
Arbitrary finite rotation within ±360° is supported; ±360° is normalized to 0°.

## Ordering and composition

The native compositor sorts elements by ascending Z-order and then by ordinal
UTF-8 element ID. Later elements draw over earlier elements. The element-ID
tie-break prevents map/dictionary iteration order from changing the result.

Crop is applied first. Stretch maps the remaining source region to the element
rectangle. Contain preserves aspect ratio and leaves the uncovered region
transparent to lower layers (opaque black when no lower layer exists). Cover
preserves aspect ratio and centre-crops the excess source region. Flip and
rotation are evaluated in element-local space.

Invisible or disabled elements retain their configuration/binding but do not
contribute pixels. The View background is opaque black. RGBA source alpha is
composited over lower elements.

## Legacy 2×2 migration

The existing four-slot `ViewDefinition` constructor remains source-compatible.
Each assigned slot also projects to a stable camera element:

| Slot | Element ID | X | Y | Width | Height |
| ---: | --- | ---: | ---: | ---: | ---: |
| 0 | `legacy-slot-0` | 0 | 0 | 0.5 | 0.5 |
| 1 | `legacy-slot-1` | 0.5 | 0 | 0.5 | 0.5 |
| 2 | `legacy-slot-2` | 0 | 0.5 | 0.5 | 0.5 |
| 3 | `legacy-slot-3` | 0.5 | 0.5 | 0.5 | 0.5 |

The existing bind/unbind and four-slot status APIs remain available for the
Gate 5 workspace. They update these legacy elements and retain the existing
empty-slot placeholders and source-state diagnostics. An explicit freeform
scene is applied through the new scene API; legacy slot status is intentionally
only a compatibility projection for the four legacy element IDs.

## Native ownership and atomic apply

ABI 1.9 additively introduces `rch_view_camera_element_v1` and
`rch_view_apply_camera_scene`. The element structure uses fixed-width C fields,
borrowed UTF-8 strings, size/version fields and no C++/STL layout. The complete
candidate scene is validated and all logical camera references are resolved
before one locked swap. Any failure leaves the previous scene and bindings
active.

Each camera element holds a weak reference to the existing configured logical
camera. Its last-good image is a bounded native `LatestFrameLease`, not a pixel
copy or queue. Reusing one camera in multiple elements or Views adds consumers
of the same latest decoded frame and never creates another RTSP session or
decoder. One View still owns exactly one compositor and one latest composed
frame, shared independently by preview and all NDI senders.

Source loss retains the element transform and native last-good lease. Healthy
elements continue reading their newest frames. Recovery immediately resumes
from the existing logical camera without rebuilding the scene, View, preview or
sender.

## Deterministic verification

The Gate 6A native tests use local UDP RTSP/H.264 fixtures and representative
pixel assertions to cover geometry, crop, Contain fit, horizontal/vertical
flip, 90°/180° and arbitrary rotation, overlap/Z-order, hidden elements,
off-canvas clipping, invalid-scene atomicity, caller-size canaries, repeated
camera use, multi-View fan-out, transformed source outage/recovery and NDI
consumption of the existing transformed View.

Managed tests cover legacy migration, stable IDs/order, immutable transform
configuration, validation, reference resolution, the thin ABI mapping and
runtime scene application. Gate 5 application behavior remains fixed 2×2 in
this gate and its regression suite remains authoritative.

## Gate 6A transformed-output validation

Gate 6A was manually validated on macOS 14.7.1 x86_64 using the production
Release library, NDI SDK 6.3.2.0 and NDI Video Monitor 5.2. Four independent
local UDP RTSP/H.264 fixtures supplied the sources; this was not a
four-physical-camera test. The deliberately non-grid scene contained a large
background source, a picture-in-picture source, a horizontally cropped source,
and a rotated/flipped overlapping source with explicit Z-order.

The same 1920×1080 composition was visually confirmed in both the native
NSView preview and the official NDI receiver. A separate 60-second temporary
Avalonia `NativeControlHost` harness then exercised the production
Runtime/NativeInterop attach path with the same scene; it remained Live at about
30 fps and also shut down at zero sessions/decoders. The harness was validation
code only and is not part of the product or this gate's committed UI. The
receiver-reported source was `ROBOCAM - Gate6A` at 1080/60p. The 180-second
combined preview/NDI run completed normally with all four cameras Receiving,
exactly four active RTSP sessions and four decoders throughout, and final
shutdown at zero sessions and zero decoders. View, preview and NDI sequences
continued to advance. Newest-first skipped-frame counters increased under load
rather than forming a backlog.

The following numbers are observations from this Intel Mac, not cross-platform
performance guarantees. The steady legacy control excludes its first ten
seconds. The transformed workload intentionally combines freeform
position/scale, crop/flip, arbitrary-angle rotation and overlap/Z-order; the
deterministic native suite also exercises each transform independently with
representative pixel assertions.

| Release workload | View FPS | Render average | Render p95 | Preview FPS | NDI FPS | View frame age |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Equivalent legacy 2×2 | 59.87 | 10.50 ms | 11.79 ms | not attached in control | 59.50 | 8.25 ms average |
| Combined non-grid transform scene | 30.82 | 32.51 ms | 34.33 ms | 29.37 | 31.16 | 19.34 ms average, 0–124 ms |

The combined arbitrary-transform path therefore exceeds the 16.67 ms budget
on this machine, while the compatibility fast path remains near 60 fps. NDI
send duration in the transformed run averaged 5.37 ms with a 6.37 ms p95;
preview frame age averaged 18.43 ms (0–99 ms). The process sampled at 186.5%
and 188.8% CPU. RSS was 381.7 MiB at 79 seconds and 407.8 MiB at 121 seconds.
This short, decoder-active sample is insufficient to characterize the RSS
trend or prove leak freedom; a longer profiling soak remains required. It did
not show progressive frame-age growth or a compositor/consumer backlog.

The NDI receiver ran on the same Mac, so its traffic was loopback. The official
SDK path used the existing direct RGBA frame with no application conversion or
additional full-frame copy. Windows and Apple Silicon performance and official
NDI runtime behavior remain CI/build-only or unverified as applicable.

## Current limitations

- The canvas remains fixed at native 1920×1080/60 for this gate.
- The compositor remains the existing CPU RGBA implementation; a GPU rewrite is
  explicitly deferred.
- Only camera scene elements render. Text, images, shapes, groups and style
  effects remain future element types.
- The Gate 5 Avalonia workspace remains a fixed-slot operational UI. Freeform
  editor controls and persistence are not included.
- Gate 6A does not add output scaling, NIC selection, discovery, audio,
  licensing or managed frame transport.
- Manual performance evidence is a bounded functional sample, not the deferred
  four-hour profiling soak or proof of leak freedom.
- The CPU arbitrary-transform renderer is materially slower than the optimized
  legacy Stretch/no-crop/no-rotation path on the validated Intel Mac; a GPU
  rewrite remains explicitly out of Gate 6A scope.
