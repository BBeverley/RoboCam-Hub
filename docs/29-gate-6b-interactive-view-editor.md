# 29 — Gate 6B Interactive View Editor

## Scope

Gate 6B adds the first interactive Avalonia editor for Gate 6A camera scene
elements. It edits only the selected View and uses the existing ABI 1.9 complete
scene apply. It does not add a second compositor or preview, managed frame
transport, persistence, templates, text/image/shape elements, Show Mode,
fullscreen, NIC selection, licensing, animation or a GPU renderer.

The editor and clean output remain separate:

```text
Avalonia schematic editor canvas
        ↓ local pending edit state
immutable View scene definition
        ↓ one atomic ApplyViewSceneAsync call
native compositor latest frame
        ├─ native preview
        └─ NDI output(s)
```

The schematic canvas draws camera names, selection, guides and handles with
Avalonia primitives. It never reads or displays decoded pixels. It does consume
the low-frequency negotiated source width and height already present in camera
status so its geometry can match native crop/fit behavior. Before a source has
reported dimensions, the schematic uses a 16:9 placeholder; there is no native
image to compare before the first frame. The existing native-hosted preview
remains a separate clean surface because ADR 0002 does not permit Avalonia
content to overlay its native airspace.

## Editor state

Each `ViewWorkspaceViewModel` owns one independent `ViewEditorViewModel`. The
editor holds an applied immutable scene plus transient selection, one pending
pointer transform and an optional property draft. Runtime status polling updates
only camera/View/output/preview health; it does not rebuild this edit state.
Switching Views clears transient selection safely and changes only the selected
native preview. Output `ViewId` routing is unchanged.

During move, resize or rotation, only the selected editor element changes. The
full candidate scene is submitted once on pointer release. A successful apply
becomes the next applied editor and workspace definition. A rejected apply
restores the prior applied scene and leaves a concise inline error, so the UI
does not claim that rejected geometry is live. Property-sheet values similarly
remain a separate draft until one successful atomic apply.

## Selection and hit testing

Hit testing uses the Gate 6A scene order rather than Avalonia child order:

1. invisible or disabled elements are ignored;
2. higher Z-order wins;
3. equal Z-order uses descending ordinal element ID because the native renderer
   draws ascending IDs and the later element is topmost;
4. arbitrary rotation is handled by inverse-transforming the pointer into the
   element's final visible local rectangle.

Selection is editor-only state. It cannot change camera ownership, preview
lifecycle, compositor ownership or NDI routing. The selected element gets a
blue primary outline, four corner resize handles and a rotation handle around
the actual visible source image. Right-click offers Properties, Locate Source,
Duplicate, Bring Forward, Send Backward and Delete. Locate Source marks the
matching camera in the external source rail.

### Destination and visible geometry

Gate 6A's `X`, `Y`, `Width` and `Height` remain the destination rectangle. The
editor derives a separate visible rectangle with the same deterministic order
as the native compositor:

```text
negotiated source dimensions
→ fractional crop
→ Stretch / Contain / Cover
→ rotation around destination centre
→ output-canvas clipping
```

- Stretch maps the cropped source across the complete destination, so visible
  and destination bounds are identical.
- Contain preserves the cropped source aspect ratio. Its primary outline,
  hit-test area and handles surround only the fitted visible image. Transparent
  letterbox space is not selectable as that element. When selected, the full
  destination remains visible as a subtle dashed container outline so the
  letterbox space is explicit rather than an unexplained invisible box.
- Cover centre-crops additional source content and fills the complete
  destination, so visible and destination bounds are identical.
- Horizontal and vertical flips change sampling direction, not geometry.
- Rotation is applied to the final fitted visible rectangle about the common
  destination/visible centre, exactly as in the native compositor.
- Elements may extend beyond the output canvas. Their scene-space geometry is
  retained, while schematic fill, selection chrome and hit testing are clipped
  to the same 16:9 canvas boundary as clean output. The scene-element selector
  in the editor toolbar is a non-spatial recovery path for elements positioned
  completely outside the canvas; selecting one there exposes its Properties,
  Delete and keyboard-nudge actions without changing Gate 6A coordinate rules.

## Editing behavior

- Drag moves an element; corner handles resize it.
- Resize preserves the starting aspect ratio by default. Holding Shift unlocks
  the destination ratio for Stretch and Cover. Contain's primary visible image
  necessarily retains the cropped source aspect; exact outer destination
  dimensions, including letterbox space, remain editable in Properties.
- The minimum normalized width and height are `1/60` (32 horizontal pixels or
  18 vertical pixels on the fixed 1920×1080 Gate 6 canvas), keeping small
  elements and handles practical.
