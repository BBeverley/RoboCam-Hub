# 27 — Gate 5D Multiple Views and Outputs

## Scope

Gate 5D turns the Gate 5A–5C single-View/single-Output workspace into the first
production-shaped collection model. It supports multiple fixed 2×2 Views,
multiple independently controlled NDI Outputs and safe selection of one local
preview. It does not add freeform layout, persistence, discovery, NIC binding,
output scaling, audio, licensing or managed frame transport.

No native ABI revision or new ADR is required. Existing ABI 1.7 already models
multiple native View and sender handles, ADR 0001 keeps full-resolution media in
C++, and ADR 0002 defines the native preview boundary.

## Runtime object graph

```text
ShowRuntime
├─ CameraRuntime[]           stable CameraDefinition.Id
├─ ViewRuntime[]             stable ViewDefinition.Id
│  ├─ four camera-ID bindings
│  ├─ one native compositor
│  ├─ zero/one selected local preview attachment
│  └─ zero/many OutputRuntime consumers
└─ OutputRuntime[]           stable OutputDefinition.Id
   ├─ immutable referenced ViewId
   ├─ independent desired/actual state
   └─ one native sender handle/worker/backend
```

`WorkspaceRuntimeService` exposes collection snapshots keyed by stable IDs:

```text
Cameras[cameraId]
Views[viewId]
ViewSources[viewId][slotIndex]
Outputs[outputId]
Preview (selected View only)
```

Camera and View definition mutations use the runtime-wide gate. Each Output has
its own lifecycle gate, so a slow start/stop/restart for Output A does not block
Output B. Status snapshots report collections consistently without replacing
operator edit state.

## Ownership and fan-out

One logical camera still owns one native ingest session, decoder and bounded
latest-frame state. Reusing it in another View adds only a reference from the
second compositor. One View still owns one compositor and one latest composed
frame. Reusing it for another Output adds one sender consumer of that same
composed frame; it does not add another compositor.

The local preview and every sender independently acquire a reference-counted
lease to the newest available composed frame. There is no cross-consumer queue.
A slow sender skips superseded sequences and cannot back-pressure the View or
another sender. Managed C# transports only definitions, control calls and
low-frequency status.

## Workspace behavior

- Views and Outputs have stable IDs and user-facing names.
- The selected local View and the View chosen for a new Output are separate
  pending selections.
- Polling actual status does not reset pending camera-slot, preview-View or
  Output-View choices.
- `Show View` commits selection only after native preview switching succeeds.
- Every Output row provides independent Start, Stop and Restart controls plus
  state, receiver count, send FPS, frame age, send duration and skip count.
- Duplicate NDI source names are rejected case-insensitively before native
  sender creation.
- Aggregate RTSP/decoder totals and per-View Output consumer counts are visible
  as low-frequency ownership diagnostics.

Native View destruction refuses while sender/preview leases are active. Managed
`ShowRuntime` disposal deterministically destroys Outputs before Views and Views
before Cameras/engine. Tests also cover engine teardown while multiple senders
are active.

## Deterministic verification

The Gate 5D test additions cover:

1. stable IDs across multiple View and Output definitions;
2. two Views sharing four camera definitions without extra ingest/decode;
3. multiple Outputs targeting different Views;
4. two Outputs targeting one View with the expected consumer count;
5. stop/destroy isolation between Outputs;
6. View-destruction dependency handling;
7. engine teardown with active senders;
8. a deliberately slow sender backend while another sender and the compositor
   continue newest-first;
9. preview switching and preview-switch failure while Outputs stay active;
10. pending selection preservation across polling;
11. case-insensitive duplicate NDI source-name rejection;
12. application collection polling and independent per-Output operations.

The native slow-backend seam is private to tests and does not cross the C ABI.

## Local functional and official-SDK validation

Validation used:

- NDI SDK 6.3.2.0;
- macOS 14.7.1 x86_64;
- NDI Video Monitor 5.2;
- four independent local UDP RTSP/H.264 960×540/60 sources, composited into two
  different 1920×1080/60 Views;
- NDI sender format RGBA, declared 60/1, through the existing direct SDK path
  with no application conversion or explicit full-frame copy;
- loopback RTSP and receiver traffic.

The Avalonia smoke test completed these operations without a crash or forced
termination:

- created two Views and assigned the same four logical cameras in different
  slot orders;
- switched the local preview between both live Views;
- created and simultaneously started `ROBOCAM - SPOTS A` and
  `ROBOCAM - SPOTS B` against different Views;
- NDI Video Monitor discovered both source names and sequentially displayed
  each correct 2×2 composition while both senders stayed active;
- stopped and restarted Output A while Output B, both Views, preview and all
  camera ingest remained live;
- disconnected and reopened the receiver; both senders remained active and the
  selected source resumed without rebuilding ingest/View/sender ownership;
