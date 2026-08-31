# 20 — Technology Stack Decision

## Decision

RoboCam-Hub should be built as a **native C++ desktop application using Qt 6 / Qt Quick (QML)** for the application shell and editor UI, with **GStreamer** embedded directly for RTSP ingest/decode and the **NDI SDK** integrated natively for clean NDI output.

Primary implementation stack:

```text
Language / Core:       C++20
Desktop UI:            Qt 6 + Qt Quick / QML
Build system:          CMake
RTSP ingest/decode:    GStreamer 1.x
View compositor:       Qt Quick Scene Graph / native GPU rendering layer
NDI output:            NDI SDK native C/C++ integration
Persistence:           versioned JSON / packaged .rchshow archive
Licensing client:      native C++ HTTPS client + signed local lease
Windows build:         MSVC x64
macOS build:           Clang / Apple Silicon first
```

The architecture should keep the real-time media engine separate from the QML/UI layer even though both live in the same desktop application.

## Why this stack

RoboCam-Hub is not primarily a forms/database desktop application. Its difficult requirements are:

- eight simultaneous low-latency 720p60 RTSP feeds;
- direct control of GStreamer buffering and pipeline behaviour;
- GPU composition of multiple live video surfaces;
- multiple simultaneous 1080p60 NDI High Bandwidth outputs;
- strict frame-freshness and back-pressure behaviour;
- native Windows and macOS networking/device integration;
- an OBS-style free-form visual editor;
- clean View frames sent directly to NDI rather than screen capture;
- predictable long-running live-show reliability.

A C++ core minimizes language/runtime boundaries between GStreamer, rendering, networking and NDI.

Qt provides one mature cross-platform desktop/UI framework for Windows and macOS while still allowing direct access to native C/C++ libraries and platform APIs.

## UI framework: Qt Quick / QML

Use Qt Quick/QML for new UI rather than building the application primarily with traditional Qt Widgets.

QML is a strong fit for:

- transformable camera rectangles;
- drag/drop source assignment;
- snapping and guides;
- selection outlines and handles;
- layers and Z-order;
- responsive/collapsible panels;
- animation and transient status states;
- Auto/Light/Dark theming;
- high-DPI Windows/macOS displays.

The application logic and media engine remain C++ and expose only controlled models/state to QML.

Avoid putting media-pipeline logic in JavaScript/QML.

## Media engine boundary

Recommended high-level process structure:

```text
Qt / QML UI
     ↓ commands + read-only state models
Application Controller
     ↓
Media Engine (C++)
├─ Camera Manager
│  └─ GStreamer pipeline per camera
├─ Latest Frame Router
├─ View Render Engine
├─ NDI Output Manager
└─ Runtime Diagnostics
```

The UI thread must never run RTSP, decode, NDI send or heavy compositor work synchronously.

## GStreamer integration

GStreamer should be linked/embedded as an application library rather than controlling long-running `gst-launch` child processes.

Per-camera pipeline logic remains aligned with the proven prototype:

```text
rtspsrc
  latency=0
  buffer-mode=none
  drop-on-latency=true
  protocols=udp
→ rtph264depay
→ decoder
→ latest-frame boundary
```

Each camera pipeline is independently restartable and failure isolated.

GStreamer binaries/runtime dependencies should be packaged with RoboCam-Hub so the user does not install/configure GStreamer separately.

## GPU compositor

The View compositor needs one authoritative clean render surface per active View.

Conceptually:

```text
Decoded Camera Frames
       ↓
GPU textures / latest frame references
       ↓
View compositor
       ↓
Clean View Frame
   ├─ local Qt preview
   └─ NDI sender
```

Do not render the NDI feed by screen-capturing a QML window.

The implementation should investigate the cleanest Qt 6 rendering integration for sharing/importing decoded frame surfaces into the scene graph while avoiding unnecessary CPU copies.

Initial platform GPU directions to benchmark:

- Windows: Direct3D-backed Qt RHI path;
- macOS: Metal-backed Qt RHI path.

Qt's Rendering Hardware Interface (RHI) abstraction may be used where appropriate, but the media/compositor layer should retain a controlled abstraction so a lower-level native path can replace a Qt-specific implementation if benchmarking requires it.

## NDI integration

Use the native NDI SDK from C++.

RoboCam-Hub should provide the NDI sender with the clean composed View frame directly.

Initial target remains:

```text
NDI High Bandwidth
Video only
1920×1080 @ 60 typical
Multiple simultaneous senders
```

The NDI integration is wrapped behind an `NdiOutputBackend` interface so platform/SDK changes do not leak into View or UI code.

## Cross-platform structure

Core code should be platform independent wherever possible.

Suggested source structure:

```text
src/
├─ app/
├─ domain/
├─ media/
│  ├─ ingest/
│  ├─ frames/
│  ├─ compositor/
│  └─ ndi/
├─ persistence/
├─ licensing/
├─ network/
├─ ui/
│  ├─ qml/
│  └─ models/
└─ platform/
   ├─ windows/
   └─ macos/
```

Platform folders contain only capabilities that genuinely differ, such as stable NIC identity, secure token storage, packaging helpers or OS-specific GPU/device handling.

## Windows target

Initial Windows target:

- Windows 11 x64 primary;
- MSVC toolchain;
- signed installer/application;
- Direct3D/Qt RHI rendering path;
- platform-secure token/credential storage;
- bundled GStreamer runtime;
- bundled/licensed NDI runtime as permitted by the NDI SDK agreement.

Windows 10 support should be determined against the selected Qt release and actual user need rather than assumed indefinitely.

## macOS target

Initial macOS target:

- Apple Silicon primary;
- supported current macOS releases;
- Clang/Xcode toolchain;
- Metal/Qt RHI rendering path;
- signed and notarised application;
- Keychain-based secure credential/licence storage where appropriate;
- bundled GStreamer runtime/framework;
- native NDI runtime as permitted by the NDI SDK agreement.

Intel Mac support is optional until performance and dependency testing justifies it.

## Why not Electron

Electron is not recommended as the primary architecture.

Although it would make general UI development fast, RoboCam-Hub would still require native modules for:

- GStreamer;
- NDI;
- low-latency decoded frame surfaces;
- GPU texture sharing;
- stable NIC handling;
- platform secure storage.

That would produce a web UI plus a substantial native media engine with a high-frequency boundary between them. It adds Chromium/runtime overhead without simplifying the hardest parts of the product.

## Why not Tauri

Tauri is lighter than Electron, but the same architectural issue remains: the core application is a real-time native media/rendering system, while the webview would only own the controls/editor UI.

The required frame/compositor/native-library integration would still need a significant Rust/C/C++ bridge and a separate GPU rendering strategy.

Tauri remains attractive for ordinary desktop applications, but not as the default choice for this particular media workload.

## Why not .NET / Avalonia

Avalonia provides strong Windows/macOS UI support and could build a good application shell.

However, RoboCam-Hub would still need native interoperability for GStreamer, NDI and likely the highest-performance rendering path. This adds managed/native lifetime, callback and frame-buffer boundaries to the most latency-sensitive part of the application.

A .NET frontend over a native C++ media library is technically viable, but it is more architectural complexity than using Qt/QML directly over the same C++ core.

## Why not separate native UIs

Building WinUI/WPF on Windows and SwiftUI/AppKit on macOS would provide excellent native platform integration but approximately doubles UI work and testing.

The product does not need radically different OS-native interaction models; it benefits more from one consistent show-control interface on both platforms.

## Qt licensing warning

Qt is dual licensed. Before production development begins, the project must deliberately choose and document either:

- a Qt commercial licence; or
- an LGPL-compliant Qt configuration and distribution model.

Do not accidentally begin development under one Qt licensing route and assume it can later be changed without checking the applicable Qt terms.

For a proprietary commercial RoboCam-Hub product, obtaining a Qt commercial Application Development licence may be the cleanest operational route, but the cost/terms should be checked before committing financially.

This is a commercial/legal dependency, not a reason to choose a technically weaker framework.

## NDI licensing warning

The exact redistribution and commercial terms for the selected NDI SDK/runtime must be reviewed before release.

Architecture should not assume that every SDK component can simply be bundled without complying with NDI's applicable licence agreement.

## Threading model

Indicative threading model:

```text
Main/UI Thread
  Qt event loop + QML only

Camera workers
  independent GStreamer pipelines / callbacks

Frame router
  lock-minimised latest-frame ownership

Render thread(s)
  GPU View composition

NDI sender workers
  independent output timing/senders

Background service workers
  discovery
  persistence/autosave
  licensing refresh
  diagnostics
```

No component should rely on one giant shared worker queue.

## Memory/frame ownership

Avoid copying full-resolution frames repeatedly between subsystems.

Target principles:

- decode into GPU-usable surfaces where practical;
- reference latest complete frame rather than queueing many frames;
- explicit frame lifetime ownership;
- bounded queues at unavoidable asynchronous boundaries;
- zero/low-copy paths benchmarked on both Windows and macOS;
- CPU fallback path remains available if GPU interop is unstable.

## Build system and dependency management

Use CMake as the top-level build system.

CI should eventually produce separate signed build artifacts for Windows and macOS.

Dependencies must use pinned/known versions rather than whatever is installed globally on a developer machine.

A reproducible dependency/package strategy is required for:

- Qt;
- GStreamer;
- NDI SDK;
- JSON/archive library if not using Qt equivalents;
- test frameworks;
- crash reporting if later adopted.

## First technical spike

Before implementing the full product UI, build a cross-platform technical spike that proves the risky path.

Minimum spike:

```text
4 RTSP cameras initially
→ embedded GStreamer low-latency decode
→ GPU 2×2 compositor
→ local Qt preview
→ one direct 1080p60 NDI High Bandwidth output
```

Then scale immediately to:

```text
8 × 720p60 RTSP ingest
2 × independent 2×2 Views
2 × 1080p60 NDI High Bandwidth outputs
```

Run the same benchmark on:

- representative Windows touring laptop;
- representative Apple Silicon Mac.

The spike should measure end-to-end latency, CPU, GPU, memory, dropped frames, recovery behaviour and output freshness before the full editor is built.

## Decision gate

The Qt/C++ architecture is accepted provided the technical spike demonstrates that:

- camera-to-local-preview latency remains near the proven GStreamer test path;
- the compositor does not add unacceptable buffering;
- clean frames can be delivered to NDI without application-window capture;
- eight feeds and two NDI outputs are stable on representative hardware;
- macOS performance is operationally comparable to Windows;
- the Qt licensing route is commercially acceptable.

If the spike fails specifically because Qt's render integration imposes unacceptable overhead, keep the C++/GStreamer/NDI core and replace only the compositor/rendering integration rather than restarting the whole product architecture.

## Decisions adopted

- C++20 is the primary implementation language.
- Qt 6 + Qt Quick/QML is the selected cross-platform desktop UI framework.
- CMake is the build system.
- GStreamer is embedded directly into the application for camera ingest/decode.
- NDI is integrated through the native SDK.
- The media engine is separated from UI state and never blocks the UI thread.
- Windows and macOS are first-class targets from the start.
- Apple Silicon is the initial primary macOS architecture.
- Clean View rendering is the source for NDI; application-window capture is prohibited.
- A technical media-path spike precedes full application implementation.
