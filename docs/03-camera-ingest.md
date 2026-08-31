# 03 — Camera Ingest

## Purpose

Define how RoboCam-Hub discovers, connects to, receives, decodes and supervises followspot camera feeds with the lowest practical latency while remaining robust enough for live touring use.

This document describes the ingest side only. Camera configuration management, multiview composition and NDI output are specified separately.

## Current proven hardware

RoboCam-Hub is initially being designed around camera systems found in Robe BMFL FollowSpot / RoboSpot deployments.

Known camera models currently tested or observed:

- Samsung SNZ-6320;
- Wisenet / Hanwha XNZ-L6320A family.

The application should not hard-code support to these models only. Any camera capable of supplying a compatible RTSP/RTP H.264 stream should be usable through manual configuration, even if enhanced discovery or configuration features are unavailable.

## Primary ingest protocol

Initial ingest target:

- RTSP session control;
- RTP media transport;
- H.264 video;
- UDP transport preferred for operator-low-latency mode.

The application should support TCP transport as a fallback where UDP is unavailable or unreliable.

### Why UDP is preferred

For operator monitoring, the newest frame is more valuable than guaranteed delivery of an older frame.

TCP retransmission can preserve image integrity at the cost of increasing latency when packets are delayed or lost. UDP permits the media pipeline to discard incomplete or late data and continue with fresher frames.

The application therefore prioritises:

> frame freshness over perfect continuity.

## Proven low-latency GStreamer behaviour

The following behaviour has been manually tested and produces an RTSP feed that feels substantially closer to the RoboSpot BaseStation than conventional OBS RTSP playback:

```text
rtspsrc
  latency=0
  drop-on-latency=true
  buffer-mode=none
  protocols=udp
    ↓
rtph264depay
    ↓
H.264 decoder
    ↓
queue
  max-size-buffers=1
  leaky=downstream
    ↓
video consumer
  sync=false
```

A software-decoding test using `avdec_h264 max-threads=1` also produced very low perceived latency.

These exact element choices are not yet a permanent architectural requirement. They are the reference behaviour against which embedded ingest implementations should be benchmarked.

## Camera stream characteristics observed

A tested Wisenet camera profile has been observed with approximately the following settings:

- H.264;
- 1280 × 720;
- 60 fps;
- VBR;
- GOV / GOP length: 1;
- H.264 High profile;
- CABAC;
- Smart Codec disabled;
- Dynamic GOV disabled.

The GOV length of 1 is considered significant for low latency because it minimises dependence on long inter-frame prediction chains.

RoboCam-Hub must initially treat camera-side stream configuration as an external dependency and report what it can observe rather than silently changing it.

Future camera-management functionality may validate or apply known-good low-latency profiles, but that must be implemented separately and conservatively.

## Stream addressing

The application must support both:

1. automatically discovered stream URLs where supported;
2. manually entered RTSP URLs.

Known Hanwha / Samsung deployments commonly expose profile-based URLs such as:

```text
rtsp://<camera-address>/profileN/media.smp
```

The application must not assume a fixed profile number across every camera.

### Camera source record

Each logical camera should store at minimum:

- stable internal source ID;
- user-facing name, e.g. `Spot 1`;
- camera IP address or hostname;
- RTSP URL;
- username where required;
- credential reference;
- preferred transport: UDP / TCP / Auto;
- selected network adapter;
- expected resolution;
- expected frame rate;
- enabled / disabled state;
- optional model / manufacturer metadata;
- optional serial or hardware identifier where discoverable.

Passwords should not be stored directly in plaintext show files.

## Network interface binding

Camera ingest must be explicitly associated with a selected camera network adapter.

The application must support systems where:

- the camera VLAN exists only on one dedicated NIC;
- cameras exist on multiple VLANs reachable through one NIC;
- camera and NDI networks use separate physical adapters;
- camera and NDI traffic intentionally share one adapter.

RoboCam-Hub must not assume the Windows default route is the correct path to a camera.

Where technically possible, discovery and media sockets should be bound to the selected interface or source address rather than relying only on operating-system route preference.

## Discovery

Discovery is desirable but must never be mandatory.

Initial discovery goals:

- enumerate active local network adapters;
- allow the user to choose which adapter(s) to scan;
- discover compatible cameras using ONVIF / WS-Discovery where supported;
- display manufacturer, model, IP and identifying metadata when available;
- allow discovered devices to be assigned to logical spots;
- permit manual addition when discovery fails.

Discovery must not automatically alter the camera.

### Future enhanced discovery

Potential later capability:

- interrogate available media profiles;
- identify codec, resolution, fps and GOP / GOV configuration;
- determine which profile best matches RoboCam-Hub low-latency requirements;
- detect known Robe / Hanwha / Samsung camera families;
- offer a validation result such as `Optimised`, `Usable`, or `High-latency configuration`.

## Connection workflow

For each enabled source, the camera manager should:

1. verify that the selected NIC is available;
2. resolve / validate the camera address;
3. open the RTSP session;
4. negotiate the requested transport;
5. start receiving RTP;
6. depayload and decode the stream;
7. publish the newest decoded frame to the frame router;
8. continuously report health and freshness metrics.

A failure at one stage must be reported distinctly rather than collapsed into a generic `Camera Offline` state.

## Source states

Suggested runtime state machine:

```text
Disabled
  ↓
Waiting for NIC
  ↓
Connecting
  ↓
Negotiating RTSP
  ↓
Receiving
  ↓
Healthy
```

Failure / degraded states may include:

```text
Camera unreachable
Authentication failed
RTSP failed
No RTP received
Decoder stalled
Frame rate degraded
Stale video
Reconnecting
```

The UI may simplify these into user-friendly states while retaining detailed diagnostic information underneath.

## Freshness and queue policy

The ingest system must be designed to prevent progressive latency accumulation.

Requirements:

- queues must be bounded;
- stale frames should be dropped;
- downstream slowdown must not allow an unbounded backlog;
- a source recovering from a temporary stall should return to the newest available media rather than replay buffered history;
- one slow source must not block any other source.

### Operator mode

Default operator mode should use the most aggressive practical low-latency settings.

Typical characteristics:

- zero or minimal jitter buffer;
- UDP preferred;
- late-frame dropping;
- no presentation-time synchronisation that intentionally delays display;
- bounded one-frame or similarly tiny queues where technically appropriate.

### Compatibility mode

A later compatibility mode may trade some latency for resilience on poorer networks, using:

- non-zero jitter buffering;
- TCP transport;
- less aggressive dropping.

This should not be the default for followspot operation.

## Decoder strategy

Initial implementation should support software H.264 decoding because it is known to work and provides a predictable reference point.

Hardware decode should be benchmarked rather than assumed to be lower latency.

Tests should compare at least:

- software decode;
- available Windows GPU decode path(s);
- CPU usage;
- GPU usage;
- frame latency;
- behaviour at 1, 2, 4, 6 and higher simultaneous feeds;
- recovery after packet loss or camera disconnect.

The chosen decoder should minimise frame reordering and internal buffering where possible.

## Reconnection behaviour

Touring operation requires automatic recovery.

When a camera disappears, RoboCam-Hub should:

- immediately mark the source stale / lost;
- preserve its logical `Spot N` assignment;
- keep the rest of the multiview operating;
- retry connection automatically;
- use a bounded reconnect backoff;
- recover without requiring application restart;
- discard old queued media on recovery;
- return directly to the current live frame.

A proposed reconnect sequence is:

```text
Immediate retry
→ short retry interval
→ progressively slower retry interval
→ capped retry interval until source returns
```

Exact timing remains TBD and should be tested against real RoboSpot workflows.

## Authentication and credentials

The application should support authenticated RTSP cameras.

Requirements:

- username may be stored as part of camera configuration;
- passwords must use an OS-backed secure credential mechanism where feasible;
- exported show files should reference credentials rather than contain raw passwords;
- the UI should provide a clear `Authentication failed` state;
- logs must redact credentials and credential-bearing RTSP URLs.

## Diagnostics per camera

At minimum, expose or record:

- connection state;
- camera address;
- selected RTSP profile / URL;
- selected transport;
- negotiated codec;
- negotiated resolution;
- received frame rate;
- decoded frame rate;
- last-frame age;
- dropped-frame count where measurable;
- reconnect count;
- packet-loss indicators where measurable;
- active NIC;
- decoder type;
- current ingest pipeline state.

Advanced diagnostics should be available without cluttering the normal show UI.

## Camera naming and logical assignment

Network identity and show identity must remain separate.

Example:

```text
Logical name: Spot 1
Device: Wisenet XNZ-L6320A
IP: 10.110.0.12
RTSP: profile2/media.smp
```

If the camera IP changes, the user should be able to update or rediscover the device without rebuilding the multiview layout.

Likewise, a spare camera should be assignable to `Spot 1` while preserving the output layout and NDI configuration.

## Safety around camera configuration

RoboCam-Hub must initially be read-only with respect to camera configuration unless the user explicitly invokes a future management action.

It must not silently:

- factory-reset a camera;
- change its IP address;
- alter credentials;
- modify a RoboSpot-required profile;
- change encoder settings;
- reboot the camera.

Future `Apply Low-Latency Profile` functionality should use an explicit preview / confirmation model and should avoid modifying the stream profile used by an active RoboSpot BaseStation unless proven safe.

## Open design decisions

The following still need to be decided through testing and product planning:

1. Should UDP be forced by default or should `Auto` attempt UDP first and fall back to TCP?
2. Should the application support multiple camera NICs simultaneously in v1, or one selected camera NIC with multiple reachable VLANs?
3. What is the target maximum number of simultaneous camera feeds for v1?
4. Which camera discovery protocol(s) are mandatory for v1?
5. Should v1 inspect camera media profiles, or only ingest a supplied / selected RTSP URL?
6. What reconnect timing provides the best balance between fast recovery and avoiding connection storms?
7. Should per-camera latency mode be configurable, or should latency policy be global?
8. Which secure credential store should the Windows application use?
9. Can the same decoded frame be shared efficiently between local preview, compositor and future direct single-camera NDI outputs without copies?

## Initial acceptance tests

A camera-ingest implementation is not considered ready until it passes at least these tests:

- connect to a known Wisenet camera over UDP;
- connect to a known Samsung camera over UDP;
- manually select a valid RTSP profile;
- maintain 720p60 ingest without progressive latency growth;
- disconnect Ethernet from one active camera and verify all other sources remain live;
- reconnect that camera and recover automatically;
- restart a camera while the application remains open and recover automatically;
- run at least six simultaneous 720p60 streams on the reference machine;
- confirm stale frames are dropped rather than accumulated;
- verify a lost camera never blocks the compositor;
- verify credentials are not exposed in logs;
- verify traffic exits through the selected camera NIC;
- compare end-to-end latency against the manually proven GStreamer reference pipeline.
