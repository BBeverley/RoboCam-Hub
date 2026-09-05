# 26 — Gate 5C Native View Preview

## Scope and result

Gate 5C replaces the central Gate 5B placeholder with the actual native
1920×1080 composed View. The surrounding single-View operational workspace is
unchanged. This gate does not add editor features, another ingest path, another
decoder, another compositor, managed frame transport or a second NDI output.

## Architecture

The durable host decision is recorded in ADR 0002:

```text
existing native View compositor
  ├─ existing bounded NDI sender worker
  └─ native latest-frame preview presenter
       ├─ Windows: Avalonia host HWND → child HWND → GDI
       └─ macOS:  Avalonia host NSView → child NSView → Core Graphics
```

Avalonia's `NativeControlHost` owns the outer platform host. A small app adapter
passes its typed host identity through Application and Runtime to NativeInterop.
The adapter does not retain a frame address. NativeInterop wraps the additive C
ABI preview handle with `SafeHandle`; native code retains shared View state for
the life of the attachment.

The ABI is additive version 1.8. Its versioned caller-sized config and status
structures contain a fixed-width platform host value, IDs, state and
low-frequency counters only. No frame payload, C++ type, STL type or exception
crosses the ABI.

## Frame ownership and presentation

Each OS paint acquires the existing native `LatestFrame` composed lease, maps it
read-only, presents it synchronously and releases it. macOS creates a transient
`CGImage` whose provider refers directly to the mapped RGBA bytes. Windows gives
the mapped RGBA bytes directly to `StretchDIBits` with explicit channel masks.
RoboCam-Hub performs no additional application full-frame copy or color
conversion in either presenter. OS display upload/scaling work may still occur.

The presenter maintains no queue and no catch-up loop. A 30 fps invalidation
cadence is used while the View and NDI remain independently targeted at 60 fps.
When paints are delayed or coalesced, the next paint leases only the newest
composed frame. Sequence gaps feed the skipped counter.

## Lifecycle

Normal ownership is:

```text
ShowRuntime
  → ViewRuntime
      ├─ ViewPreviewRuntime
      └─ OutputRuntime
```

Window shutdown detaches preview before Output, View and engine teardown. The
native attachment also tolerates out-of-order View or engine destruction: it
retains the View state, observes removal atomically and reports `Preview Failed`
instead of dereferencing released media state. Native UI resources are removed
on their owning UI thread. Repeated host recreation creates and destroys only a
preview surface; camera, decoder, View, compositor and NDI ownership are not
rebuilt.

## Diagnostics and operator states

The workspace polls these local-only values at the existing low-frequency
status cadence:

- `Preview Starting`, `Preview Live`, `Preview Waiting for View` or
  `Preview Failed`;
- attachment and selected View ID;
- last presented sequence and frame age;
- presentation fps and target fps;
- presented and skipped frame counts;
- surface recreation count and last result.

Failures are displayed inline and never use a modal dialog. Avalonia diagnostics
sit outside the native-host region because native controls have an airspace
boundary.

## Deterministic validation

The native test seam substitutes a private fake platform presenter while
compiling the real engine implementation into the test executable. It is not
part of the shared library or production ABI. The tests cover:

- unchanged RTSP, decoder, View and NDI ownership on attach;
- caller-size canaries for preview config/status;
- 50 repeated attach/detach cycles with zero retained fake surfaces;
- active View destruction and engine destruction;
- a 250 ms slow presenter while View and NDI sequences continue;
- sequence gaps proving newest-frame selection instead of backlog draining;
- surface-recreation reporting and safe teardown.

Managed tests cover Runtime dependency order, preview switching/reattachment,
workspace shutdown, state mapping, inline failures and the absence of P/Invoke
or native media handles in the ViewModel layer.

## Manual preview and performance evidence

