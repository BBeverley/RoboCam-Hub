# 02 — System Architecture

## Goal

Define a media architecture that keeps latency predictable, isolates failures, and avoids opening unnecessary RTSP sessions against the fixture cameras.

A critical requirement is that **each configured physical camera is ingested and decoded only once by RoboCam-Hub**, regardless of how many Views, previews or NDI Outputs use that source.

The camera feeds have limited practical client capacity. RoboCam-Hub must therefore treat the camera connection as a scarce resource and fan decoded frames out internally rather than reconnecting to the camera for each consumer.

## Fundamental single-ingest rule

For each configured camera:

```text
Physical Camera
      ↓
ONE RTSP session
      ↓
ONE decode pipeline
      ↓
Latest decoded frame state
      ↓
Internal fan-out
├─ View A
├─ View B
├─ View C
├─ Local preview
├─ Fullscreen monitoring
└─ NDI Outputs through their referenced Views
```

If `SR Followspot` appears in three Views and those Views feed two NDI Outputs, RoboCam-Hub still opens only **one RTSP connection and one decoder** for `SR Followspot`.

Views reference logical camera sources. They never create their own camera ingest pipelines.

NDI Outputs reference Views. They never create camera ingest pipelines either.

The local UI preview also consumes the already-decoded internal frame state and must never reconnect to the physical camera simply to display a preview.

## High-level architecture

```text
                    ┌──────────────────────┐
Camera NIC / VLAN → │ Camera Discovery     │
                    └──────────┬───────────┘
                               │
                               ↓
┌──────────────┐     ┌──────────────────────┐
│ RTSP Camera 1│ ──→ │ Ingest+Decode Pipe 1 │ ──┐
├──────────────┤     ├──────────────────────┤   │
│ RTSP Camera 2│ ──→ │ Ingest+Decode Pipe 2 │ ──┤
├──────────────┤     ├──────────────────────┤   │
│ RTSP Camera N│ ──→ │ Ingest+Decode Pipe N │ ──┘
└──────────────┘     └──────────┬───────────┘
                               │ one decoded latest-frame source per camera
                               ↓
                    ┌──────────────────────┐
                    │ Frame Router / State │
                    └──────────┬───────────┘
                               │ internal references / GPU surfaces
                               ↓
                    ┌──────────────────────┐
                    │ View Compositor(s)   │
                    └──────────┬───────────┘
                               │ clean composed View frames
                     ┌─────────┴─────────┐
                     ↓                   ↓
            ┌────────────────┐  ┌────────────────┐
            │ Local Preview  │  │ NDI Sender(s)  │
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

Each physical camera has exactly one independently managed ingest/decode pipeline while configured and active.

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
leaky latest-frame boundary
    ↓
shared frame router
```

Exact elements may change after profiling.

### Requirement

A blocked, disconnected or slow camera must never cause the compositor to wait indefinitely for it.

The compositor should work with the newest available frame from each source rather than enforcing broadcast-style frame synchronisation by default.

### No duplicate consumer pipelines

The following operations must **not** create additional RTSP sessions or decoders for an already configured camera:

- adding the same camera to another View;
- displaying that camera in the current editor View;
- entering Show Mode;
- entering local fullscreen monitoring;
- creating multiple NDI Outputs that ultimately use the same camera;
- opening camera properties or diagnostics;
- highlighting/locating a camera from the Camera Source Rail.

All of these consume shared internal source state.

## Frame router / shared source state

The frame router is the boundary between camera ingest and every downstream consumer.

For each logical source it should expose the latest complete decoded frame plus lightweight metadata such as:

- source ID;
- arrival timestamp;
- source timestamp where useful;
- frame sequence / counter;
- freshness age;
- negotiated dimensions and frame rate;
- decode state;
- health state.

The frame itself should be shared by reference or GPU-resource handle where practical rather than copied independently for every View.

A downstream consumer may read the latest complete frame, but it does not own or control the camera pipeline.

## Frame freshness model

The compositor should prefer the newest completed frame available for each tile.

If no new frame is available, it should either:

- reuse the most recent frame;
- show a degraded / reconnect state after a threshold;
- show a placeholder when the source is lost.

It should not delay healthy cameras waiting for a missing frame from another source.

## Multiple Views

Multiple Views may use the same camera concurrently.

Example:

```text
SR Followspot ── ONE ingest/decode ── latest frame
                                      ├─ View: Spots A
                                      ├─ View: All Spots
                                      └─ View: SR Fullscreen
```

The cost of another View is therefore composition/render work, **not another camera network stream or another H.264 decode**.

This is an architectural invariant, not merely an optimisation.

## Compositor

The compositor is responsible for:

