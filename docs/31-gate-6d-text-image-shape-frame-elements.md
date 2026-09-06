# 31 — Gate 6D text, image, shape and frame elements

## Scope and result

Gate 6D extends the ordered View scene with four non-camera element types:

- UTF-8 text using system fonts, word/character wrapping, left/centre/right
  alignment, normal/bold and normal/italic styling, RGBA text colour and an
  optional RGBA background;
- imported PNG/JPEG images with alpha, Stretch/Contain/Cover, opacity and flip;
- filled rectangles with optional outline;
- border-only rectangular frames.

The durable hierarchy is `CameraElementDefinition`, `TextElementDefinition`,
`ImageElementDefinition`, `ShapeElementDefinition` (the Gate 6D rectangle) and
the dedicated `FrameElementDefinition`.

All types share stable ID, normalized X/Y/Width/Height, clockwise rotation,
Z-order, visibility and enabled state. Scene order remains ascending Z then
ordinal UTF-8 element ID. Rectangle interiors are hit-testable; a frame is
hit-testable only on its visible border. The Gate 6B canvas remains schematic:
labels and coloured blocks represent element identity and bounds, while the
adjacent native preview is the authoritative pixel result.

The editor toolbar has a compact Add menu for Camera, Text, Image, Rectangle
and Frame. Image import uses the platform file picker. Only PNG/JPEG dimensions
are read in managed code for schematic Contain geometry; managed code never
decodes, owns or transports the full image pixels. Type-specific properties and
the existing move/resize/rotate/nudge/duplicate/delete/reorder paths all submit
one complete candidate scene.

## Asset identity

`AssetDefinition` carries a stable asset ID, display name, media type, pixel
dimensions and a local runtime source reference. `ImageElementDefinition`
stores only the stable asset ID. This avoids baking a machine path into each
scene element and establishes the identity boundary required by future show
packaging. Gate 6D does not implement `.rchshow` persistence or asset copying;
the runtime source is therefore intentionally session/import metadata.

If a selected file is malformed, missing, or cannot be decoded, apply fails
with an operator-visible result and the preceding native scene remains live.
Deleting the last element that references an asset removes that asset from the
in-memory View catalog.

## Native scene and ABI

ABI 1.10 additively introduces `rch_view_scene_element_v1` and
`rch_view_apply_scene`. The structure has explicit element-kind and fixed-width
enum/scalar fields; all UTF-8 pointers and the array are borrowed for the call.
All entries use one caller-declared `struct_size`, which is also the array
stride, so appended future fields are ignored safely. No C++ class, STL type,
exception, allocator ownership or frame buffer crosses the ABI.

The native call performs:

```text
validate complete mixed candidate
→ resolve logical cameras and asset references
→ decode/rasterize bounded static resources
→ sort by Z/order tie-break
→ atomically swap the View scene
```

The old camera-only call remains exported for compatibility. Both paths feed
the same one View compositor. Image decoding and Pango/Cairo text rasterization
happen only during apply; render ticks reuse retained native RGBA resources.
One resource is limited to 64 MiB and one View to 256 MiB. See ADR 0003.

The compositor uses numeric `0xRRGGBBAA` colour values. Rectangle outline and
frame thickness are output pixels. Corner radius is deferred. A system font
name that is unavailable resolves through Pango's sans-serif fallback rather
than making the active View disappear.

## Ownership and output invariants

Non-camera elements do not affect camera diagnostics. A camera referenced by
one or many elements still owns no more than one RTSP session and one decoder.
The output path remains:

```text
one native View compositor latest frame
├─ native Avalonia preview host
└─ one or more existing NDI sender workers
```

Editor chrome is never composed. No second compositor, sender thread, frame
queue or managed full-frame transport is introduced.

## Deterministic validation

The Gate 6D native regression covers native text pixels and UTF-8/fallback,
PNG alpha over a lower layer, JPEG decode, retained image pixels after the
source file is removed, rectangle fill/outline, frame border semantics,
deterministic Z-order, atomic missing-asset rollback, a camera-plus-missing-
image rollback, scene ABI canaries and zero RTSP/decoder ownership for visuals.

Managed tests cover stable mixed IDs, cross-type duplicate rejection, missing
assets and invalid values, image Contain geometry from header metadata,
topmost/frame-border hit testing, pending non-camera transforms, visual add and
duplicate flows, property validation, View duplication with regenerated
element IDs and reused asset IDs, managed ABI marshaling and runtime ownership.
All pre-Gate-6D tests remain in the normal Release and sanitizer suites.

## Release performance sample

A native Release sample on macOS 14.7.1 x86_64 (GStreamer 1.28.6,
Pango 1.58.2, Cairo 1.18.4) used two independent local RTSP/H.264 1280×720p60
sources and the same 1920×1080 CPU compositor. Each row was observed after a
two-second warm-up, with an 8–12 second measurement window:

| Scene | View fps | render avg/p95 | process CPU | RSS | apply |
|---|---:|---:|---:|---:|---:|
| two cameras | 30.0 | 34.1/35.9 ms | 124.7% | 220.9 MiB | 32 µs |
| cameras + text | 27.4 | 36.8/38.0 ms | 126.4% | 266.4 MiB | 203.8 ms |
| cameras + PNG | 29.0 | 34.4/35.5 ms | 126.1% | 291.4 MiB | 18.6 ms |
| cameras + shape/frame | 27.1 | 37.1/38.3 ms | 125.9% | 291.3 MiB | 8 µs |
| full mixed scene | 24.0 | 41.5/42.7 ms | 125.6% | 307.4 MiB | 13.9 ms |

The full mixed View drove the official NDI backend at 24.9 fps in the same
sample (send average/p95 5.29/6.48 ms, latest sent-frame age 12 ms). No receiver
was attached to that profiling sender, so it is not the visual NDI acceptance
proof. The app's native preview cadence is recorded separately during the
manual comparison.

The first profile exposed an avoidable unrotated-frame scan across every output
pixel. A bounded fast path for unrotated raster/shape/frame elements improved
the full mixed result from 18.8 to 24.0 fps and the shape/frame row from 20.5 to
27.1 fps without changing pixels or ownership. This Intel CPU sample is below
the 60 fps target already missed by the equivalent two-camera CPU scene; it is
not presented as a GPU-compositor result. RSS increased during warm-up and
resource replacement, then was similar between the PNG and shape/frame rows.
These short windows do not establish a leak trend or prove leak freedom. Raw
before/after logs are retained in the ignored Release build directory as
`gate6d-performance.log` and `gate6d-performance-optimized.log`.

## Manual validation procedure

On both macOS and Windows where available:

1. Start two real or deterministic RTSP/H.264 cameras and create one View.
2. Add the two cameras, a title, a transparent PNG logo, a background rectangle
   and a frame from the editor Add menu.
3. Move, resize and rotate every type; flip the image; edit text/font/alignment,
   colours, opacity, outline and frame thickness; duplicate/delete/reorder each
   type and confirm the schematic selection/hit target remains predictable.
4. Confirm the clean native preview contains the same scene without selection
   chrome. Start an NDI output and confirm a known-good receiver matches it.
5. Confirm camera diagnostics remain exactly two RTSP sessions/two decoders,
   then close the application normally.
6. Record View FPS, render average/p95, preview and NDI cadence, CPU/RSS and
   static resource counts for camera-only, +text, +image, +shape/frame and the
   complete mixed scene.

## Deferred

WebP/SVG and broad format support, web-font acquisition, video/animated assets,
transitions, Show Mode/fullscreen, `.rchshow` asset packaging, advanced graphic
design tools and GPU composition remain out of scope.
