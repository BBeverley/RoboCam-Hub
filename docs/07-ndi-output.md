# 07 — NDI Output

## Purpose

Define how RoboCam-Hub publishes one or more independent NDI feeds from user-created Views.

An **NDI Output** is a publication endpoint. It references a View, applies output-specific settings, and sends that rendered video onto one or more selected network adapters.

NDI Outputs are deliberately separate from Views so that a single visual design can be reused, scaled, duplicated or sent to different networks without rebuilding the layout.

## Core model

```text
Logical Cameras
      ↓
     Views
      ↓
 ┌────┴────┐
 │         │
Output A  Output B
 │         │
NDI NICs  NDI NICs
```

A View describes *what the picture looks like*.

An NDI Output describes *how and where that picture is published*.

## Multiple simultaneous outputs

RoboCam-Hub must support multiple NDI outputs at the same time.

This is a core use case, not an advanced feature.

Example with eight followspot cameras split across two monitors:

```text
Spot 1 ─┐
Spot 2 ─┤
Spot 3 ─┤→ View A: Spots 1–4, 2×2 grid → NDI: ROBOCAM - SPOTS A
Spot 4 ─┘

Spot 5 ─┐
Spot 6 ─┤
Spot 7 ─┤→ View B: Spots 5–8, 2×2 grid → NDI: ROBOCAM - SPOTS B
Spot 8 ─┘
```

This allows two physical displays or two grandMA3 video objects to show larger camera tiles than a single 4×2 eight-camera multiview.

The same system must also support:

- one 4×2 View containing all eight cameras;
- two independent 2×2 Views;
- one 2×2 and one single-camera View;
- several NDI outputs referencing the same View;
- outputs sent to different network adapters.

## Typical touring configurations

### Four-camera show

```text
View: MA3 Main
  Spot 1 / Spot 2
  Spot 3 / Spot 4

NDI Output:
  ROBOCAM - MA3 MAIN
```

### Eight-camera show — split monitoring

```text
View: Spots A
  Spot 1 / Spot 2
  Spot 3 / Spot 4

View: Spots B
  Spot 5 / Spot 6
  Spot 7 / Spot 8

NDI Outputs:
  ROBOCAM - SPOTS A
  ROBOCAM - SPOTS B
```

### Redundant networks

```text
View: MA3 Main

Output 1:
  ROBOCAM - MA3 A
  NIC: Lighting Network A

Output 2:
  ROBOCAM - MA3 B
  NIC: Lighting Network B
```

### Mixed destinations

```text
View: MA3 Multiview
  → ROBOCAM - MA3

View: FOH Multiview
  → ROBOCAM - FOH

View: Spot 1 Fullscreen
  → ROBOCAM - SPOT 1
```

## Output properties

Each NDI Output should store at minimum:

- stable output ID;
- user-facing name;
- NDI source name;
- referenced View;
- enabled / disabled state;
- target resolution;
- target frame rate;
- NDI mode;
- selected output network adapter(s);
- optional scaling behaviour;
- startup behaviour;
- runtime status;
- receiver count where exposed by the NDI SDK.

## NDI naming

Users must be able to set a clear NDI source name per output.

Recommended default naming pattern:

```text
ROBOCAM - <OUTPUT NAME>
```

Examples:

```text
ROBOCAM - SPOTS A
ROBOCAM - SPOTS B
ROBOCAM - MA3 MAIN
ROBOCAM - FOH
```

A global prefix should be configurable in Settings.

The application should prevent or clearly warn about duplicate active NDI source names on the same machine.

## NDI mode

Initial target:

- NDI High Bandwidth;
- 60 fps operation;
- 1920×1080 and 1280×720 common presets.

NDI HX variants may be added later if useful, but they should not be the first implementation target because followspot monitoring prioritises latency over bandwidth efficiency.

## Gate 4A ownership path

For Gate 4A, the sender must remain a direct native publication of the already-composed View frame. It does not own a second ingest pipeline, does not create a second RTSP connection, and does not create an extra decoder. The sender binds to the latest composed frame output of an existing View and consumes newest-frame semantics without queue growth.

The implementation contract remains:

```text
configured camera
  → one RTSP session
  → one decoder pipeline
  → shared latest-frame state
  → View compositor
  → direct sender from composed frame
```

This ensures that slow output or a stalled sender cannot back-pressure camera ingest. The sender is a bounded output consumer, not a second production camera path.

## Official SDK discovery

The official NDI SDK is not bundled in this repository and must not be committed. CMake discovers it from an installed SDK location without shipping proprietary binaries or scripts. Set `RCH_NDI_SDK_ROOT`, `NDI_SDK_ROOT`, `NDI_SDK_DIR`, or `NDI_INSTALL_DIR`, or provide standard CMake include/library search paths. On macOS, the vendor's standard `/Library/NDI SDK for Apple` installation is also discovered automatically. The configured build reports both the selected include directory and library.

