# 21 — Technical Spike Specification

## Purpose

Define the minimum cross-platform technical prototype required before full RoboCam-Hub product development begins.

The spike exists to prove the highest-risk media path on both Windows and macOS before significant effort is spent on the production UI, persistence, licensing workflows or editor tooling.

This is not a production-ready application. It is an engineering validation build.

## Primary objective

Prove that RoboCam-Hub can reliably ingest, decode, compose and publish the required live camera workflow while maintaining the core low-latency and single-session architecture.

Target end state:

```text
8 × RTSP Profile 2 camera feeds
        ↓
8 × single GStreamer ingest/decode pipelines
        ↓
shared latest-frame state
        ↓
2 × independent 2×2 Views
        ↓
2 × 1080p60 NDI High Bandwidth outputs
        ↓
Avalonia local preview/control shell
```

The same architecture must run on:

- representative Windows 11 x64 hardware;
- representative Apple Silicon macOS hardware.

## Non-negotiable camera-session invariant

For every configured physical camera:

```text
1 configured camera
= 1 RoboCam-Hub RTSP session
= 1 H.264 decode pipeline
```

Any number of internal consumers may use the decoded result:

```text
Camera
  ↓
one RTSP session
  ↓
one decoder
  ↓
shared latest-frame state / GPU resource
  ├─ View A
  ├─ View B
  ├─ local preview
  ├─ fullscreen preview
  ├─ NDI Output A
  └─ NDI Output B
```

Adding the same camera to multiple Views or outputs must never create another RTSP connection or decoder instance.

Violating this invariant is a failed spike.

## Selected spike stack

```text
Application/UI shell:   C# / .NET + Avalonia
Native media core:      C++20
RTSP ingest/decode:     GStreamer 1.x
Native build:           CMake
NDI output:              NDI SDK
Windows compiler:       MSVC
macOS compiler:         Clang / Xcode
```

The managed/native boundary must carry commands, configuration, runtime metrics and preview-surface handles/state. It must not copy every decoded full-resolution camera frame through managed memory.

## Media-core responsibilities

The C++ native core owns:

- RTSP connection lifecycle;
- GStreamer pipeline construction;
- RTP/RTSP transport settings;
- H.264 decoding;
- latest-frame ownership;
- frame timestamps/freshness state;
- View composition;
- NDI sender instances;
- media diagnostics;
- reconnect handling;
- frame dropping/back-pressure policy.

Avalonia must not independently open RTSP streams.

## Ingest pipeline

Initial low-latency behaviour should follow the already-proven test path as closely as practical:

```text
rtspsrc
  latency=0
  drop-on-latency=true
  buffer-mode=none
  protocols=udp
→ rtph264depay
→ decoder
→ bounded/latest-frame boundary
```

Default transport for the spike is UDP.

TCP fallback may be exposed as a per-camera test option but is not required for the first success condition.

Normal Robe stream target:

```text
rtsp://<camera-ip>/profile2/media.smp
```

## Phase A — One-camera native proof

Before introducing Avalonia or NDI, prove one camera in the native core.

Required behaviour:

- connect to one Profile 2 stream;
- decode 720p60 H.264;
- maintain low-latency behaviour comparable to the existing GStreamer test;
- expose current FPS and frame-age metrics;
- disconnect/reconnect without process restart;
- confirm exactly one RTSP session and decoder instance.

This phase may use a temporary native test preview or diagnostic sink.

## Phase B — Shared-frame fan-out proof

Use one camera as multiple internal consumers without creating additional camera sessions.

Example:

```text
Camera 1
  ↓
RTSP session count: 1
Decoder count:      1
  ↓
Consumers:
  ├─ Preview A
  ├─ Preview B
  └─ View compositor
```

Instrumentation must prove the RTSP and decoder counts remain at one.

This phase validates the core ownership model before scaling camera count.

## Phase C — Four-camera 2×2 proof

Bring up four simultaneous 720p60 cameras.

Required output:

```text
Camera 1 ─┐
Camera 2 ─┤
Camera 3 ─┤→ 2×2 View
Camera 4 ─┘
```

Validate:

- exactly four RTSP sessions;
- exactly four decode pipelines;
- newest-frame composition;
- no compositor waiting for a slow/missing camera;
- bounded memory use;
- stable operation for at least 30 minutes;
- basic source-loss placeholder/freeze behaviour.

## Phase D — Direct NDI proof

Publish the clean 2×2 View as NDI High Bandwidth.

Target:

```text
4 × 720p60 inputs
        ↓
2×2 View
        ↓
1920×1080 @ 60
        ↓
NDI High Bandwidth
```

Requirements:

- NDI receives the clean View frame directly;
- no application-window capture;
- no Avalonia UI elements in the NDI frame;
- sender failure does not interrupt ingest;
- output queue remains bounded;
- stale frames are dropped rather than queued.

## Phase E — Avalonia integration proof

Add the minimal Avalonia shell.

The UI only needs enough functionality to validate the architecture.

Suggested controls:

```text
ROBOCAM-HUB SPIKE

Cameras
● Camera 1   60 fps   25 ms
● Camera 2   60 fps   23 ms
● Camera 3   60 fps   27 ms
● Camera 4   60 fps   24 ms

[ View A Preview ]

NDI Output
● Broadcasting

RTSP Sessions: 4
Decoders:      4
```

The Avalonia preview must consume an internally rendered/shared View result rather than opening camera streams itself.

A temporary CPU-copy preview is acceptable for initial functional proof only if it is clearly isolated and benchmarked. The preferred direction is a GPU/shared-surface path that avoids moving every camera frame through managed memory.

## Phase F — Full eight-camera target

Scale to the first real production topology:

```text
8 × 720p60 camera inputs

View A — 2×2
  Camera 1
  Camera 2
  Camera 3
  Camera 4

View B — 2×2
  Camera 5
  Camera 6
  Camera 7
  Camera 8

NDI Output A
  View A → 1080p60 High Bandwidth

NDI Output B
  View B → 1080p60 High Bandwidth
```

Required counts:

```text
Configured cameras:    8
RTSP sessions:         8
Decoder pipelines:     8
Views:                 2
NDI senders:           2
```

## Phase G — Duplicate-consumer stress test

Deliberately reuse cameras across multiple Views to prove fan-out does not create new camera connections.

Example:

```text
View A
  Camera 1
  Camera 2
  Camera 3
  Camera 4

View B
  Camera 1
  Camera 2
  Camera 7
  Camera 8
```

Expected result:

```text
Configured cameras: 8
RTSP sessions:      8
Decoders:           8
```

Camera 1 and Camera 2 appearing twice must still each have one RTSP session and one decode pipeline.

## Frame-freshness requirements

The spike must preserve the established low-latency philosophy:

- newest complete frame wins;
- do not accumulate stale frames;
- no unbounded queues;
- a slow consumer must not back-pressure ingest;
- one slow camera must not stall a View;
- one failing NDI output must not stall another;
- local preview must not control NDI timing.

Frame age/freshness should be measurable per camera and per composed View where practical.

## Camera-loss test

While all cameras and outputs are live:

1. physically disconnect one camera/network path;
2. confirm the other seven cameras continue normally;
3. confirm both Views continue rendering;
4. confirm NDI continues broadcasting;
5. confirm no latency backlog develops;
6. reconnect the camera;
7. confirm automatic recovery without rebuilding the View or restarting the app.

The failed camera should transition through clear internal states such as:

```text
Healthy
Degraded
Offline
Reconnecting
Healthy
```

## NDI-loss test

While both NDI outputs are live:

1. remove or disable the configured NDI network adapter;
2. verify camera ingest remains unaffected;
3. verify local preview remains responsive;
4. verify NDI sender state changes cleanly;
5. restore the adapter;
6. verify output recovery without recreating the sender configuration.

If per-NIC NDI binding cannot yet be implemented in the spike, simulate sender failure/restart and separately document the SDK/NIC-binding findings.

## CPU/GPU/memory measurements

Record at minimum:

- total application CPU usage;
- per-process CPU usage if spike components are separated;
- GPU utilisation where available;
- GPU memory where measurable;
- application working-set memory;
- frame rate per camera;
- decoder frame drops;
- compositor frame rate;
- NDI output frame rate;
- dropped NDI/output frames;
- frame-age/freshness measurements;
- RTSP session count;
- decoder instance count.

Measurements should be recorded for:

```text
1 camera
4 cameras
8 cameras
8 cameras + 1 View + 1 NDI output
8 cameras + 2 Views + 2 NDI outputs
8 cameras + overlapping cameras across Views
```

## Latency measurement

Absolute camera-to-screen latency should be measured as practically as possible.

Useful comparative tests:

1. RoboSpot BaseStation preview;
2. proven direct GStreamer low-latency pipeline;
3. RoboCam-Hub native camera frame;
4. RoboCam-Hub local Avalonia View preview;
5. RoboCam-Hub NDI received on a workstation;
6. RoboCam-Hub NDI received/rendered by grandMA3 where available.

The primary spike requirement is that RoboCam-Hub ingest/composition does not introduce a large new buffered delay relative to the proven GStreamer path.

A high-speed camera filming a common timecode/flash/motion reference may be used for practical end-to-end comparisons.

## Long-run stability test

Once the eight-camera/two-output target works, run it continuously for a minimum of four hours.

Monitor for:

- memory growth;
- increasing latency;
- queue growth;
- dropped frame escalation;
- camera reconnect loops;
- NDI sender instability;
- managed/native interop exceptions;
- UI degradation;
- CPU/GPU thermal or scheduling issues.

A later overnight soak test is recommended before production development is considered low risk.

## Windows test target

Initial Windows validation should use a realistic touring-class laptop rather than an unusually powerful development workstation.

Record:

- CPU model;
- GPU model;
- RAM;
- Windows version;
- Ethernet adapter(s);
- GStreamer version;
- NDI SDK/runtime version;
- software vs hardware decoder path.

## macOS test target

Initial macOS validation should use Apple Silicon.

Record:

