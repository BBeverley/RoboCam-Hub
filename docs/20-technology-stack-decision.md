# 20 — Technology Stack Decision

## Decision

RoboCam-Hub should use a **cross-platform Avalonia UI application in C#/.NET**, backed by a **native C++20 media core** that owns all real-time camera, decode, frame-routing, compositing and NDI responsibilities.

Primary implementation stack:

```text
Desktop UI / app shell:      Avalonia UI + C# / .NET
Native media core:           C++20
Build systems:               dotnet + CMake
RTSP ingest/decode:          GStreamer 1.x inside native core
Frame router:                native C++ latest-frame state
View compositor:             native GPU-backed compositor
NDI output:                  native NDI SDK integration
Persistence:                 versioned JSON / packaged .rchshow archive
Licensing client:            application service + signed local lease
Windows target:              x64
macOS target:                Apple Silicon first
```

Avalonia is responsible for the application experience. It must **not** become part of the per-frame camera or NDI media path.

## Hard media invariant

The architecture must preserve the following rule:

> **One configured physical camera = one RoboCam-Hub RTSP session = one decode pipeline.**

A camera may be used by any number of Views, local previews or NDI outputs without opening another RTSP connection or creating another decoder.

Conceptually:

```text
Physical Camera
      ↓
ONE RTSP session
      ↓
ONE H.264 decode pipeline
      ↓
Shared latest-frame state / GPU resource
      ↓
Internal fan-out
├─ View A
├─ View B
├─ View C
├─ local selected View
├─ fullscreen monitoring
├─ NDI Output A
└─ NDI Output B
```

This is a correctness requirement, not merely a performance optimisation.

RoboCam-Hub must never create a second configured-camera stream just because the same source appears in multiple Views or Outputs.

## Why Avalonia + native C++ core

The application has two very different workloads.

### Application/UI workload

Avalonia is a strong fit for:

- Windows and macOS from one UI codebase;
- Settings and licence dialogs;
- camera/source rail;
- first-run and New Show wizards;
- free-form View-editor controls;
- drag/drop and transform UI;
- show-file management;
- diagnostics;
- Auto / Light / Dark themes;
- polished commercial desktop UX.

### Real-time media workload

C++ remains the appropriate ownership boundary for:

- RTSP/RTP sessions;
- embedded GStreamer pipelines;
- H.264 decode;
- latest-frame ownership;
- GPU texture/surface management;
- clean View composition;
- NDI High Bandwidth sender instances;
- frame freshness and back-pressure policy;
- high-frequency diagnostics.

The managed/native split is therefore intentional: Avalonia controls the product, while the native engine controls the media.

## Architectural boundary

Recommended structure:

```text
Avalonia UI / C# application
│
├─ Views / ViewModels
├─ Show workflow
├─ Settings
├─ Licensing UI/client orchestration
├─ persistence orchestration
└─ diagnostics presentation
        │
        │ low-frequency commands + state snapshots
        ↓
Native Media Core — C++20
├─ Camera Manager
│  └─ one GStreamer pipeline per configured camera
├─ Camera Discovery / temporary discovery preview
├─ Latest Frame Router
├─ GPU View Compositor
├─ NDI Output Manager
├─ Network/media diagnostics
└─ platform media adapters
```

The boundary between C# and C++ must be deliberately narrow.

Appropriate calls include:

```text
AddCamera(...)
RemoveCamera(...)
UpdateCameraConfig(...)
ReconnectCamera(...)
CreateView(...)
UpdateViewLayout(...)
CreateOutput(...)
RestartOutput(...)
GetRuntimeSnapshot(...)
```

The boundary must **not** pass every decoded video frame through managed C# memory.

## No per-frame managed round trip

Avoid architectures such as:

```text
GStreamer decode
→ C++ frame
→ copy into C#
→ Avalonia composition
→ copy back to C++
→ NDI
```

This would introduce unnecessary copies, ownership complexity and latency.

Instead:

```text
GStreamer decode
→ native frame / GPU resource
→ native compositor
→ clean View frame
├─ native NDI sender
└─ local Avalonia preview surface/interop
```

The local UI consumes a preview of the already-owned native rendering result. It does not own the camera streams.

## Camera ingest ownership

The native Camera Manager is the only component permitted to create normal configured-camera RTSP sessions.

For each logical camera it owns exactly one ingest object containing, conceptually:

```text
CameraSession
├─ logical source ID
├─ camera address
├─ selected camera NIC/network role
├─ transport: UDP/TCP
├─ GStreamer RTSP session
├─ depay/decode pipeline
├─ newest decoded frame/resource
├─ health metrics
└─ reconnect state
```