`RCH_ENABLE_NDI_SDK=ON` is the default. When both the header and library are found, the production native library builds the official adapter. Failure to initialise the discovered runtime or create its sender is reported as sender creation failure; production does not silently switch to the deterministic backend.

When the SDK is not discovered, the native sender runs in deterministic sender-core/backend proof mode. This mode validates lifecycle, ownership, latest-frame handoff, and bounded newest-wins behavior; it does not claim real NDI publish/discovery success.

Public GitHub-hosted CI intentionally does not install or redistribute the proprietary SDK. Its Windows x64 and macOS arm64 jobs therefore compile and test the deterministic backend plus all non-NDI regressions. A local build with an externally installed official SDK is required to compile and validate real NDI interoperability.

## Gate 4A official sender lifecycle and frame format

Gate 4A owns one official NDI runtime reference and one SDK sender instance per native sender handle. The sender is created with `clock_video=true`, `clock_audio=false`, and is retained across start/stop and receiver disconnect/reconnect. Destroy first joins the existing bounded worker, then destroys the SDK sender and releases the runtime reference. SDK types remain private to the adapter and never cross the plain C ABI.

The existing View produces tightly packed 1920×1080 RGBA progressive frames at a nominal 60 fps. The official SDK exposes `NDIlib_FourCC_video_type_RGBA`, so the adapter maps the retained GStreamer buffer read-only and calls the synchronous `NDIlib_send_send_video_v2` API with that pointer and a `width × 4` stride. RoboCam-Hub performs no per-frame color conversion and adds no full-frame copy. The synchronous API is deliberate: the lease stays valid for exactly the call, avoiding the extra in-flight buffers required by the SDK's asynchronous API. Any SDK-internal compression or conversion remains owned by the SDK.

The call runs only on the existing bounded sender worker. If it is slow, the View compositor continues independently and the next worker iteration acquires the newest composed frame rather than draining a queue. Receiver count is sampled through the SDK's non-blocking connection-count query at most once per second and exposed through the existing low-frequency sender status.

For a real four-camera proof, configure with `RCH_ENABLE_NDI_SDK=ON` and run the manual probe:

```shell
cmake -S native -B native/build-ndi -DBUILD_TESTING=ON -DRCH_BUILD_NDI_PROBE=ON -DRCH_ENABLE_NDI_SDK=ON -DRCH_NDI_SDK_ROOT="/path/to/installed/NDI SDK" -DCMAKE_BUILD_TYPE=Release
cmake --build native/build-ndi --config Release --parallel
native/build-ndi/bin/robocamhub_ndi_sender_probe 600 <rtsp-url-1> <rtsp-url-2> <rtsp-url-3> <rtsp-url-4>
```

The probe uses the fixed `ROBOCAM - Gate4A` source name and refuses to run when only the deterministic backend is active. It reports View/sender cadence, frame age, send duration, skipped sequences, receiver count, individual camera state, and aggregate RTSP/decoder ownership each second. It does not replace receiver-side visual validation.

## Gate 4A validation result

Gate 4A completed a 600-second official-SDK proof using NDI SDK 6.3.2.0 on macOS 14.7.1 x86_64 with NDI Video Monitor 5.2. `ROBOCAM - Gate4A` was discovered and its four-section 2×2 View was visually confirmed. The sender published 1920×1080 RGBA with a declared 60/1 rate using the direct RGBA path described above, with no application color conversion or explicit full-frame copy.

During a single-source outage, the View reported three live quadrants and one frozen quadrant while NDI continued. Reconnection restored four live quadrants without sender recreation. Disconnecting and reconnecting the receiver did not rebuild ingest, the View, or the sender. With all sources healthy, aggregate ownership remained bounded at exactly four RTSP sessions and four decoders; normal shutdown released both totals to zero.

The proof used four independent local RTSP/H.264 sources rather than four physical cameras, and receiver traffic was loopback. Official-SDK runtime validation remains unverified on Windows and Apple Silicon, as do grandMA3 interoperability and remote-NIC behavior. The formal four-hour profiling soak remains deferred. The 10-minute RSS trend does not establish leak freedom.

## View resolution vs output resolution

A View and an NDI Output are independent.

Example:

```text
View:
  1920×1080 @ 60 fps

Output A:
  1920×1080 @ 60 fps

Output B:
  1280×720 @ 60 fps
```

The View should be rendered once at its native resolution where practical. Output-specific scaling should occur after View composition rather than forcing duplicate full View renders.

If an output uses the same resolution and frame rate as its View, the system should avoid unnecessary conversion or copying.

## Multiple output adapters

Users must be able to configure multiple NDI-capable network adapters on the machine.

The adapter selector should:

- list all available physical and virtual NICs;
- allow one or more adapters to be selected as NDI-capable;
- remember previously selected adapters;
- identify remembered adapters that are currently missing;
- handle USB Ethernet adapters that may not be connected at application startup;
- allow an output to target one or more configured NDI NICs where technically supported.

Example:

```text
NDI Adapters

☑ Intel I225 - Lighting A
☑ USB 2.5GbE - Lighting B
☐ Wi-Fi
```

A remembered but disconnected USB NIC should appear as unavailable rather than being silently removed from the configuration.

## Output runtime states

Suggested output states:

```text
Disabled
Starting
Broadcasting
No View
Waiting for NIC
NIC unavailable
Sender error
Degraded
Stopping
```

The normal operator UI should simplify these to clear status indicators while detailed failure information remains available in diagnostics.

## Output independence

One failed NDI Output must not stop:

- camera ingest;
- other Views;
- other NDI Outputs;
- local preview.

Likewise, an overloaded or unavailable destination NIC must not cause source camera queues to accumulate latency.

Each output path must be independently back-pressure protected.

## Output controls

The user should be able to:

- start / stop an individual NDI Output;
- enable / disable automatic start when the show opens;
- duplicate an output;
- change which View it references;
- rename its NDI source;
- select one or more output NICs;
- change resolution and frame rate;
- see whether it is actively broadcasting;
- see receiver information when available.

A global `Start All Outputs` / `Stop All Outputs` control may be useful on the main show page.

## Output and View duplication

Fast setup matters on tour.

Recommended workflow for an eight-camera split:

1. Create a `2×2` View using Spots 1–4.
2. Duplicate the View.
3. Replace the four camera assignments with Spots 5–8.
4. Name the Views `Spots A` and `Spots B`.
5. Create one NDI Output for each View.

A later quality-of-life feature could provide a direct `Create Split 8-Camera Setup` preset that performs these steps automatically.

## Main-screen representation

The operational screen should make multiple outputs obvious without consuming excessive space.

Example:

```text
OUTPUTS

● SPOTS A
  ROBOCAM - SPOTS A
  View: Spots A

● SPOTS B
  ROBOCAM - SPOTS B
  View: Spots B

○ BACKUP
  Stopped
```

Selecting an output should show its key configuration and optionally switch the central preview to the View referenced by that output.

## grandMA3 use case

A likely MA3 workflow is:

```text
RoboCam-Hub
  NDI: ROBOCAM - SPOTS A
  NDI: ROBOCAM - SPOTS B
      ↓
Lighting network
      ↓
grandMA3
  Video source A → monitor/layout 1
  Video source B → monitor/layout 2
```

This gives the operator two separate 2×2 camera grids rather than eight smaller camera images on one output.

The exact number of MA3 NDI receivers and their practical performance must be tested on real console hardware / onPC systems.

## Diagnostics

Per output, RoboCam-Hub should expose:

- output state;
- NDI source name;
- referenced View;
- resolution;
- frame rate;
- actual send frame rate;
- active NIC(s);
- dropped output frames where measurable;
- renderer / scale time;
- sender time;
- receiver count if available;
- recent errors;
- output start / reconnect count.

## Performance policy

The output engine must favour freshness over queueing.

Requirements:

- no unbounded output queues;
- if an NDI sender cannot keep up, stale frames should be dropped rather than accumulating delay;
- one NDI Output must not back-pressure another;
- one NDI Output must not back-pressure the camera ingest pipelines;
- local preview performance must not dictate NDI send timing;
- two simultaneous 1080p60 High Bandwidth NDI outputs must be part of early benchmarking;
- additional output counts should be benchmarked before a formal v1 supported maximum is published.

## Initial acceptance tests

- publish one 1080p60 NDI High Bandwidth output;
- publish two simultaneous 1080p60 outputs;
- use separate Views for Spots 1–4 and Spots 5–8;
- verify both outputs remain low-latency while all eight 720p60 camera feeds are active;
- route the two outputs to grandMA3 and display them simultaneously;
- stop one output without affecting the other;
- disconnect one NDI NIC without affecting camera ingest;
- reconnect a remembered USB NDI NIC and recover without rebuilding the show configuration;
- publish the same View through two separate Output objects;
- scale a 1080p View to a 720p NDI output without changing the View;
- verify output frame queues do not accumulate latency during temporary network or receiver issues.

## Open design decisions

1. Exact NDI SDK capabilities for binding a sender to one or more selected NICs on Windows.
2. Whether one Output can directly publish on multiple NICs or whether the UI should model one sender instance per NIC under the hood.
3. Formal supported maximum number of simultaneous NDI outputs for v1.
4. Whether output frame rate may differ from View frame rate in v1.
5. Whether NDI audio should be unsupported, silent, or configurable. Initial expectation is video-only.
6. Whether receiver count and receiver identity are exposed reliably enough to show in the normal UI.
