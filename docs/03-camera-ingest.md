# 03 — Camera Ingest

## Purpose

Define how RoboCam-Hub discovers, connects to, receives, decodes and supervises followspot camera feeds with the lowest practical latency while remaining robust enough for live touring use.

This document describes ingest only. Multiview composition, NDI output and application settings are specified separately.

## V1 scope decisions

The following product decisions are now locked for v1:

- support up to **8 simultaneous camera feeds**;
- support **multiple camera network adapters simultaneously**;
- remember previously selected adapters between application sessions;
- tolerate previously selected USB NICs being absent at startup and reconnect to them when they reappear;
- support both **manual camera IP entry** and **optional automatic discovery**;
- for Robe-supported camera workflows, use **RTSP profile 2 only** in v1;
- default to **UDP / low-latency transport**;
- allow explicit per-camera TCP fallback;
- do not silently fall back from UDP to TCP;
- camera configuration is **read-only** from RoboCam-Hub;
- report observable camera/profile information where possible, but do not modify camera settings.

## Current proven hardware

RoboCam-Hub is initially being designed around camera systems found in Robe BMFL FollowSpot / RoboSpot deployments.

Known models currently tested or observed:

- Samsung SNZ-6320;
- Wisenet / Hanwha XNZ-L6320A family.

The ingest engine should remain generic enough to accept other compatible RTSP/H.264 sources through manual configuration, but v1 workflow assumptions may be optimised for the Robe-supported cameras above.

## Robe profile constraint

For v1, RoboCam-Hub will request:

```text
rtsp://<camera-address>/profile2/media.smp
```

for supported Robe camera installations.

Profile 2 is the documented/default Robe stream path used for this workflow. RoboCam-Hub should not expose arbitrary profile selection in the normal v1 UI.

This is intentional. Logging into or reconfiguring the camera is not considered part of the supported Robe operator workflow, and RoboCam-Hub must avoid encouraging configuration changes that could affect the RoboSpot system.

A future advanced mode may support other profiles for generic cameras, but this is outside the initial scope.

## Primary ingest protocol

Initial ingest target:

- RTSP session control;
- RTP media transport;
- H.264 video;
- UDP transport by default.

### Low-latency principle

For operator monitoring, the newest frame is more valuable than guaranteed delivery of an older frame.

RoboCam-Hub therefore prioritises:

> frame freshness over perfect continuity.

TCP is available as a deliberate per-camera compatibility option where UDP is unreliable or unavailable.

The application must clearly indicate when a source is using TCP because the latency characteristics may differ.

## Proven low-latency GStreamer behaviour

The following behaviour has been manually tested and produces a feed that feels very close to the RoboSpot BaseStation:

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

These settings are the reference behaviour against which the embedded media implementation should be benchmarked.

## Camera stream characteristics observed

A tested Wisenet profile has been observed with approximately:

- H.264;
- 1280 × 720;
- 60 fps;
- VBR;
- GOV / GOP length: 1;
- H.264 High profile;
- CABAC;
- Smart Codec disabled;
- Dynamic GOV disabled.

RoboCam-Hub may report observable characteristics such as codec, resolution, frame rate and stream health, but must not modify encoder settings.

## Camera source model

Network identity and show identity must remain separate.

Example:

```text
Logical name: Spot 1
Device: Wisenet XNZ-L6320A
IP: 10.110.0.12
RTSP: rtsp://10.110.0.12/profile2/media.smp
Camera NIC: USB Ethernet 2
Transport: UDP
```

Each logical camera source should store at minimum:

- stable internal source ID;
- user-facing name, e.g. `Spot 1`;
- camera IP address or hostname;
- derived RTSP profile-2 URL;
- preferred transport: UDP or TCP;
- selected camera NIC;
- enabled / disabled state;
- optional manufacturer/model metadata;
- optional discovered hardware identifier;
- last-known negotiated resolution and frame rate.

