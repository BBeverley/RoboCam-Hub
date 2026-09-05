# 24 — Gate 5A Managed Application Runtime

## Purpose

Gate 5A introduces the managed application/runtime layer above the native media engine. It gives future Avalonia ViewModels production-shaped application objects without moving media ownership into C# or exposing raw native handles to the UI.

The dependency direction is:

```text
Avalonia / ViewModels
        ↓
CameraDefinition / ViewDefinition / OutputDefinition
        ↓
ShowRuntime / CameraRuntime / ViewRuntime / OutputRuntime
        ↓
RoboCamHub.NativeInterop SafeHandle wrappers
        ↓
versioned native C ABI
        ↓
native media engine
```

`RoboCamHub.Domain` contains configuration definitions and does not reference Avalonia, the runtime project, or native interop. `RoboCamHub.Runtime` references Domain and NativeInterop but not Avalonia. The application project references Runtime rather than NativeInterop directly.

## Definition and runtime separation

Definitions are immutable configuration/desired-state snapshots:

- `CameraDefinition` owns a stable logical camera ID, user-facing name, RTSP URL, connection timeout, and `Enabled` desired state;
- `ViewDefinition` owns a stable View ID, user-facing name, and exactly four logical camera-ID assignments for slots 0–3;
- `OutputDefinition` owns a stable Output ID, user-facing name, NDI source name, referenced View ID, and `Enabled` desired state.

Runtime objects expose actual native state separately:

```text
OutputDefinition.Enabled = true
OutputRuntime.GetStatus().State = Starting / Running / Failed / Stopped
```

`Enabled` does not masquerade as current state and does not implement persisted autostart policy in this gate. A disabled camera or Output cannot be started through its runtime object. Runtime status is queried as a low-frequency snapshot and contains no frame payload.

## Ownership graph

`ShowRuntime` creates and owns one native engine and the complete managed runtime graph:

```text
ShowRuntime
├─ NativeEngine
├─ CameraRuntime[] → logical camera ID in the engine registry
├─ ViewRuntime[]   → one SafeHandle-backed native View each
└─ OutputRuntime[] → one SafeHandle-backed native sender resolved through View ID
```

A `CameraRuntime` never opens RTSP itself. It translates start, stop, and status operations to the one engine registry entry keyed by `CameraDefinition.Id`.

A `ViewRuntime` resolves every `ViewDefinition` assignment against `ShowRuntime` by logical camera ID before creating or binding the native View. It never stores an IP address or creates ingest/decode ownership. Gate 5A also exposes explicit runtime bind/unbind operations; these affect the live binding only and do not mutate the immutable definition snapshot.

An `OutputRuntime` resolves `OutputDefinition.ViewId` to the existing `ViewRuntime`, then creates the native sender from that View's private interop wrapper. The native View handle never appears in the public runtime API.

Duplicate Camera, View, and Output IDs are rejected before another native object is created. In particular, applying the same logical camera ID twice cannot create a second configured-camera entry, RTSP session, or decoder.

## Lifetime order

Normal `ShowRuntime.Dispose()` teardown is dependency-safe and idempotent:

```text
Outputs stop and dispose
→ Views dispose and release source bindings
→ cameras stop and are removed from the engine registry
→ native engine disposes
```

Disposing a `ViewRuntime` directly first disposes and removes every dependent `OutputRuntime`, then destroys the View. SafeHandle wrappers provide last-resort native release, while `ShowRuntime` remains the authoritative owner and supplies the deterministic normal order.

## UI boundary

Avalonia code must operate on Domain definitions and Runtime objects. It must not call `LibraryImport` methods, hold a `SafeHandle`, or receive a raw native handle. Native imports remain internal to `RoboCamHub.NativeInterop`; View/sender handles are wrapped and are used only by the runtime adapter.

Control methods are synchronous in Gate 5A and some native teardown operations can block. Future ViewModels must invoke potentially blocking orchestration away from the Avalonia UI thread. This gate deliberately adds no UI or preview surface.

Full-resolution decoded and composed frames remain entirely native. Managed status objects carry identifiers, state, counters, dimensions, cadence, and error names only.

## Gate 5A supported workflow

The managed layer can now perform:

```text
create ShowRuntime
→ add four CameraDefinitions
→ start each CameraRuntime
→ add one fixed 2×2 ViewDefinition
→ resolve and bind slots 0–3 by camera ID
→ add one OutputDefinition referencing the View ID
→ start OutputRuntime
→ query camera/View/Output/aggregate status
→ stop or dispose in dependency order
```

## Current limitations

Gate 5A intentionally supports only the current spike surface:

- fixed four-slot 2×2 View assignments;
- one managed Output runtime per ShowRuntime;
- current native 1920×1080, nominal 60 fps NDI sender behavior;
- explicit start/stop calls, without show-file persistence or autostart reconciliation;
- no Avalonia UI, local preview, camera discovery, NIC selection, scaling, audio, licensing, or polished error presentation;
- no managed full-frame transport;
- no production multi-output manager or eight-camera orchestration.

No new native ABI or ADR is introduced. The existing ABI 1.7 and ADR 0001 ownership boundary are sufficient for this gate.