- tile placement;
- scaling;
- crop / fit behaviour;
- labels;
- missing-source overlays;
- output frame generation.

The compositor consumes already-decoded source frames from the frame router.

It must never pull RTSP directly from a camera.

The compositor should initially prioritise low latency over broadcast-perfect frame synchronisation.

Potential implementation approaches should be benchmarked before final selection:

1. GStreamer compositor elements operating on the shared decoded source path.
2. GPU-backed custom composition.
3. Application rendering layer feeding an NDI frame buffer.

The chosen approach must avoid unnecessary CPU↔GPU copies.

## NDI output

The first implementation target is NDI High Bandwidth.

The NDI sender receives clean composed View frames directly from the compositor rather than capturing the application's preview window.

NDI never reads RTSP directly and never owns a camera decoder.

This avoids:

- duplicate camera sessions;
- duplicate H.264 decoding;
- desktop capture latency;
- extra render stages;
- accidental UI overlays;
- dependence on window visibility.

## Local preview

The local application preview consumes the same composed View result or the same shared internal source/frame state as NDI composition.

It must not create its own RTSP connection to any configured camera.

The preview must not be allowed to back-pressure the media pipeline. If the UI cannot render fast enough, preview frames should be dropped.

## Discovery preview

Camera discovery is the one workflow where RoboCam-Hub may temporarily open a preview stream before the camera has been added to the Show.

Rules:

- only one discovery preview is active at a time by default;
- once a discovered camera is added, normal operation should transition to the single configured ingest/decode pipeline;
- the discovery preview must be closed or cleanly handed over so the application does not leave both a temporary and configured RTSP connection open;
- selecting an already configured camera for identification must reuse its existing decoded frame rather than opening another session.

This is particularly important because the camera may already be serving another required client such as the RoboSpot BaseStation.

## Network separation

The application must expose explicit adapter selection for camera ingest and NDI output.

### Camera network adapter

Used for:

- the single RTSP / RTP session per configured camera;
- camera discovery;
- read-only metadata access where supported.

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
  │   └─ one ingest/decode instance per active camera
  ├─ shared latest-frame router
  ├─ View compositor(s)
  ├─ NDI output manager
  └─ diagnostics / telemetry
```

If media stability requires stronger fault isolation later, camera pipelines could be moved into worker processes. The single-ingest invariant still applies even if process boundaries change.

## Threading model

The UI thread must never perform blocking media or network work.

Camera ingest, decoding, composition and NDI sending should run on appropriate worker / media threads.

State updates sent to the UI should be lightweight summaries. Live preview should consume shared render/frame resources rather than causing a second decode path.

## Persistence

Configuration should be local-first.

A show file should reference stable logical identifiers rather than transient runtime objects.

Views reference logical camera IDs, not RTSP pipeline instances.

The storage format should be versioned from the beginning so future migrations are possible.

## Dependency boundaries

The application should keep these concepts loosely coupled:

- camera discovery;
- logical camera configuration;
- single camera ingest/decode ownership;
- shared frame routing;
- composition;
- NDI output;
- UI;
- show persistence.

This allows a future output module to consume composed or shared frames without creating another camera connection.

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

A downstream failure must not cause the camera manager to open additional duplicate pipelines as a recovery mechanism.

## Resource accounting / diagnostics

Diagnostics should make accidental duplicate sessions detectable during development and support.

Per configured camera, diagnostics should expose at least:

```text
RTSP sessions owned by RoboCam-Hub: 1
Decoder instances:                  1
Current internal consumers:         4
  - View: Spots A
  - View: All Spots
  - View: SR Fullscreen
  - Local preview
```

A configured active camera showing more than one owned RTSP session should be treated as an architectural fault unless an explicit temporary diagnostic mode justifies it.

## Initial architectural decisions to validate

Before committing to production architecture, prototype and measure:

1. One RTSP/decode pipeline feeding multiple simultaneous Views without creating duplicate camera connections.
2. GStreamer software decode vs hardware decode at 720p60 × 8 streams.
3. GStreamer compositor vs custom GPU compositor.
4. Direct NDI SDK sender integration.
5. Two simultaneous 1080p60 NDI outputs while reusing the same eight single-decoded camera sources.
6. End-to-end latency through grandMA3.
7. Network behaviour when cameras and NDI share a NIC vs separate NICs.
8. Recovery when one RTSP source is physically disconnected during active output.
9. Discovery-preview handover without leaving a second connection open.

## Adopted architectural invariant

**One physical/configured camera = one RoboCam-Hub RTSP session = one decode pipeline.**

Every View, preview and NDI Output fans out from that shared decoded source state. Duplicate camera sessions for downstream consumers are prohibited by design.