- The rotation handle uses Gate 6A clockwise arbitrary rotation. The properties
  sheet also accepts an exact angle.
- Resize handles use the visible fitted bounds and update only destination
  `X`/`Y`/`Width`/`Height`; crop values never feed back from a canvas resize.
- Arrow keys nudge by one displayed editor pixel; Shift+arrow nudges by ten.
- Command/Ctrl+D duplicates and Delete/Backspace deletes the selection.
- Duplicate creates a new GUID-based stable element ID, retains the logical
  camera reference, offsets the element by `0.025`, and places it at the highest
  available Z-order.
- Add to View on the camera rail creates a `0.5 × 0.5` Contain element near the
  centre with a stable ID and deterministic next Z-order.
- Bring Forward and Send Backward exchange adjacent deterministic scene ranks.
  Exact Z-order remains editable in Properties.

The properties sheet exposes X, Y, width, height, crop on all four sides,
rotation, Z-order, horizontal/vertical flip, visibility and Stretch/Contain/
Cover fit.

## Snapping

Move and resize snap independently on each axis to canvas edges, canvas centre,
and the edges/centres of other visible enabled elements. The normalized
tolerance is exactly `1/240` (eight pixels at 1920 width). Snapping is only an
editor calculation; the resulting normalized transform is what enters the
atomic scene definition. Coordinates and extents are clamped to Gate 6A's
finite validation range, and no NaN or infinity can be produced by pointer
input.

## Ownership and performance

Adding or duplicating an element adds another reference to an existing logical
camera. It does not add an RTSP session or decoder. The invariant remains:

```text
configured logical camera → at most one RTSP session → at most one decoder
```

Pointer movement does no native call and does not rebuild the full element
collection. Native work occurs asynchronously as one complete scene apply when
the gesture ends, keeping editor interaction independent from the current CPU
compositor cadence.

## Gate 6B manual validation

The final Release build was exercised on macOS 14.7.1 x86_64 with four
independent local RTSP/H.264 960×540p60 sources. The test added all four cameras
to one View, dragged an element into a non-grid position, resized two elements,
rotated one with the canvas handle, and applied crop and vertical flip from the
property sheet. Z-order was changed, one element was duplicated and deleted,
and a second empty View was selected before returning to the edited View. The
scene remained independent and camera ownership stayed exactly four RTSP
sessions and four decoders throughout.

The adjacent native preview and NDI Video Monitor 5.2 both showed the same clean
transformed output without editor selection chrome. With the official NDI SDK
6.3.2.0 backend and receiver active, the software compositor ran at 37–41 fps,
the native preview held 30 fps, and NDI reported 37–39 fps with 6–20 ms frame
age. One observed NDI sample reported 6.0 ms average / 7.5 ms p95 send duration.
The application process used approximately 1.06 GB RSS and 243–256% CPU while
decoding four sources, compositing, previewing and sending NDI on this Intel Mac.
These short-run samples are functional evidence only, not a memory-leak or
long-soak claim.

Pointer motion was locally responsive during injected multi-step drag, resize
and rotation gestures; native scene state changed only on release. A coarse
end-to-end property Apply measurement, including macOS accessibility polling
overhead, completed in approximately 1.03 seconds, so it is an upper-bound UI
observation rather than a native compositor benchmark.

### Crop/fit geometry correction validation

The selection geometry follow-up was manually compared on the same macOS
14.7.1 x86_64 system using one live local RTSP/H.264 960×540p60 source, the
native preview and NDI Video Monitor 5.2. With 25% cropped from both horizontal
source edges:

- Stretch filled the full primary editor outline in the native preview and NDI;
- Contain showed a narrow primary editor outline around only visible content,
  with the wider destination retained as a subtle dashed container; preview and
  NDI showed the same narrow content geometry;
- Cover filled the full primary editor outline in preview and NDI while applying
  the compositor's additional centre crop.

The source remained one RTSP session/one decoder, preview remained near 30 fps,
and NDI continued during each atomic fit-mode change. The application closed
normally and the RTSP reader session was torn down. This was an editor-only
correction: ABI 1.9 and native crop/fit/compositor code were unchanged.

## Current limitations

- The editor canvas is schematic; live pixels remain solely in the adjacent
  native clean preview.
- Resize has four corner handles only; there is no multi-select, lock control,
  undo/redo, guide persistence or alignment toolbar in this gate.
- Cropping and flips are numeric/property-sheet operations rather than direct
  canvas gestures.
- Canvas resolution remains the Gate 6A fixed 1920×1080 output.
- Manual performance evidence is platform-specific and does not replace the
  deferred long profiling soak.
