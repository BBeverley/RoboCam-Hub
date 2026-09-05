# ADR 0001 — Native / Managed Interop ABI

## Status

Accepted.

## Context

RoboCam-Hub uses Avalonia / C# for the desktop application layer and a native C++20 media core for RTSP ingest, GStreamer decode, frame ownership, composition and NDI output.

The boundary between these layers must remain stable, cross-platform and suitable for AI-assisted development. It must not encourage full-resolution video frames to be copied repeatedly between native and managed memory.

The application must also preserve the hard invariant that each configured logical camera owns at most one RoboCam-Hub RTSP session and one decoder pipeline, regardless of how many Views or outputs consume that camera.

## Decision

Expose the native media core through a **plain versioned C ABI** implemented by the C++ library and consumed by .NET through **P/Invoke** behind a managed `RoboCamHub.NativeInterop` wrapper.

The ABI is a control/status interface, not a per-frame media transport.

Conceptually:

```text
Avalonia / C# application
        ↓
RoboCamHub.NativeInterop
        ↓ P/Invoke
Versioned plain C ABI
        ↓
C++20 Media Core
├─ camera registry
├─ GStreamer ingest/decode
├─ latest-frame state
├─ compositor
├─ NDI senders
└─ diagnostics
```

## ABI principles

The exported ABI must:

- use `extern "C"` exports;
- use fixed-width integer types;
- use opaque handles instead of C++ object pointers exposed as types;
- use explicit ownership rules;
- return status/error codes rather than throwing exceptions across the boundary;
- use caller-provided buffers or explicit native allocation/free pairs where strings/blobs are unavoidable;
- include struct size/version fields for forward compatibility where structured values cross the ABI;
- avoid STL containers, C++ classes, RTTI-dependent layouts and exceptions across the boundary;
- avoid platform-specific types in the common ABI unless wrapped in explicitly platform-specific extension functions.

## Initial handle model

Use an opaque engine handle owned by the native library:

```c
rch_engine_handle
```

The managed wrapper creates one engine instance for the application process and disposes it deterministically.

Camera, View and Output identity should use stable IDs supplied by the application/domain layer rather than exposing raw internal object addresses as durable identifiers.

## Initial API categories

The exact function names can evolve during Gate 0/1, but the ABI should be organised around these operations:

```text
Engine lifecycle
- create engine
- destroy engine
- query ABI version

Camera control
- add/configure camera
- remove camera
- reconnect camera
- enable/disable camera

View control
- create/update/remove View definitions

NDI output control
- create/update/remove output
- session start/stop/restart where applicable

Status / diagnostics
- camera status snapshot
- output status snapshot
- runtime snapshot
- active RTSP session count
- active decoder count
- consumer count
- last error information
```

## Single-ingest invariant visibility

The native API must expose enough diagnostics for automated tests to assert:

```text
active_rtsp_session_count(camera_id) <= 1
active_decoder_count(camera_id) <= 1
```

Adding Views, preview consumers, fullscreen consumers or NDI outputs must not increase those values.

This observability is part of the architecture, not optional debug decoration.

## Strings and encoding

Use UTF-8 for textual data crossing the ABI unless a future platform-specific API has a documented reason to do otherwise.

Do not expose `std::string`, .NET strings, Objective-C objects or Windows wide-string ownership directly across the common ABI.

## Callbacks

Callbacks from native to managed code are permitted only for **low-frequency events/state notifications** where they materially improve responsiveness.

Examples:

- camera state changed;
- NIC state changed;
- output state changed;
- engine fatal error;
- configuration apply result.

Do not emit a managed callback for every decoded video frame.

High-frequency diagnostics should normally be collected natively and returned as a snapshot when requested.

Managed callback delegates must be rooted for the entire registration lifetime, and unregister/dispose ordering must be deterministic.

## Media-frame rule

Full-resolution decoded video frames remain owned by the native media/rendering layer.

Forbidden default architecture:

```text
GStreamer decode
→ native frame
→ copy every frame into C#
→ managed processing
→ copy frame back into native
→ compositor / NDI
```

Gate 5C implements the Avalonia preview through the native-backed platform host
mechanism selected in ADR 0002. Full frames remain native-owned; managed code
carries only a typed host identity and low-frequency status. A future GPU
compositor may replace the platform presenter behind that ownership boundary.

## Threading

P/Invoke calls that may block on network/media work must not execute synchronously on the Avalonia UI thread.

The native core owns its worker/media threads. Control calls should preferably enqueue or apply bounded configuration work and return deterministic results.

Any callback into managed code must document its calling-thread behaviour. The managed wrapper is responsible for dispatching UI-facing updates onto the Avalonia UI thread.

## Error model

C ABI functions return a stable result enum/code.

Detailed error information may be queried separately and should preserve meaningful categories such as:

- invalid argument;
- invalid/stale handle;
- duplicate camera/config conflict;
- RTSP failure;
- decoder failure;
- NIC unavailable;
- compositor failure;
- NDI failure;
- unsupported operation;
- internal error.

Native exceptions must be caught inside the library and converted to ABI-safe error results.

## Versioning

Expose an ABI version query and keep the managed wrapper aware of the compatible native ABI range.

Breaking changes to exported function signatures or struct layouts require an explicit ABI version change and migration plan.

Prefer additive extension over breaking replacement while the application is under active development.

## Library naming

Use one logical native library name from managed code and platform-appropriate packaging/resolution underneath.

Indicative names:

```text
Windows: robocamhub_native.dll
macOS:   librobocamhub_native.dylib
```

The managed resolver may map the logical import name to packaged platform files.

## Consequences

### Benefits

- C ABI is stable across C++ compiler/language boundaries;
- P/Invoke is built into .NET and requires little additional runtime machinery;
- the media core remains usable independently of Avalonia;
- AI agents have a clear subsystem boundary;
- high-frequency frame ownership stays native;
- Windows and macOS share one conceptual contract;
- unit/integration tests can use a fake/native test implementation behind the same concepts.

### Costs

- requires explicit marshaling definitions;
- lifetime/ownership must be carefully documented;
- platform preview adapters require explicit UI-thread-affine lifecycle code;
- ABI evolution requires discipline.

These costs are preferable to exposing C++ classes directly or allowing the managed UI layer to become part of the real-time media path.

## Rejected alternatives

### C++/CLI

Rejected because it is not an appropriate shared Windows/macOS strategy.

### Exposing C++ classes directly

Rejected because ABI stability, compiler compatibility, ownership and exception behaviour become fragile.

### Per-frame P/Invoke copying

Rejected because it creates unnecessary copies/GC pressure and undermines the native media-core boundary.

### Running the native engine as a separate process immediately

Deferred. Stronger process isolation could be introduced later if crash/fault evidence justifies it, but it adds IPC and deployment complexity before the media spike proves it is needed.

## Follow-up ADRs

Expected later decisions include:

- compositor GPU backend;
- native event/status transport details if callbacks prove insufficient;
- optional worker-process isolation if real-world stability warrants it.