Views never create streams.

NDI Outputs never create streams.

Avalonia controls never create streams.

Fullscreen monitoring never creates streams.

## GStreamer path

GStreamer remains embedded directly in the C++ core.

The proven low-latency behaviour remains the starting point:

```text
rtspsrc
  latency=0
  buffer-mode=none
  drop-on-latency=true
  protocols=udp
→ rtph264depay
→ decoder
→ bounded/latest-frame boundary
```

TCP is an explicit per-camera fallback, not a silent automatic transport change.

Each configured camera pipeline is independently reconnectable and failure-isolated.

## Internal frame fan-out

A decoded frame should become a shared internal resource referenced by consumers rather than copied into separate consumer queues.

Conceptually:

```text
Camera 1 decoder
      ↓
LatestFrame(Camera 1)
      ↓
View renderer reads reference
      ↓
View A + View B can both use Camera 1
```

The application should favour immutable/shared frame references or platform-appropriate GPU-resource handles with explicit lifetime ownership.

No consumer may force the source pipeline to wait.

## View compositor

The compositor belongs in the native media core.

It is responsible for producing the authoritative clean View frame containing:

- camera elements;
- crop / scale / rotate / flip;
- text;
- images;
- frames/shapes;
- source-loss placeholders;
- user-created branding.

The compositor must not include application UI such as selection handles, health dots, Settings, camera rail or Show Mode controls.

One clean View render may feed both local monitoring and one or more NDI Outputs.

Where several Outputs reference exactly the same View at the same resolution/frame rate, the architecture should reuse the composed View result rather than re-decoding cameras or unnecessarily recomposing identical source content.

## Avalonia preview integration

Avalonia should display native render results through the most efficient practical platform interop path.

The initial spike must compare available approaches for exposing the native View render into the Avalonia UI without copying full video frames through ordinary managed arrays on every frame.

Possible implementation techniques may differ by OS and Avalonia version, but the abstraction should remain:

```text
Native View Render Target
        ↓
Preview Interop Adapter
        ↓
Avalonia visual control
```

If a zero-copy or low-copy GPU-backed preview path is available and reliable, use it.

A CPU-copy preview fallback may exist for compatibility, but it must never become the NDI source and must not back-pressure the compositor.

## NDI integration

NDI sending remains entirely native.

```text
Clean View frame
      ↓
NDI Output processing
      ↓
Native NDI SDK sender
```

Initial target:

```text
NDI High Bandwidth
Video only
1920×1080 @ 60 typical
Multiple simultaneous senders
```

NDI never consumes an Avalonia window capture.

## Discovery preview special case

Discovery may temporarily require an RTSP connection before a camera becomes a configured source.

This must not undermine the one-stream invariant.

Rules:

- only the currently selected discovered camera needs a live discovery preview in v1;
- the temporary discovery session is clearly owned by the Discovery subsystem;
- when a discovered device is added as a configured camera, the discovery connection must be stopped or transferred before the normal Camera Manager establishes ownership;
- RoboCam-Hub must not leave both discovery and configured sessions connected to the same camera;
- selecting an already-configured camera in discovery must reuse/report the existing source rather than opening another stream where possible.

## Diagnostics invariant checking

Development and Diagnostics builds should expose enough information to prove stream ownership.

Example:

```text
SR Followspot
RTSP sessions owned:      1
Decoder instances:        1
Internal View consumers:  3
NDI outputs consuming:    2
```

If a configured camera reports more than one normal RoboCam-Hub RTSP session or decoder instance, that is treated as a software defect.

Automated tests should validate that:

- adding a camera starts one pipeline;
- placing it into multiple Views does not increase session count;
- adding multiple NDI Outputs does not increase session count;
- switching local Views does not increase session count;
- entering fullscreen does not increase session count;
- leaving discovery for a newly configured camera does not leave a duplicate session alive.

## Cross-platform organisation

Suggested repository/source organisation:

```text
src/
├─ RoboCamHub.App/              # Avalonia / C#
│  ├─ Views/
│  ├─ ViewModels/
│  ├─ Services/
│  ├─ Persistence/
│  ├─ Licensing/
│  └─ NativeBridge/
│
├─ RoboCamHub.Media/            # C++20
│  ├─ ingest/
│  ├─ discovery/
│  ├─ frames/
│  ├─ compositor/
│  ├─ ndi/
│  ├─ diagnostics/
│  └─ platform/
│     ├─ windows/
│     └─ macos/
│
└─ tests/
```

