# 10 — View Editor

## Purpose

Define the View editing experience for RoboCam-Hub.

## Gate 6D implementation status

Gate 6D adds editable text, PNG/JPEG image, rectangle and frame elements to the
same ordered scene and clean native View output. The Avalonia canvas remains a
schematic editor; native preview and NDI show the authoritative composed pixels.
See `docs/31-gate-6d-text-image-shape-frame-elements.md`.

Saved template content, durable show asset packaging, undo/redo and the broader
design features below remain future work.

## Gate 6C implementation status

Gate 6C adds the first built-in template and View-duplication workflows on top
of Gate 6B. A compact modal creates Blank, common grid and picture-in-picture
Views, with optional logical-camera assignments for each portable slot.
Templates instantiate ordinary camera scene elements and leave no locked layout
type in the View or native compositor. Duplicating a View regenerates its View
and element IDs while retaining camera references and complete transforms;
Outputs are not copied or rerouted. See
`docs/30-gate-6c-view-templates-layout-workflows.md`.

User-authored/saved templates, drag-and-drop slot population and persistence
remain future work.

## Gate 6B implementation status

Gate 6B implements the first bounded camera-element subset of this design: a
selected-View Avalonia schematic canvas with selection, move, corner resize,
rotation, keyboard nudge, duplicate/delete, Z-order controls, camera-rail Add to
View, deterministic hit testing/snapping and an atomic properties flow. Editor
chrome is separate from the native clean preview and NDI output. See
`docs/29-gate-6b-interactive-view-editor.md` for the exact state/commit model and
current limitations; Gate 6D supersedes its camera-only element limitation.

The editor should feel familiar to users of OBS Studio: a free-form canvas where sources can be dragged, resized, cropped and transformed directly. The difference is that RoboCam-Hub is purpose-built around low-latency followspot camera sources and should therefore provide stronger camera-specific workflows and useful layout templates.

## Core editing model

A View is a free-form canvas.

Gate 6A establishes the underlying scene/transform contract without adding the
editor controls described below. Its canonical coordinates are normalized to
the View, crop is normalized to the source, and element rotation is clockwise
about the element centre. Existing fixed 2×2 definitions migrate to four stable
legacy camera elements. See
`docs/28-gate-6a-view-scene-transform-foundation.md`.

Users may start from:

- a blank canvas;
- a predefined layout template;
- a duplicated existing View;
- a saved user template.

Templates are starting points, not locked layouts.

Once created, every element may be moved, resized, transformed, layered or removed while the editor is unlocked.

```text
Named Camera Sources
       ↓
Elements palette
       ↓ drag
Free-form View canvas
       ↓
NDI Output(s)
```

## Camera naming and source identity

Camera feeds are configured and named outside the View editor.

Example logical sources:

```text
Spot 1
Spot 2
Spot 3
Spot 4
Spot 5
Spot 6
Spot 7
Spot 8
```

The View editor should only reference these logical source names. Network addresses, NICs and RTSP configuration should not clutter the design workspace.

Changing the physical camera assigned to `Spot 3` must automatically update every View containing `Spot 3`.

## Editor layout

