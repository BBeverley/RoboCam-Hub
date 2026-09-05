# 06 — Multiview Engine

## Purpose

Define how RoboCam-Hub builds one or more low-latency visual outputs from logical camera sources and user-designed graphic elements.

A **View** is a reusable composition. A View may be shown locally and/or published as one or more NDI outputs.

## V1 goals

RoboCam-Hub v1 should support:

- multiple independent Views;
- multiple simultaneous NDI outputs;
- automatic camera-grid layouts;
- fully custom user-designed layouts;
- camera tiles;
- text items;
- frames/shapes;
- image items;
- tour/show branding;
- per-item positioning, sizing and basic styling;
- references to logical camera sources rather than physical IP addresses;
- low-latency composition where stale source frames are dropped rather than accumulated.

## Core model

```text
Camera Sources
   ↓
Logical Spots
   ↓
Views
   ├─ Camera tiles
   ├─ Text
   ├─ Images
   └─ Frames / shapes
   ↓
Render / Compositor
   ├─ Local preview
   └─ NDI Output(s)
```

A View must not own a camera connection. It references logical sources managed by the camera subsystem.

## Gate 3A foundation status

Gate 3A establishes the ownership foundation only:

- native View objects with stable IDs;
- source-slot bind/unbind by logical camera ID;
- shared latest-frame fan-out from camera ingest into bound View sources;
- low-frequency diagnostics for bound-source and source-freshness state.

Gate 3A intentionally does not deliver full 2x2 composition output yet.

Replacing the physical camera assigned to `Spot 2` must therefore update every View using `Spot 2` automatically.

## Gate 3B fixed 2x2 spike status

Gate 3B adds one minimal native compositor path for four logical camera
bindings into one fixed 1920x1080 View.

Current Gate 3B behavior:

- one View render loop targets 60 fps;
- slots 0..3 map to fixed quadrants (TL, TR, BL, BR);
- each render tick reads the freshest available frame per bound source;
- the render loop never waits for all sources to align;
- one slow/missing source does not stall other quadrants;
- output is stored as one native latest composed frame (newest wins, no queue);
- composed-frame leases are reference-counted and race-safe;
- destroying the View releases compositor resources without stopping cameras.

Temporary source-loss policy for Gate 3B:

- if a source never produced a frame, its quadrant renders a deterministic
   placeholder;
- if a source was previously healthy then goes missing, the quadrant freezes the
   last-good frame until fresh frames resume.

Implementation note:

- this gate uses a native CPU compositor intended as a spike proof and is
   isolated so a future GPU compositor can replace it without changing ingest
   ownership semantics.

## Multiple Views

Users may create any practical number of Views. Examples:

- `All Spots` — 2×2 or 3×2 camera grid;
- `FOH Monitor` — all active cameras plus large labels;
- `MA3 Spots` — compact multiview intended for grandMA3;
- `Spot 1 + Spot 2` — two-up operator view;
- `Tour Branded` — custom artwork, frames and text around camera feeds.

Views should be independently configurable for:

- canvas resolution;
- frame rate;
- background;
- layout;
- graphic elements;
- local preview visibility;
- NDI publication.

## Automatic layouts

The application should provide quick automatic layouts for common camera counts.

Examples:

```text
1 camera  → 1×1
2 cameras → 2×1
3 cameras → 3×1 or 2×2 with one unused tile
4 cameras → 2×2
5 cameras → 3×2
6 cameras → 3×2
7 cameras → 4×2
8 cameras → 4×2
```

The exact presets should be configurable later, but v1 should provide sensible defaults.

Automatic layout should be a starting point, not a restriction. Users must be able to convert/edit a generated layout manually.

## Layout editor

The layout editor should behave more like a simple graphics/layout tool than a broadcast vision mixer.

A View canvas contains independently selectable objects.

### Camera tile object

Properties should include:

- logical camera source;
- X/Y position;
- width/height;
- fit mode: contain / cover / stretch;
- crop/zoom controls where practical;
- optional border/frame;
- optional label;
- corner radius if supported by the renderer;
- source-lost behaviour.

### Text object

Properties should include:

- content;
- X/Y position;
- width/height;
- font family;
- font size;
- weight/style;
- alignment;
- text/background opacity;
- optional dynamic tokens later, e.g. `{spot.name}`.