The native media core should expose a stable C-compatible ABI or similarly controlled interop surface rather than exposing arbitrary C++ object layouts directly to .NET.

## Interop strategy

Initial recommendation is a small explicit native API with opaque handles and callbacks/state snapshots.

Example conceptually:

```text
rch_engine_create()
rch_engine_destroy()
rch_camera_add(...)
rch_camera_remove(...)
rch_view_update(...)
rch_output_update(...)
rch_get_runtime_snapshot(...)
```

C# can wrap this behind a clean service layer.

High-frequency health/state events should be throttled/coalesced for UI consumption. Media-frame ownership remains native.

## Threading

Indicative model:

```text
Avalonia UI thread
  UI interaction only

.NET background services
  persistence
  licensing
  low-frequency application coordination

Native camera/media workers
  GStreamer ingest/decode

Native frame router
  latest-frame ownership

Native render workers / GPU queues
  View composition

Native NDI workers
  independent senders
```

No camera, compositor or NDI workload may depend on the Avalonia UI thread remaining responsive.

## Why not pure Avalonia/.NET media processing

A fully managed implementation is not preferred because the hardest workload is native media processing and GPU/resource interoperability.

Keeping that workload in C++:

- matches GStreamer and NDI native APIs naturally;
- makes frame lifetime ownership explicit;
- avoids full-frame managed copies;
- keeps the UI framework replaceable;
- preserves a direct path for platform-specific GPU work;
- makes one-session/one-decoder ownership easy to enforce centrally.

## Why not Qt

Qt remains technically capable, but commercial Qt licensing may add significant recurring/project cost for a proprietary product.

Avalonia provides the cross-platform application UI under a more attractive open-source licensing model while the native C++ media core preserves the technical characteristics that originally made Qt/C++ appealing for the real-time path.

The key requirement is therefore not that the whole application be C++; it is that **the media engine stays native and independent of the UI framework**.

## Build and packaging

Use:

```text
.NET build tooling  → Avalonia app
CMake               → native media library
```

CI should produce integrated Windows and macOS application packages containing the correct native media library and permitted GStreamer/NDI runtime dependencies.

Windows and macOS signing/notarisation requirements remain mandatory for release builds.

## First technical spike

Before the full application UI is built, validate this architecture with a deliberately small Avalonia shell over the real native media engine.

Stage 1:

```text
1 camera
→ one GStreamer RTSP session
→ one decode pipeline
→ native latest-frame state
→ native compositor
→ Avalonia local preview
→ one native NDI output
```

Then:

```text
4 cameras
→ one session/decode each
→ native 2×2 View
→ Avalonia preview
→ one 1080p60 NDI High Bandwidth output
```

Then immediately validate the target workload:

```text
8 × 720p60 cameras
→ exactly 8 RTSP sessions
→ exactly 8 decode pipelines
→ 2 independent 2×2 Views
→ 2 × 1080p60 NDI High Bandwidth outputs
```

The same camera should also be deliberately placed into multiple Views during the spike to prove that the session/decode count remains unchanged.

Run this on representative Windows and Apple Silicon hardware.

## Decision gate

The Avalonia + native C++ architecture is accepted provided the spike proves:

- Avalonia can display the native View output with acceptable overhead;
- no decoded frame has to make a full managed round-trip before NDI;
- one configured camera remains one RTSP session and one decode pipeline regardless of consumer count;
- camera-to-preview latency remains near the proven GStreamer path;
- two independent NDI outputs can operate without opening duplicate camera streams;
- eight-camera target load is stable;
- Windows and macOS packaging is practical.

If Avalonia preview integration is the only weak point, retain the native C++ media core and replace only the local preview/UI interop technique rather than changing the camera/NDI architecture.

## Decisions adopted

- Avalonia UI + C#/.NET is the selected desktop application/UI stack.
- A native C++20 media core owns all real-time camera, decode, frame-routing, compositing and NDI functionality.
- GStreamer is embedded directly in the native media core.
- NDI is integrated natively.
- One configured camera may have only one RoboCam-Hub RTSP session and one decoder pipeline.
- Views and NDI Outputs share decoded frame state and never create their own camera connections.
- Full-resolution media frames must not be routed through managed C# memory as the normal media path.
- Local Avalonia preview is a consumer of native rendered View output and can never back-pressure camera/NDI processing.
- Windows and macOS remain first-class targets.
- Apple Silicon is the initial primary macOS architecture.
- A cross-platform technical spike precedes full product implementation.