Reference direction:

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ ← Views   Spots A      Undo  Redo     1920×1080 60fps     Show Mode [OFF] │
├────────────────┬───────────────────────────────────────────┬────────────────┤
│ SOURCES        │                                           │ PROPERTIES     │
│                │                                           │                │
│ CAMERAS        │                                           │ Spot 2         │
│ ● Spot 1       │                CANVAS                     │ X      960      │
│ ● Spot 2       │                                           │ Y      0        │
│ ● Spot 3       │      ┌────────────┬────────────┐           │ W      960      │
│ ● Spot 4       │      │            │            │           │ H      540      │
│                │      │            │            │           │ Rotation 0°    │
│ ELEMENTS       │      ├────────────┼────────────┤           │ Flip H   [ ]   │
│ + Text         │      │            │            │           │ Flip V   [ ]   │
│ + Image        │      │            │            │           │                │
│ + Rectangle    │      └────────────┴────────────┘           │ Crop / Fit     │
│ + Camera Slot  │                                           │                │
├────────────────┴───────────────────────────────────────────┴────────────────┤
│ Layers: BG | Frame | Spot 1 | Spot 2 | Spot 3 | Spot 4 | Labels | Logo   │
└─────────────────────────────────────────────────────────────────────────────┘
```

The centre canvas should dominate the workspace.

## Adding cameras

There are two valid camera-placement workflows.

### 1. Drag into a predefined Camera Slot

Templates may contain empty Camera Slot elements.

Example:

```text
┌───────────────┬───────────────┐
│ DROP CAMERA   │ DROP CAMERA   │
├───────────────┼───────────────┤
│ DROP CAMERA   │ DROP CAMERA   │
└───────────────┴───────────────┘
```

The user drags `Spot 1` from the camera source list onto a slot.

The slot retains the template-defined:

- position;
- dimensions;
- crop/fit defaults;
- border styling;
- optional label styling;
- clipping/mask behaviour where supported.

Dropping another camera onto an occupied slot should offer or perform source replacement rather than creating an accidental overlap.

### 2. Drag directly onto a blank canvas

Dragging a named camera source onto empty canvas space creates a new Camera element at the drop location.

The element receives a sensible default size while retaining the camera aspect ratio.

The user may then freely transform it.

## Camera Slot versus Camera element

A Camera Slot is a design placeholder used by templates.

A Camera element is a live logical camera source on the canvas.

A Slot may exist without a source assigned. This is useful when building reusable layouts before the final camera count or assignments are known.

A View template may therefore contain:

```text
Camera Slot A
Camera Slot B
Camera Slot C
Camera Slot D
Tour Logo
Labels
Background artwork
```

When applied to a Show, the user fills each slot with named logical cameras.

## Direct manipulation

The editor should support OBS-style direct manipulation.

Selecting an element shows transform handles.

Required actions:

- drag to move;
- drag corner handles to resize proportionally by default;
- modifier key or property control for non-proportional resize;
- edge handles where useful;
- rotate via a rotation handle and numeric field;
- horizontal flip;
- vertical flip;
- keyboard arrow-key nudge;
- larger nudge with modifier key;
- duplicate;
- copy/paste;
- delete;
- lock/unlock.

All transforms should also have exact numeric controls in the Properties panel.

## Camera transforms

A Camera element should expose at least:

### Geometry

- X position;
- Y position;
- width;
- height;
- rotation in degrees;
- anchor / transform origin if required by implementation;
- horizontal flip;
- vertical flip.

### Fit and crop

Modes:

- Fit / Contain;
- Fill / Cover;
- Stretch;
- Manual Crop.

Manual crop should allow either:

- direct crop handles in a dedicated crop mode;
- numeric crop values for top/right/bottom/left;
- both where practical.

The user should be able to reposition the camera image inside its visible frame when using Cover or manual crop.

### Optional future transform controls

Not required for first implementation but the data model should not make them impossible:

- corner pin / perspective;
- arbitrary masks;
- circular/rounded masks;
- colour correction;
- opacity animation.

## Snapping

Snapping should make clean layouts fast without preventing free-form placement.

Default snapping targets:

- canvas edges;
- canvas horizontal/vertical centre;
- other element edges;
- other element centres;
- Camera Slot edges;
- user guides if guides are included.

Snapping should be temporarily bypassable with a modifier key.

The editor should optionally show alignment guides during drag operations.

## Safe areas and guides

Useful optional overlays:

- canvas centre lines;
- configurable safe-area margins;
- rule-of-thirds grid;
- custom guides;
- pixel grid at high zoom.

These overlays are editor-only and must never appear in the NDI output.

## Zoom and canvas navigation

The editor needs comfortable navigation for laptop use.

Required:

- Fit Canvas;
- 25%, 50%, 75%, 100%, 150%, 200% zoom presets;
- mouse-wheel or trackpad zoom;
- pan when zoomed;
- reset view.

Zoom affects only the editor viewport, never the output resolution.

## Layers

Every View uses explicit Z-order.

The Layers panel should support:

- reorder by drag;
- select layer;
- rename element;
- visibility toggle;
- lock toggle;
- duplicate;
- delete;
- multi-select where practical.

Suggested default names:

```text
Background
Frame 1
Spot 1
Spot 2
Spot 3
Spot 4
Title
Tour Logo
```

## Grouping

Grouping is highly desirable for branded layouts.

Example:

```text
Spot 1 Group
  ├─ Camera: Spot 1
  ├─ Border
  └─ Text Label
