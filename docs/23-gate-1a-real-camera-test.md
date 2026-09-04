# Gate 1A — GStreamer dependency and real-camera test

## Dependency strategy

Gate 1A requires the official GStreamer 1.x runtime and development SDK. CMake
requires GStreamer and GStreamer App version 1.24 or newer through `pkg-config`;
configuration fails when either dependency is absent. The runtime must provide
`rtspsrc`, `rtph264depay`, `h264parse`, `avdec_h264`, `queue` and `appsink`.

CI installs the official GStreamer 1.28.6 SDK for Windows x64 and macOS Universal
from `gstreamer.freedesktop.org`. The installer URLs and SHA-256 checksums are
pinned in `.github/workflows/ci.yml`. Application execution never downloads a
runtime. Release packaging and redistribution are separate future work.

For local development, install both runtime and development components from the
[official GStreamer downloads](https://gstreamer.freedesktop.org/download/).
Expose the SDK's `bin` directory on `PATH` and its `lib/pkgconfig` directory on
`PKG_CONFIG_PATH` before configuring CMake. On macOS the official framework root
is normally `/Library/Frameworks/GStreamer.framework/Versions/1.0`.

## Build and deterministic validation

```shell
cmake -S native -B native/build -DBUILD_TESTING=ON -DRCH_BUILD_INGEST_PROBE=ON
cmake --build native/build --config Release --parallel
ctest --test-dir native/build --build-config Release --output-on-failure
```

These tests require the real GStreamer SDK and plugins. They do not substitute a
fake implementation when the dependency is unavailable.
The loopback fixture additionally requires `gstreamer-rtsp-server-1.0`,
`videotestsrc` and `x264enc`; these are test-only, not production ingest dependencies.

## ABI and ownership notes

ABI 1.1 additively extends ADR 0001's existing 1.x control/status contract with
one engine-owned camera, versioned configuration/status structs and explicit
result categories. Existing engine exports and layouts are unchanged. No frame
pointer, pixel buffer or per-frame callback crosses the ABI. Configuration
strings are copied during the call; native `GstSample` references retain decoded
frames without copying their pixels.

GStreamer initialisation is process-wide and reused by engine instances; each
engine destruction stops and releases its pipeline. GStreamer deinitialises
when the library's process-lifetime runtime is destroyed, not between engine
instances. Destroy all engine handles before unloading the library.

Counts describe owned active pipeline components, including connection attempts,
not measured RTSP socket counts. The one-sample latest-frame slot, one-buffer
leaky queue and one-buffer dropping appsink bound downstream backlog. Decoder
reference frames and GStreamer's protocol buffers are separate from this slot.
The first-frame deadline produces `Failed` on connection/negotiation timeout;
there is no automatic reconnect or post-receive inactivity policy in this gate.
Stop waits for native teardown and clears the retained frame. The header
documents concurrency, timestamp, cumulative-counter and configuration rules.

## Deferred RoboSpot/BMFL Profile 2 procedure

Use the production probe built above. No source changes or alternate pipeline
are required.

1. Connect the test computer and RoboSpot/BMFL Profile 2 camera to the camera
   network. Confirm the camera IP without changing any camera settings.
2. Set `GST_DEBUG=rtspsrc:6` to retain RTSP negotiation diagnostics.
3. Run the probe for at least ten minutes, substituting the camera IP:

   macOS:

   ```shell
   GST_DEBUG=rtspsrc:6 native/build/bin/robocamhub_ingest_probe Spot1 rtsp://10.110.0.12/profile2/media.smp 600 2>&1 | tee gate-1a-camera.log
   ```

   Windows PowerShell:

   ```powershell
   $env:GST_DEBUG = "rtspsrc:6"
   native\build\bin\Release\robocamhub_ingest_probe.exe Spot1 rtsp://10.110.0.12/profile2/media.smp 600 2>&1 | Tee-Object gate-1a-camera.log
   ```

4. Confirm the state reaches `Receiving`; `sessions=1` and `decoders=1` never
   exceed one; frame sequence continually increases; and the negotiated size is
   `1280x720` when the camera supplies the expected profile.
5. Estimate decoded rate from the frame-count change between successive lines;
   it should be near 60 fps when the source supplies 720p60.
6. Confirm UDP RTP packets with a packet capture filtered to the camera IP. The
   production `rtspsrc` is restricted to UDP and has no TCP fallback.
7. Record the previously proven direct GStreamer/BaseStation visual baseline
   using a shared stopwatch or sharp movement. This metadata-only probe has no
   video display: its frame age measures local freshness, not camera-to-screen
   latency. The production-path visual latency comparison remains pending a
   separately scoped native display/measurement facility; do not mark it passed
   from the probe or a separate `gst-launch` reference alone.
8. Compare frame age near the beginning and end of the run. It must not trend
   progressively upward. Record process memory at both points as a leak check.
9. Stop the probe normally. Its final line must report `Stopped`, `sessions=0`
   and `decoders=0`.

## Hardware acceptance still pending

Until the procedure above is run with physical hardware, Gate 1A does not claim:

- connection to a Samsung SNZ-6320 or Wisenet/Hanwha XNZ-L6320A-family camera;
- successful use of `profile2/media.smp` on a real RoboSpot installation;
- observed UDP negotiation/traffic from that camera;
- negotiated 1280×720 at 60 fps H.264;
- latency comparable to the proven GStreamer/BaseStation workflow;
- absence of progressive latency growth during a useful live run.