- switched the local preview to View A while the receiver continued showing
  View B, proving preview selection did not change Output routing;
- started `ROBOCAM - SPOTS B BACKUP` against View B and confirmed the same
  existing composition under a second independent sender name;
- retained exactly four RTSP sessions and four decoders; normal close released
  the application graph cleanly.

## Measured performance

The non-preview phase probe used the same Release native library and sources.
It sampled every second and exited normally. CPU is macOS process percentage,
where 100% represents one logical core. RSS includes GStreamer and NDI SDK
working sets inside the process.

| Phase | View A / B render FPS | NDI A / B send FPS | View age A / B | Sender age A / B | Avg/p95 send duration A / B | CPU | RSS trend | Consumers A / B |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 2 Views, no NDI | 60.1 / ~60.0 | — | 6.5 / 8.4 ms | — | — | 151–155% | 379→412 MB, then near-flat | 0 / 0 |
| 2 Views + 1 NDI | 59.8 / 59.8 | 60.2 / — | 9.4 / 7.6 ms | 8.2 / — | 7.21/8.53 ms / — | 192–218% | 450→483 MB | 1 / 0 |
| 2 Views + 2 NDI | 59.9 / 59.9 | 56.4 / 42.0 | 7.3 / 7.1 ms | 3.5 / 4.6 ms | 7.18/8.95 ms / 6.06/7.23 ms | 221–254% | 493→567 MB, final samples near-flat | 1 / 1 |
| 2 Views + preview + 2 NDI | about 60 / 60 | 55.1 / 57.4 at the end | selected preview 0–17 ms | 2 / 9 ms at the end | 10.71/13.63 ms / 10.37/15.17 ms at the end | 270–453% | `ps` RSS 692 MB at 4 min, 1.15 GB at 15.6 min | 1 / 1 |

The two-sender non-preview phase intentionally saturated local encode/decode and
desktop resources unevenly: sender B fell to about 42 fps and skip counters
rose. Both View compositors nevertheless stayed near 60 fps, sender ages stayed
bounded to the newest frame, and RTSP/decoder ownership remained exactly 4/4.
This is the required isolation behavior; it is not a claim that every x86_64 Mac
can sustain two 1080p60 High Bandwidth NDI encodes at full cadence under
simultaneous local source generation and receiver display.

RSS rose at phase transitions as compositors and SDK senders established their
working sets. During the longer preview run, `ps` RSS continued to establish a
high-water mark and fluctuated rather than becoming a convincing plateau: 1.05
GB at 11.6 minutes, 1.14 GB at 14.6 minutes, 1.13 GB at 15.1 minutes and 1.15 GB
at 15.6 minutes. A simultaneous `vmmap -summary` check distinguished this from
equivalent live allocation: physical footprint was 153.8 MB (185.7 MB peak) at
about 14 minutes and 157.3 MB (same 185.7 MB peak) at the end; most apparent RSS
was clean/reclaimable malloc pages. This does not presently indicate
progressively retained live frame data, but it does not prove leak freedom. The
formal four-hour production profiling soak remains deferred.

## Artifacts

Local validation artifacts are preserved in `/tmp`:

- `rch-g5d-phase-probe.log` and `rch-g5d-phase-process.log`;
- `rch-g5d-preview-phase-app.log`,
  `rch-g5d-preview-two-output-process.log` and
  `rch-g5d-preview-two-output-long-process.log`;
- `rch-g5d-preview-two-output-final-process.log`,
  `rch-g5d-preview-vmmap-summary.txt` and
  `rch-g5d-preview-vmmap-summary-final.txt`;
- `rch-g5d-two-outputs-running.png`,
  `rch-g5d-output-a-stopped-b-running.png`,
  `rch-g5d-output-a-restarted.png`,
  `rch-g5d-receiver-disconnected.png`,
  `rch-g5d-receiver-reconnected-live.png`,
  `rch-g5d-preview-switched-output-b-live.png`,
  `rch-g5d-same-view-two-outputs-running.png`,
  `rch-g5d-same-view-backup-receiver.png` and
  `rch-g5d-preview-two-output-diagnostics-start.png` and
  `rch-g5d-preview-two-output-diagnostics-end.png`.

## Remaining limits

- The proof reused four independent local RTSP/H.264 sources in two Views; it
  did not claim eight cameras or four physical cameras.
- Receiver traffic was loopback. Remote NIC behavior and explicit NDI NIC
  binding remain unverified and out of scope.
- Official-SDK runtime behavior on Windows and Apple Silicon remains unverified;
  public CI uses the deterministic backend on those platforms.
- grandMA3 interoperability and a formal four-hour profiling soak remain
  deferred.
- The UI remains the fixed 2×2 operational workspace. Freeform composition,
  persistence, scaling and final visual polish belong to later gates.
