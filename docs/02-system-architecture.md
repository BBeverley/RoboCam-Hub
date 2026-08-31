# 02 — System Architecture

## Goal

Define a media architecture that keeps latency predictable, isolates failures, and remains simple enough to deploy on a touring Windows machine.

## High-level architecture

```text
                    ┌──────────────────────┐
Camera NIC / VLAN → │ Camera Discovery     │
                    └──────────┬───────────┘
                               │
                               ↓
┌──────────────┐     ┌──────────────────────┐
│ RTSP Camera 1│ ──→ │ Ingest Pipeline 1    │ ──┐
├──────────────┤     ├──────────────────────┤   │
│ RTSP Camera 2│ ──→ │ Ingest Pipeline 2    │ ──┤
├──────────────┤     ├──────────────────────┤   │
│ RTSP Camera N│ ──→ │ Ingest Pipeline N    │ ──┘
└──────────────┘     └──────────┬───────────┘
                               │ decoded frames
                               ↓
                    ┌──────────────────────┐
                    │ Frame Router / State │
                    └──────────┬───────────┘
                               │
                               ↓
                    ┌──────────────────────┐
                    │ Multiview Compositor │
                    └──────────┬───────────┘
                               │
                     ┌─────────┴─────────┐
                     ↓                   ↓
            ┌────────────────┐  ┌────────────────┐
            │ Local Preview  │  │ NDI Sender     │
            └────────────────┘  └───────┬────────┘
                                        │
                                        ↓
                                   NDI Output NIC
```

## Media engine

GStreamer is the initial preferred media engine because:

- low-latency RTSP ingest has already been proven experimentally;
- it provides mature RTP / RTSP handling;
- pipeline components can be configured independently;
- it supports queue control and frame-dropping behaviour;
- it allows hardware acceleration to be added later where appropriate.

The application should control GStreamer through an embedded integration rather than spawning opaque command-line processes for normal operation.

## Per-camera pipeline isolation

Each camera should have its own independently managed ingest pipeline.

Reference low-latency behaviour:

```text
rtspsrc
  latency=0
  buffer-mode=none
  drop-on-latency=true
  protocols=udp
    ↓
rtph264depay
    ↓
H.264 decoder
    ↓
leaky one-frame queue
    ↓
frame router
```

Exact elements may change after profiling.

### Requirement

A blocked, disconnected or slow camera must never cause the compositor to wait indefinitely for it.

The compositor should work with the newest available frame from each source rather than enforcing broadcast-style frame synchronisation by default.

## Frame freshness model

The application should attach internal metadata to each received frame or source state:

- source ID;
- arrival timestamp;
- source timestamp where useful;
- frame sequence / counter;
- freshness age;
- decode state.

The compositor should prefer the newest completed frame available for each tile.

If no new frame is available, it should either:

- reuse the most recent frame;
- show a degraded / reconnect state after a threshold;
- show a placeholder when the source is lost.

It should not delay healthy cameras waiting for a missing frame from another source.

## Compositor

The compositor is responsible for:

- tile placement;
- scaling;
- crop / fit behaviour;
- labels;
- missing-source overlays;
- output frame generation.

The compositor should initially prioritise low latency over broadcast-perfect frame synchronisation.

Potential implementation approaches should be benchmarked before final selection:

1. GStreamer compositor elements.
2. GPU-backed custom composition.
3. Application rendering layer feeding an NDI frame buffer.

The chosen approach must avoid unnecessary CPU↔GPU copies.

## NDI output

The first implementation target is NDI High Bandwidth.

The NDI sender should receive frames directly from the compositor rather than capturing the application's preview window.

This avoids:

- desktop capture latency;
- extra render stages;
- accidental UI overlays;
- dependence on window visibility.

## Local preview

The local application preview and NDI output should consume the same compositor result or equivalent frame state.

The preview must not be allowed to back-pressure the media pipeline. If the UI cannot render fast enough, preview frames should be dropped.

## Network separation

The application must expose explicit adapter selection for:

### Camera network adapter

Used for:

- RTSP / RTP traffic;
- camera discovery;
- ONVIF or vendor-specific management where later supported.

### NDI network adapter

Used for:

- NDI advertisement / discovery as applicable;
- NDI media output.

These may be the same physical adapter, but the application must support them being different.

The application should not act as an IP router between the two networks.

## Process architecture

Initial recommendation:

```text
Desktop Application Process
  ├─ UI / application state
  ├─ show configuration service
  ├─ camera manager
  ├─ media pipeline manager
  ├─ compositor
  ├─ NDI output manager
  └─ diagnostics / telemetry
```

If media stability requires stronger fault isolation later, camera pipelines could be moved into worker processes. This should not be done prematurely unless profiling or crash behaviour justifies it.

## Threading model

The UI thread must never perform blocking media or network work.

Camera ingest, decoding, composition and NDI sending should run on appropriate worker / media threads.

State updates sent to the UI should be lightweight summaries rather than full media frames unless the rendering architecture explicitly requires shared GPU resources.

## Persistence

Configuration should be local-first.

A show file should reference stable logical identifiers rather than only transient runtime objects.

The storage format should be versioned from the beginning so future migrations are possible.

Potential format:

- JSON or similar structured local document for early versions;
- SQLite if configuration, history or diagnostics become relational enough to justify it.

Decision deferred until the data model is fully defined.

## Dependency boundaries

The application should keep these concepts loosely coupled:

- camera discovery;
- camera configuration;
- media ingest;
- composition;
- NDI output;
- UI;
- show persistence.

This allows, for example, a future SRT output module to be added without rewriting camera ingest.

## Failure domains

The application should explicitly distinguish:

- camera unreachable;
- RTSP session failed;
- RTP packets missing;
- decoder failed;
- frame rate degraded;
- compositor overloaded;
- NDI sender failed;
- NDI NIC unavailable;
- camera NIC unavailable.

Each failure should have a targeted recovery path.

## Initial architectural decisions to validate

Before committing to production architecture, prototype and measure:

1. GStreamer software decode vs hardware decode at 720p60 × 6 or more streams.
2. GStreamer compositor vs custom GPU compositor.
3. Direct NDI SDK sender integration.
4. End-to-end latency through grandMA3.
5. Network behaviour when cameras and NDI share a NIC vs separate NICs.
6. Recovery when one RTSP source is physically disconnected during active output.
