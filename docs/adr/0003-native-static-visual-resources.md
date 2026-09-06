# ADR 0003 — Native static visual resources

Status: Accepted for Gate 6D

## Context

Text and imported images must appear in the authoritative clean View frame used
by native preview and NDI. Rendering them through Avalonia, capturing editor
chrome, or copying full frames through managed memory would violate the native
media boundary. Gate 6D also needs equivalent Windows and macOS behaviour
without introducing a new heavyweight graphics engine.

## Decision

The existing native compositor owns all text and image pixels.

- Pango/Cairo rasterizes UTF-8 text once while a candidate scene is prepared.
  These libraries already ship with the required cross-platform GStreamer SDK.
  Requested system font families use Pango's platform font resolution; a
  missing family falls back deterministically to the platform's sans-serif
  font.
- The existing GStreamer runtime decodes PNG and JPEG assets once while a
  candidate scene is prepared. The decoded straight-RGBA resource is retained
  by the native scene element and reused on render ticks.
- Rectangle and frame elements are rendered procedurally in the same native
  compositor. Numeric colours use `0xRRGGBBAA`.
- Resource preparation occurs before the scene lock and before the atomic swap.
  A missing/invalid asset, invalid element, or resource-budget failure leaves
  the previously active scene unchanged.
- One raster resource is capped at 64 MiB and all raster resources in a View
  are capped at 256 MiB. No frame queue is added.
- Managed `ImageElementDefinition` contains only a stable `AssetId`. The
  associated `AssetDefinition` separates display/media metadata from its local
  runtime source reference. Durable show packaging remains a later persistence
  task; the local path is not the element's durable identity.

## Consequences

Text/image preparation may make a scene-apply command take measurable time, so
application calls remain off the Avalonia UI thread. Per-frame work does not
decode images or shape text. Preview and every NDI sender continue to consume
the same latest composed View frame, and visual elements create no RTSP session
or decoder ownership.

Gate 6D supports system fonts, PNG and JPEG only. Web fonts, WebP/SVG, video
assets, animation, transitions, embedded show assets and a GPU compositor are
explicitly deferred.