The local Release smoke test ran on macOS 14.7.1 (23H222), x86_64, on an
8-core Intel Core i9 MacBookPro16,1 with 32 GB RAM. Four independent local
960×540/60 H.264 RTSP sources were bound to the existing 1920×1080 View. They
were test sources rather than physical RoboSpot cameras. The official NDI SDK
6.3.2.0 and NDI Video Monitor 5.2 supplied the simultaneous output proof.

The native NSView preview visibly showed four distinct, changing quadrants and
preserved the View aspect ratio while the Avalonia window was resized. Resizes
advanced the surface-recreation diagnostic without changing camera or View
ownership. NDI Video Monitor discovered `ROBOCAM - SPOTS A` and showed the same
2×2 content. Stopping one camera changed only that slot to `Frozen — Last Good
Frame`; the other three slots, preview and NDI continued. Restarting the camera
returned all four slots to `Live` without recreating the sender. Stopping and
restarting NDI advanced the preview sequence from 3,949 to 4,041 throughout.
The application then closed normally.

The final comparison used a clean Release native library. CPU is macOS process
CPU, where 100% is approximately one logical core, and RSS is resident memory.
The samples are short functional/performance observations, not soak or leak
proof:

| Path | Cadence and age | CPU | RSS observation |
| --- | --- | ---: | ---: |
| View only | View 59.88–60.52 fps; age 0–16 ms | 93.5–95.1% | 216→257 MB during 30-second warm-up |
| View + preview | View 60.0 fps; preview 29.6–30.0 fps; age 3–11 ms | 178.3–190.6% | 514→543 MB during the first minute |
| View + preview + live NDI receiver | View initially 59.9 fps and 52.0 fps at the final sample; preview 29.3 then 20.3 fps; NDI 54.7 then 51.5 fps; age 2–11 ms | 368.8–383.1% | 657–664 MB over the final 30-second sample |

The View-only control held exactly four RTSP sessions and four decoders and
released both totals to zero. The combined UI showed the four corresponding
camera and source states as `Receiving`/`Live`; the deterministic ownership test
separately proves that preview attach changes neither total and does not create
another View or NDI sender.

The all-local combined case was CPU-bound: even with VideoToolbox hardware H.264
encoding, the four-source generator consumed roughly 93–185% CPU and NDI Video
Monitor consumed up to roughly 83% in addition to RoboCam-Hub. An initial
software-encoded source run was more constrained still (about 403% in the source
generator). Under that saturation, cadence fell but frame age stayed low,
sequences continued and no component failed or accumulated a presentation
backlog. A View+NDI native control with no preview showed NDI cadence varying
between roughly 43 and 60 fps under the same changing desktop load, so the
combined result did not reveal a preview-specific NDI stall. The obvious current
cost is CPU presentation/scaling of the 1920×1080 RGBA frame; a GPU compositor or
shared texture remains outside Gate 5C.

Longer sampling with View + preview and no NDI showed RSS fluctuating between
approximately 501 and 545 MB in its final three minutes, including both rises
and falls. That is consistent with warm-up/high-water caching rather than a
strict monotonic trend, but it does not establish leak freedom. A longer
production profiling soak remains required.

Local artifacts from this validation were written to `/tmp`:

- `rch-g5c-2x2-debug.png`, `rch-g5c-outage-debug.png` and
  `rch-g5c-reconnect-debug.png`;
- `rch-g5c-view-only-release.log`;
- `rch-g5c-view-ndi-release-hwsource.log` and
  `rch-g5c-view-ndi-receiver-release.log`.

## Current limitations

- `NativeControlHost` has an airspace boundary; Avalonia controls cannot overlay
  the video region.
- The current CPU compositor means GDI/Core Graphics may perform OS-owned
  scaling or upload work. A GPU rewrite is not part of this gate.
- Color-management tuning, HDR and fullscreen monitoring remain deferred.
- Windows runtime behavior cannot be claimed from macOS validation; Windows x64
  is compiled and tested by CI.
- macOS arm64 is the first-class CI target. Local runtime measurements may be on
  a different Mac architecture and must be labelled accurately.
- The manual proof used local deterministic sources and loopback NDI rather than
  four physical cameras or a remote receiver.