- Mac model;
- Apple Silicon generation;
- RAM;
- macOS version;
- Ethernet/Thunderbolt adapters;
- GStreamer version;
- NDI SDK/runtime version;
- software vs hardware decoder path.

The architecture must remain the same conceptually on both platforms even if native GPU/decoder implementations differ.

## Hardware decode

The spike should begin with the simplest reliable decoder path that matches the existing proof, then benchmark hardware decode.

Potential direction:

- Windows: suitable D3D11/D3D12-capable GStreamer decoder path where reliable;
- macOS: VideoToolbox-backed decode where reliable.

Hardware decode is desirable for eight 720p60 streams but must not be accepted if it introduces extra buffering or unstable surface interop.

The final choice should be based on measured latency, stability and total system load.

## Preview interop decision gate

Avalonia is accepted as the production UI framework if its local preview can be integrated without changing the core camera-session model and without unacceptable latency/copy overhead.

The preferred relationship is:

```text
Native compositor
      ↓
shared/native View surface
      ↓
Avalonia preview control
```

If the first preview implementation requires a CPU copy, record the cost separately and treat GPU/shared-surface interop as a follow-up spike item.

Failure of one preview technique is not automatically a failure of Avalonia; the media core remains authoritative.

## NDI implementation decision gate

The spike must validate:

- direct native NDI sender creation;
- two simultaneous High Bandwidth outputs;
- 1080p60 send rate;
- clean View frame only;
- sender recovery;
- practical NIC-selection/binding behaviour on Windows and macOS.

Any SDK limitation around binding one sender to a specific NIC must be documented before product UI for NDI network selection is implemented.

## Minimum diagnostic instrumentation

The spike must expose a developer diagnostics view or console containing at least:

```text
Camera ID / Name
RTSP session count
Decoder instance count
Input FPS
Latest frame age
Reconnect count
Pipeline state

View ID
Render FPS
Render time
Late/dropped frame count

NDI Output ID
Sender state
Target FPS
Actual send FPS
Dropped output frames

Process CPU
Process memory
```

This instrumentation is part of the spike, not optional debugging polish.

## Explicit out of scope

Do not spend spike time building:

- polished View editor;
- camera discovery wizard;
- final Settings UI;
- licensing server/client;
- `.rchshow` production persistence;
- template library;
- final theming;
- marketing website;
- installer/update system;
- production diagnostics UI;
- user account portal.

Hard-coded or simple configuration files are acceptable for the spike.

## Pass criteria

The spike passes when all of the following are demonstrated on both Windows and Apple Silicon macOS:

- eight simultaneous supported Profile 2 camera streams can be ingested;
- there are exactly eight RoboCam-Hub RTSP sessions for eight configured cameras;
- there are exactly eight decode pipelines;
- reusing a camera in multiple Views does not increase either count;
- two independent 2×2 Views render continuously;
- two simultaneous 1080p60 NDI High Bandwidth outputs publish clean View frames;
- Avalonia can display the selected View without opening additional RTSP sessions;
- camera loss is isolated and recovers cleanly;
- NDI sender failure/loss does not back-pressure ingest;
- frame queues remain bounded and latency does not progressively increase;
- four-hour soak testing shows no material memory or latency growth;
- CPU/GPU load is considered practical on representative target hardware.

## Fail / redesign triggers

The architecture must be revisited if any of these occur:

- Avalonia integration requires separate RTSP/decode pipelines for preview;
- decoded frames must routinely pass through multiple full CPU copies to reach Views/NDI;
- eight 720p60 streams cannot run reliably on representative hardware;
- compositor architecture introduces significant buffered latency;
- two NDI 1080p60 outputs destabilise ingest;
- one failed camera or output blocks healthy streams;
- RTSP/decoder counts increase when Views reuse cameras;
- macOS requires a fundamentally different product architecture rather than a platform-specific backend.

## Deliverables

At completion, the spike should produce:

1. runnable Windows spike build;
2. runnable macOS Apple Silicon spike build;
3. native media-core source;
4. minimal Avalonia shell source;
5. reproducible build instructions;
6. benchmark results for 1/4/8-camera scenarios;
7. latency comparison notes;
8. RTSP/decoder-count proof for duplicated View usage;
9. NDI two-output test results;
10. camera/NDI failure-recovery results;
11. four-hour soak-test results;
12. list of any unresolved platform/SDK limitations;
13. go/no-go recommendation for full product development.

## Decisions adopted

- The spike precedes production application development.
- Avalonia is validated as the UI shell, not the owner of camera media.
- The native C++ core owns all RTSP sessions and decode pipelines.
- One configured physical camera creates exactly one RoboCam-Hub RTSP session and one decode pipeline.
- Decoded frames are internally shared across all Views, previews and outputs.
- Eight 720p60 camera feeds are the primary ingest target.
- Two independent 2×2 Views are the main eight-camera composition target.
- Two simultaneous 1080p60 NDI High Bandwidth outputs are required for the spike to pass.
- Both Windows and Apple Silicon macOS must pass before the architecture is considered validated.
- Freshness, bounded queues and failure isolation are more important than perfect frame synchronisation.