```

Moving or resizing a Group should transform its child elements together.

If grouping is too complex for the earliest prototype, the data model should still reserve for it because it will materially improve custom layout workflows.

## Templates

Templates are a core usability feature.

### Built-in templates

Initial useful templates:

- Blank;
- Single Camera Fullscreen;
- 2-Up Horizontal;
- 2-Up Vertical;
- 2×2;
- 3×2;
- 4×2;
- 8 Camera Single View;
- Split 8 Cameras into two 2×2 Views.

Templates may contain Camera Slots instead of hard-coded Spot assignments.

### User templates

Users should be able to save a completed View as a reusable template.

A saved template may include:

- canvas resolution/frame rate defaults;
- Camera Slots;
- images/assets;
- frames/shapes;
- text styling;
- background;
- guides;
- labels.

Where a View currently contains fixed logical camera assignments, `Save as Template` should allow the user to choose whether to:

1. preserve the named camera assignments; or
2. convert camera elements into generic Camera Slots.

## Text elements

Text elements should behave as free-form canvas elements.

Required controls:

- drag/resize;
- rotation;
- font family;
- font size;
- weight;
- alignment;
- text colour;
- opacity;
- optional background fill;
- padding;
- optional border;
- layer order.

Dynamic values may later include tokens such as:

```text
{camera.name}
{view.name}
{show.name}
```

## Image elements

Images are primarily for tour branding, logos and decorative design.

Required:

- PNG/JPEG/WebP at minimum;
- transparency where supported by the source format;
- drag/resize;
- rotation;
- horizontal/vertical flip;
- crop/fit;
- opacity;
- layer order.

Imported assets should be managed with the Show so the View does not break if the original Desktop/Downloads path changes.

## Shape / frame elements

V1 should include at least rectangles.

Controls:

- position/size;
- rotation;
- fill;
- opacity;
- stroke colour;
- stroke width;
- corner radius;
- layer order.

These should be sufficient to create most practical camera borders, headers and branded panels.

## Undo and redo

Undo/Redo is required for the editor.

It should cover at least:

- movement;
- resize;
- transform;
- source assignment;
- element create/delete;
- property changes;
- layer reorder;
- paste/duplicate.

A bounded history is acceptable.

## Show Mode

RoboCam-Hub should use a single **Show Mode** rather than separate Live Edit and Staged Edit workflows.

The purpose of Show Mode is to make the View safe during a live performance by locking the design surface against accidental edits while allowing the live media pipelines and NDI outputs to continue normally.

### Show Mode OFF

This is the normal setup / rehearsal state.

The user may:

- select canvas elements;
- move, resize, crop, rotate and flip elements;
- drag cameras from the Sources rail;
- add/delete/duplicate elements;
- reorder layers;
- change element properties;
- use Undo/Redo;
- alter the View freely.

If the View is already being transmitted by an NDI Output, edits are reflected live as they are made.

### Show Mode ON

The canvas becomes operationally read-only.

Show Mode should prevent accidental design changes by disabling:

- element selection handles;
- dragging/resizing/rotation;
- crop controls;
- source replacement by drag/drop;
- adding/removing canvas elements;
- layer reordering;
- destructive transform shortcuts;
- editable element property controls.

The View continues rendering normally and all active NDI Outputs remain live.

Camera health, source-loss indicators and application diagnostics must continue updating.

The UI should show an obvious persistent state such as:

```text
🔒 SHOW MODE
```

or:

```text
Show Mode  [ON]
Canvas Locked
```

The indicator must be prominent enough that a user immediately understands why the canvas cannot be edited, but should not cover the preview or appear in the NDI output.

### Entering Show Mode

Show Mode should be accessible from a prominent button/toggle in the View editor and potentially from the main Operate screen.

Enabling it should be immediate and should not stop/restart camera or NDI pipelines.

No confirmation should normally be required to enable Show Mode because it is a protective action.

### Leaving Show Mode

Leaving Show Mode re-enables editing.

To prevent accidental unlocks during a performance, the app may use one lightweight safeguard such as:

- a deliberate `Unlock Editing` action;
- click-and-confirm;
- hold-to-unlock;
- optional user preference for confirmation.

This should remain fast enough for legitimate show-time adjustments.

### Scope

Initial recommendation: Show Mode is a **global application/show state**, not a per-element state.

When enabled, all View canvases are protected from layout editing. This avoids a situation where one output is accidentally editable while another is locked.

Individual element lock controls still remain useful during design mode for protecting background artwork or finished groups.

### What Show Mode does not lock

Show Mode should not prevent operational actions that may be necessary during a show, such as:

- camera reconnect/recovery;
- viewing diagnostics;
- selecting which View to preview locally;
- starting/stopping an NDI Output if intentionally requested;
- replacing a failed physical camera behind an existing logical Spot from the Cameras area;
- switching an Output to another already-prepared View, if this is an intentionally supported show-time workflow.

These are operational controls rather than canvas-design changes.

## Source loss while editing

A camera tile remains present even if its source is offline.

The editor should display a placeholder containing the logical source name rather than removing the element.

Example:

```text
┌─────────────────────┐
│                     │
│       Spot 3        │
│       OFFLINE       │
│                     │
└─────────────────────┘
```

This allows layouts to be designed without every physical camera connected and allows Show Mode to preserve the intended layout during a source failure.

## Property panel behaviour

The Properties panel changes according to selected element type.

No selection:

- View/canvas properties.

Camera selected:

- source;
- transform;
- crop/fit;
- border/label;
- offline behaviour.

Text selected:

- content;
- typography;
- transform;
- styling.

Image selected:

- asset;
- transform;
- crop/fit;
- opacity.

Shape selected:

- transform;
- fill/stroke;
- radius;
- opacity.

Multi-selection:

- common alignment/distribution actions;
- common transform controls where meaningful.

When Show Mode is enabled, editable design controls should be disabled or replaced with read-only values.

## Context menu

Right-clicking an element should expose common actions similar to professional visual tools when Show Mode is off:

- Transform;
- Fit to Canvas;
- Centre to Canvas;
- Reset Transform;
- Flip Horizontal;
- Flip Vertical;
- Rotate 90° CW;
- Rotate 90° CCW;
- Copy;
- Paste;
- Duplicate;
- Lock;
- Order;
- Delete.

Camera-specific options may include:

- Replace Source;
- Fit;
- Fill;
- Reset Crop.

In Show Mode, design/transform context menus should not be available.

## Keyboard shortcuts

Initial useful shortcuts:

- Ctrl+Z — Undo;
- Ctrl+Y / Ctrl+Shift+Z — Redo;
- Ctrl+C — Copy;
- Ctrl+V — Paste;
- Ctrl+D — Duplicate;
- Delete — Delete element;
- Arrow keys — Nudge;
- Shift+Arrow — Large nudge;
- Ctrl+A — Select all canvas elements;
- optional OBS-like transform shortcuts where intuitive.

Design-changing shortcuts must be disabled while Show Mode is active.

Exact shortcut set should be documented in-app.

## Template creation example

A tour designer could create:

```text
Tour 2×2 Template