Credentials should not be required by the normal Robe workflow. If generic authenticated RTSP support is added, passwords must not be stored in plaintext show files.

## Multiple camera network adapters

V1 must support a **list of enabled camera NICs**, not one global camera NIC.

Typical examples include:

- one built-in Ethernet adapter plus one or more USB Ethernet adapters;
- different camera VLANs presented on different adapters;
- temporary touring adapters that are not always connected;
- camera and NDI networks deliberately separated physically.

### Adapter persistence

RoboCam-Hub should remember previously enabled adapters using stable operating-system interface identifiers where possible rather than only the user-visible adapter name.

At startup:

- present all currently available NICs;
- automatically restore previously enabled NICs that are present;
- retain missing remembered NICs as `Unavailable` rather than deleting them;
- automatically recognise a remembered USB NIC if it is reconnected during the session;
- allow the user to remove a remembered NIC from the configuration explicitly.

The application must not assume the Windows default route is the correct path to a camera.

Where technically possible, discovery and RTSP/RTP sockets should be bound to the selected interface or source address.

## Camera discovery

Discovery is optional and must never be required to operate the application.

V1 discovery goals:

- scan only user-enabled camera NICs;
- discover compatible cameras using ONVIF / WS-Discovery where available;
- display useful metadata such as IP, manufacturer and model where available;
- allow a discovered camera to be assigned to a logical `Spot N` source;
- construct/use the supported profile-2 RTSP path;
- never modify the discovered camera.

### Manual camera entry

Manual entry must always work.

The minimum manual workflow should be:

1. add camera;
2. enter IP address;
3. choose camera NIC;
4. choose UDP or TCP;
5. assign logical name / spot number;
6. connect using profile 2.

The user should not need to type the full RTSP URL for the normal Robe workflow.

## Read-only camera reporting

Where information can be obtained without altering camera configuration, RoboCam-Hub should report it.

Useful fields include:

- manufacturer/model;
- IP address;
- active profile path;
- codec;
- negotiated resolution;
- received/decoded fps;
- transport;
- active NIC;
- last-frame age;
- packet loss indicators where available;
- reconnect count;
- reconnect attempt count;
- successful reconnect count;
- next retry backoff delay;
- stream health.

If configuration parameters such as GOP/GOV can be read safely without requiring unsupported management credentials, they may be shown as diagnostic information.

RoboCam-Hub must not offer camera configuration edits in v1.

## Gate 2B diagnostics and managed interop semantics

The managed application layer consumes low-frequency ingest diagnostics through
the native C ABI. It does not own, decode or transport media frames.

### Camera ID enumeration

- configured logical camera IDs are enumerated by the native engine;
- enumeration returns IDs in deterministic lexical order by camera ID;
- IDs are returned through caller-provided UTF-8 buffers;
- callers must follow count/required-size semantics and retry when the camera
  set changes between calls;
- add/remove operations may occur concurrently with enumeration without
  undefined memory ownership;
- no STL/native-owned string container crosses the ABI boundary.

### Aggregate engine diagnostics

The native engine exposes a point-in-time aggregate snapshot including:

- configured camera count;
- active RTSP session total;
- active decoder total;
- cameras grouped by ingest state (starting, receiving, waiting-to-retry,
  failed, stopped/stopping);
- optional cumulative successful reconnect total.

These diagnostics are designed for low-frequency status presentation and test
invariant validation, not per-frame telemetry.

### Duplicate and re-add semantics

- adding an already configured logical camera ID overwrites its configuration
  in-place for that single logical camera slot;
- duplicate add does not create a second configured camera entry;
- removing a camera releases its ingest ownership and removes it from
  enumeration;
- operations on removed IDs return explicit not-configured/stale failures;
- re-adding a previously removed ID creates a fresh entry with deterministic
  behavior.

### Managed/native ownership rule

Managed code may call add/remove/start/stop/status/enumeration/aggregate
snapshot APIs, but media frame ownership remains native:

- no managed RTSP sessions;
- no managed decode path;
- no per-frame callbacks for ingest video frames;
- no full-frame copy loop across the ABI for diagnostics.

## Gate 3A shared latest-frame fan-out semantics

Gate 3A formalises native fan-out ownership over the per-camera latest-frame
slot.

For one configured logical camera:

```text
1 camera ID
= 1 RTSP session owner
= 1 decode pipeline owner
= 1 latest-frame source
```

Any number of native consumers may read that source concurrently without
creating another session/pipeline.

### Frame lease lifetime

- frame reads use explicit native lease ownership;
- replacing the latest frame never queues history;
- older frame storage remains valid only while at least one lease still
  references it;
- once the final lease releases, replaced storage is reclaimed;
- slow consumers do not back-pressure ingest and do not receive backlog replay.

### View/source ownership foundation

Gate 3A introduces a minimal native View ownership object with source slots
bound by logical camera ID.

- binding a camera ID to a View source slot reuses the same latest-frame source;
- unbinding or destroying a View releases only the binding/consumer reference;
- View operations must never start a new RTSP session or decoder;
- camera removal marks bound consumers as stale/unavailable rather than causing
  undefined ownership behaviour;
- re-adding the same logical camera ID creates a fresh ingest owner and requires
  explicit rebind/re-attach by callers.

### Gate 3A diagnostics additions

Per-camera status now includes:

- direct frame-consumer count;
- bound View-source count;
- total native consumer count.

Engine diagnostics now include:

- View count;
- aggregate direct frame-consumer count;
- aggregate bound View-source count.

These counters are low-frequency ownership diagnostics for tests and state
inspection, not per-frame telemetry.

## Gate 3B decoded-frame format contract

Gate 3B fixes the ingest-to-compositor decoded format to native `RGBA` samples
at the latest-frame boundary.

Current native path:

```text
RTSP/H.264
  -> rtph264depay
  -> h264parse
  -> avdec_h264
  -> videoconvert
  -> capsfilter (video/x-raw,format=RGBA)
  -> queue (max-size-buffers=1, leaky=downstream)
  -> appsink/latest-frame owner
```

Implications:

- composition reads only the current native latest-frame lease;
- no frame payload crosses into managed C#;
- no historical decoded-frame queue is introduced;
- conversion remains bounded and native-only;
- one configured camera still owns exactly one RTSP session and one decoder.

## Connection workflow

For each enabled source, the camera manager should:

1. verify the selected NIC is available;
2. validate the camera address;
3. build the profile-2 RTSP URL;
4. open the RTSP session on the selected NIC;
5. request the configured transport;
6. receive RTP;
7. depayload and decode;
8. publish only the newest usable decoded frame;
9. continuously report health/freshness metrics.

A failure at one stage must be distinguishable from failures at other stages.

## Source states

Suggested detailed runtime states:

```text
Disabled
Waiting for NIC
Connecting
Negotiating RTSP
Receiving
Healthy
Camera unreachable
RTSP failed
No RTP received
Decoder stalled
Frame rate degraded
Stale video
Reconnecting
```

The normal show UI may reduce these to simpler statuses such as `Live`, `Connecting`, `Offline`, `Degraded` and `NIC Missing`, with detailed diagnostics available separately.

## Freshness and queue policy

The ingest system must prevent progressive latency accumulation.

Requirements:

- queues are bounded;
- stale frames are dropped;
- downstream slowdown never creates an unbounded backlog;
- after a stall, a source returns to the newest available frame instead of replaying buffered history;
- one slow or failed source never blocks another source;
- compositor consumers should always receive the newest completed frame available.

## Decoder strategy

Software H.264 decoding is the known-good reference implementation.

Hardware decode should be benchmarked rather than assumed to be lower latency.

Tests should compare:

- software decode;
- available Windows hardware decode paths;
- CPU/GPU usage;
- latency;
- 1, 2, 4, 6 and 8 simultaneous 720p60 feeds;
- behaviour after packet loss;
- recovery after disconnect/reconnect.

## Reconnection behaviour

When a camera disappears, RoboCam-Hub should:

- mark the source stale/lost immediately;
- preserve its logical spot assignment;
- leave every other feed operating;
- retry automatically;
- use bounded reconnect backoff;
- recover without application restart;
- discard old buffered media on recovery;
- return directly to the current live frame.

If the selected camera NIC itself disappears, the source should enter `Waiting for NIC` and resume automatically when that remembered adapter returns.

### Gate 1C reconnect/backoff policy

Gate 1C implements bounded automatic reconnect in the native ingest runtime.

- initial retry delay: 250 ms;
- exponential backoff sequence on consecutive failures: 250, 500, 1000, 2000 ms;
- maximum retry delay: 2000 ms;
- retry backoff resets after live frame receipt resumes;
- explicit Stop and engine destruction interrupt pending backoff waits.

Per retry attempt, the failed pipeline is torn down before a new attempt owns a pipeline. Latest-frame availability is cleared during outage and ownership counters return to zero before waiting/retrying.

Required invariant remains:

```text
active_rtsp_session_count <= 1
active_decoder_count <= 1
```

## Diagnostics per camera

At minimum expose or record:

- logical source name;
- camera address;
- connection state;
- profile path (`profile2/media.smp`);
- UDP/TCP transport;
- active NIC;
- negotiated codec;
- resolution;
- received frame rate;
- decoded frame rate;
- last-frame age;
- dropped-frame count where measurable;
- reconnect count;
- reconnect attempt count;
- successful reconnect count;
- next retry backoff delay;
- packet-loss indicators where measurable;
- decoder type;
- current pipeline state.

Advanced diagnostics should remain available without cluttering the normal operator interface.

## Camera replacement workflow

The logical `Spot N` object must be persistent independently of the physical camera.

If a camera fails:

1. select `Spot N`;
2. assign a replacement discovered/manual camera IP;
3. retain the existing logical spot identity;
4. retain every multiview tile, label and NDI output that references that spot;
5. reconnect without rebuilding layouts.

## Hard safety boundary around camera management

RoboCam-Hub v1 must not:

- factory reset a camera;
- change its IP address;
- alter credentials;
- change encoder/profile settings;
- alter a RoboSpot-required stream profile;
- reboot the camera.

The application is a **consumer and diagnostic viewer**, not a Robe camera configuration utility.

## Remaining design decisions

Still to define through implementation/testing:

1. outage/stale detection thresholds per transport profile and camera family;
2. exact ONVIF discovery support across Samsung and Wisenet generations;
3. stable Windows NIC identity strategy for USB adapters;
4. secure credential approach if authenticated generic cameras are later supported;
5. decoder selection/fallback policy;
6. whether each camera may bind to exactly one NIC or optionally try a user-ordered NIC list;
7. how source latency/health should be estimated and displayed.

## Initial acceptance tests

An ingest implementation is not ready until it can at least:

- connect to known Wisenet and Samsung cameras using profile 2 over UDP;
- manually add a camera using only IP, logical name and NIC selection;
- optionally discover supported cameras;
- maintain 720p60 without progressive latency growth;
- run **8 simultaneous 720p60 feeds** on the agreed reference machine or clearly identify the hardware limit during benchmarking;
- use multiple camera NICs simultaneously;
- remember enabled USB NICs across restarts;
- show an absent remembered NIC without losing its configuration;
- automatically recover when that NIC is reattached;
- disconnect one active camera without affecting any other source;
- reconnect/restart a camera and recover automatically;
- discard stale frames rather than accumulate them;
- verify traffic exits through the selected camera NIC;
- report active transport and camera health;
- perform no camera configuration writes;
- compare end-to-end ingest latency against the manually proven GStreamer reference pipeline.