### Image object

Properties should include:

- imported local image asset;
- X/Y position;
- width/height;
- fit mode;
- opacity;
- maintain aspect ratio option.

Use cases include tour logos, sponsor marks and background artwork.

### Frame / shape object

V1 should at least support rectangular frames/blocks with:

- X/Y position;
- width/height;
- fill;
- border/stroke;
- stroke width;
- opacity;
- layer order.

These provide the basic building blocks for a tour-branded multiview without requiring users to create every graphic externally.

## Editing interaction

Expected editor interactions:

- click to select;
- drag to move;
- resize handles;
- keyboard nudge;
- duplicate;
- delete;
- bring forward / send backward;
- lock/unlock element;
- multi-select where practical;
- alignment tools;
- snap to canvas / guides / other objects;
- numeric position and size fields for precise setup.

V1 does not need to become a full design suite. The goal is fast show setup and simple branded layouts.

## Layer model

Each View should maintain explicit Z-order.

Typical order:

```text
Background image / fill
Frames / graphic blocks
Camera tiles
Borders / overlays
Text labels
Tour logo
```

The user must be able to reorder objects.

## Canvas and rendering

Each View should have an explicit output canvas, initially targeting common production formats such as:

- 1920×1080 @ 60 fps;
- 1280×720 @ 60 fps.

The renderer must not wait for every camera to produce a matching timestamp before rendering a frame.

For each camera object, use the newest completed frame currently available.

Before Gate 3B, this remains a minimal native source-binding/runtime foundation
rather than a complete production compositor backend.

A slow or missing camera must never cause healthy camera tiles to gain latency.

## Missing-source behaviour

Per camera tile, a source that becomes unavailable should retain the tile position and styling.

Suggested states:

- short dropout: freeze most recent frame with subtle warning indicator;
- prolonged loss: replace with configurable offline placeholder;
- reconnect: return immediately to newest live frame.

The exact timeout values are TBD.

## NDI relationship

A View is separate from an NDI Output.

This allows:

```text
View: All Spots
  ├─ NDI Output: ROBOSCAM-ALL-SPOTS
  └─ Local Preview

View: Spot Pair
  ├─ NDI Output: ROBOSCAM-SPOT-PAIR-MA3
  └─ NDI Output: ROBOSCAM-SPOT-PAIR-FOH
```

Multiple NDI outputs may reference the same View if required.

This model also leaves room for future outputs such as SRT or local fullscreen displays without changing the View definition.

## Asset management

Images used by a View should be stored as show assets rather than loose external paths wherever practical.

A show should remain portable when moved between machines.

The exact asset storage format is TBD, but the data model should distinguish:

- imported asset identity;
- original filename;
- internal stored copy or managed path;
- use in one or more Views.

## Templates

Users should eventually be able to save a View as a reusable template independent of a show.

Example:

`Tour 4 Spot Layout`

On a new show, the template could reference logical `Spot 1`–`Spot 4` placeholders and automatically populate when those spots are assigned.

This is highly desirable but may follow the basic v1 editor if needed.

## Performance requirements

The multiview engine must:

- render at the configured output frame rate when hardware allows;
- never accumulate frame backlog;
- never allow local preview slowdown to back-pressure NDI output;
- support at least the v1 target of 8 simultaneous 720p60 camera sources;
- avoid unnecessary video frame copies;
- favour GPU composition if benchmarking proves it materially improves performance without adding latency.

## Initial acceptance tests

- create at least three Views simultaneously;
- place 1–8 logical camera tiles on a View;
- create/edit text, image and frame objects;
- reorder layers;
- move/resize objects without stopping live video;
- replace the physical camera assigned to a logical spot and see every View update automatically;
- disconnect one source and verify other tiles remain live and low-latency;
- publish the same View to multiple independent outputs;
- maintain stable latency over an extended runtime;
- save and reload the complete layout without visual changes.

## Open design decisions

1. Exact UI framework for the layout editor.
2. GPU compositor vs GStreamer compositor for final implementation.
3. Whether canvas resolution is fixed presets only in v1 or supports custom dimensions.
4. Offline placeholder styling and timeout behaviour.
5. Asset embedding/storage format.
6. How much text styling is required for v1.
7. Whether reusable View templates ship in v1 or immediately after first release.