Canvas: 1920×1080

Background artwork
Tour Logo
Show title

Camera Slot A    Camera Slot B
Camera Slot C    Camera Slot D

Four matching borders
Four matching camera-name labels
```

At each tour setup:

```text
Slot A ← Spot 1
Slot B ← Spot 2
Slot C ← Spot 3
Slot D ← Spot 4
```

A duplicated View could then map:

```text
Slot A ← Spot 5
Slot B ← Spot 6
Slot C ← Spot 7
Slot D ← Spot 8
```

and be published as a separate NDI output.

## Performance requirements

Editing must not compromise media freshness.

Requirements:

- transform operations should remain responsive while live camera feeds are running;
- local editor preview may drop frames under load rather than back-pressure ingest or NDI;
- dragging/resizing must not create an accumulating media queue;
- rendering an editor selection outline must be separate from the clean View frame sent to NDI;
- source transforms should be GPU-backed where the selected rendering architecture allows it efficiently;
- entering or leaving Show Mode must not restart media or NDI pipelines.

## Initial acceptance tests

- create a blank View;
- drag a named camera onto blank canvas;
- drag named cameras into predefined Camera Slots;
- resize and reposition camera elements;
- rotate a camera;
- flip camera horizontally and vertically;
- crop and reposition source content;
- add text, image and rectangle elements;
- reorder layers;
- duplicate elements;
- use Undo/Redo;
- save and reload without transform drift;
- build a 2×2 layout from a template;
- duplicate the View and replace Spots 1–4 with Spots 5–8;
- enable Show Mode and verify all design manipulation is blocked;
- verify active NDI output continues without interruption when Show Mode is enabled/disabled;
- verify camera health and diagnostics continue updating in Show Mode;
- replace a failed physical camera behind a logical Spot while Show Mode is active without changing the View layout;
- preserve elements when a physical camera goes offline.

## Decisions currently adopted

- Editor is free-form rather than grid-locked.
- OBS-style direct manipulation is the intended interaction model.
- Templates are convenience starting points, not restrictions.
- Camera sources are named/configured outside the editor.
- Cameras can be dragged either into predefined Camera Slots or onto blank canvas space.
- Camera elements support resize, crop, rotate, horizontal flip and vertical flip.
- Physical camera/network settings remain separate from View design.
- Show Mode is used to lock the canvas during live operation rather than maintaining separate Live Edit and Staged Edit workflows.
- Show Mode protects View design while leaving operational camera/NDI functions available.
