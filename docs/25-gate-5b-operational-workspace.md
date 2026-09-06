# 25 — Gate 5B Operational Avalonia Workspace

## Purpose

Gate 5B makes the Gate 5A managed runtime operable through the first functional Avalonia workspace. It is an Edit Mode control surface, not the final layout editor and not a video-preview implementation.

The visible workspace is arranged as:

```text
RoboCam-Hub                                      EDIT MODE
┌─────────────────┬──────────────────────────────────────┐
│ CAMERAS         │ VIEW                                 │
│ add camera      │ Main 2x2 View                        │
│ camera health   │ slot 1          slot 2               │
│ Start / Stop    │ slot 3          slot 4               │
│                 │ Live View Preview                    │
│                 │ Native video preview arrives Gate 5C │
├─────────────────┴──────────────────────────────────────┤
│ OUTPUTS · source name · View · state · Start / Stop    │
└────────────────────────────────────────────────────────┘
```

Every state indicator combines readable text with an icon and colour. Colour is never the only health signal.

## Application boundary

`RoboCamHub.Application` is an Avalonia-independent application layer between the Views and Gate 5A:

```text
MainWindow.axaml
    ↓ compiled bindings
WorkspaceViewModel
├─ CameraItemViewModel[]
├─ ViewWorkspaceViewModel
│  └─ ViewSlotViewModel[4]
└─ OutputItemViewModel[0..1]
    ↓
IWorkspaceRuntimeService / WorkspaceRuntimeService
    ↓
ShowRuntime
    ↓
NativeInterop
    ↓
native engine
```

Avalonia owns only presentation, window lifetime and UI-thread dispatch. The ViewModels contain presentation state and commands but do not own native objects. `WorkspaceRuntimeService` owns the single `ShowRuntime`, resolves its managed Camera/View/Output runtime objects, and remains the sole application-layer orchestration boundary.

The App and Application projects contain no raw P/Invoke declarations, native library names, SafeHandles or native handle types. No video frame or pixel buffer crosses into managed code.

## Workspace creation and definitions

Startup creates one empty in-memory `ViewDefinition` named `Main 2x2 View` through the real `ShowRuntime.AddView()` path. It does not create test or demo cameras. Operators add cameras using a name and absolute RTSP URL; the form constructs a real `CameraDefinition` with a generated stable in-memory ID and calls `ShowRuntime.AddCamera()` through the runtime service.

The bottom form similarly constructs the one Gate 5A-supported `OutputDefinition`, references the fixed View by stable ID, and calls `ShowRuntime.AddOutput()`. Camera and Output configuration is intentionally in-memory until show-file persistence is implemented by a later gate.

## Live slot-assignment state

`ViewDefinition` remains the immutable initial snapshot established by Gate 5A. Each `ViewSlotViewModel` owns separate operator-visible live assignment state:

- assignment changes are sent to `ViewRuntime.BindCameraSource()` or `UnbindSource()` first;
- the ViewModel changes its displayed assignment only after that runtime operation succeeds;
- a failed operation leaves the previous assignment intact and displays a concise inline message;
- every status snapshot reconciles the displayed assignment from `ViewSourceRuntimeStatus.CameraId`, so changes do not become stale relative to the live native View;
- the immutable `ViewDefinition` is never mutated to imitate runtime state.

## Async commands and runtime serialization

Gate 5A control methods are synchronous and may block. `WorkspaceRuntimeService` runs create/start/stop/bind/unbind/status/dispose work on background tasks and serializes access through one bounded semaphore. Avalonia button handlers therefore never invoke those operations directly on the UI thread.

`AsyncCommand` and per-item busy state prevent re-entrant or double-click Start/Stop/Assign/Remove operations. Command enablement also accounts for definition state and current runtime state. Completion, status and error property changes are marshalled through `IUiDispatcher`; the production adapter posts to Avalonia's UI dispatcher.

Normal failures are concise inline operator messages. Detailed exceptions are written through `System.Diagnostics.Trace`; recurring runtime faults do not create modal dialogs.

## Status polling and shutdown

One `StatusPollingService` refreshes the complete workspace approximately three times per second. Each tick requests one low-frequency snapshot covering camera, View/source and Output state. There are no native callbacks and no per-frame managed events.

The polling loop is single-consumer and cannot overlap itself. Runtime service serialization also prevents a status query from racing a control operation at the managed orchestration boundary. Window close cancels and awaits polling, clears every ViewModel reference to the runtime service, then disposes `ShowRuntime` off the UI thread in its Gate 5A dependency order.

## Gate 5B functional workflow

The current application supports:

```text
launch in Edit Mode
→ add in-memory cameras
→ start/stop cameras and observe lifecycle health
→ assign/remove cameras in fixed slots 1–4
→ create one in-memory Output for Main 2x2 View
→ start/stop NDI and observe sender health
→ show receiver count only when the SDK marks it known
→ shut down cleanly
```

Reconnect is not shown because Gate 5A has no distinct managed reconnect operation. Native camera retry remains visible through the real `Waiting to Retry` state.

## Current limitations

> Historical Gate 5B boundary: Gate 5C replaced the preview placeholder and Gate
> 5D replaced the fixed View/single Output workspace with collection-based View
> and Output controls. See `docs/26-gate-5c-native-view-preview.md` and
> `docs/27-gate-5d-multiple-views-outputs.md`.

Gate 5B deliberately does not provide:

- native or managed video preview; the central surface is an explicit Gate 5C placeholder;
- persistence, saved shows, autostart reconciliation or production startup configuration;
- camera edit/delete/discovery or thumbnail support;
- more than the current fixed 2x2 View and one managed Output;
- drag/drop, crop, resize, rotation, freeform composition or final visual polish;
- Show Mode, fullscreen monitoring, NIC selection, scaling, licensing or NDI audio;
- modal diagnostics or a dedicated logging viewer.

No native ABI, compositor, ingest/decode ownership or NDI sender implementation changes are introduced by this gate. The single-ingest invariant remains owned and enforced by the existing native runtime.
