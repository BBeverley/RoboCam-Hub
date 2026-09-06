# 32 — Gate 6E Show Mode and fullscreen operator workflow

## Scope and result

Gate 6E adds the operator-safe application states used during a live show:

```text
one existing workspace
├─ Edit Mode: scene and configuration capabilities enabled
└─ Show Mode: global editor lock, runtime operations continue

one selected View
└─ one existing native preview attachment
   ├─ normal workspace host
   └─ fullscreen host (attachment transferred, never duplicated)
```

Show Mode is not a staged/live pair, second scene or durable show property.
Every new application workspace starts in Edit Mode. The gate introduces no
native ABI, compositor, ingest, decoder, NDI, persistence or licensing change.

## Shared capability model

`WorkspaceCapabilities` is the single application-layer policy source. It
exposes the current `WorkspaceMode` plus explicit capabilities:

- `CanEditScene`;
- `CanCreateView`;
- `CanEditCameraAssignments`;
- `CanConfigureOutputs`;
- `CanOperateOutputs`;
- `CanSwitchPreviewView`;
- `CanUseFullscreen`.

Scene/View/camera/output configuration capabilities are enabled only in Edit
Mode. Output operation, local View selection and fullscreen capabilities remain
enabled in both modes. ViewModels derive command enablement from this shared
state, and mutation methods enforce the same capabilities so hiding or
disabling an Avalonia control is not the safety boundary.

Entering Show Mode synchronously notifies every View editor and legacy slot
assignment adapter. Each editor cancels its pending pointer transform, rebuilds
the schematic element from the last applied immutable scene, discards any
camera or visual property draft and clears its selection. The Avalonia canvas
also releases pointer capture. No native scene apply occurs during this
transition, so an in-progress gesture cannot commit after the lock becomes
active. Open property windows are closed by the application shell.

Leaving Show Mode restores the capabilities without recreating the workspace.
Selected View, runtime camera state, Output state and Output routing survive the
round trip. Selection is intentionally cleared on entry rather than retained as
hidden edit state.

## Operator presentation

The top bar exposes adjacent Edit Mode and Show Mode actions plus a persistent,
colour-differentiated mode badge. In Show Mode the editor toolbar and schematic
canvas are removed from the layout and the clean native preview expands into
the available space. Camera and Output health remain visible. Camera Start/Stop
and Output Start/Stop/Restart remain operational; add/assignment and Output
configuration controls are disabled.

Delete, Backspace, arrow nudge and Command/Ctrl+D are rejected both by the
canvas and the editor capability boundary. Context-menu mutations and direct
ViewModel calls are rejected by the same policy.

## Fullscreen architecture

The fullscreen monitor is one borderless Avalonia window containing one
`NativeViewPreviewHost` and a separate minimal control row. Because the native
host has the ADR 0002 airspace boundary, the controls are never composited into
the View pixels and cannot enter NDI.

Transition order is deterministic:

```text
enter fullscreen
→ mark local fullscreen presentation active
→ detach normal preview attachment
→ attach selected View preview to fullscreen native host

exit fullscreen
→ detach fullscreen preview attachment
→ clear local fullscreen presentation state
→ reattach selected View preview to normal native host
```

Only the preview presentation surface is recreated. The selected `ViewRuntime`,
its one compositor, camera ingest/decoders and all Output sender runtimes remain
owned and running. Switching the fullscreen selection uses the existing
`SwitchPreviewView` path and does not mutate any `OutputDefinition.ViewId`.

The control row shows the current View, permits safe local View switching and
exits fullscreen. It remains deliberately slim and visible because pointer
movement over the ADR 0002 native-host airspace is not reliably delivered to
Avalonia; auto-hiding it would make pointer reveal unsafe. Escape is handled at
the window tunnel; on platforms whose window manager consumes Escape first,
the resulting fullscreen-state exit closes the monitor and restores the normal
host. F11 also toggles out. Closing the
application closes the fullscreen window,
detaches preview, stops polling and then follows the existing runtime teardown
order.

## Deterministic validation

Managed Gate 6E regressions cover:

- initial Edit Mode and capability values;
- synchronous rollback of a pending transform and property draft;
- rejection of drag, property, add, duplicate, delete, reorder, Z-order and
  keyboard-nudge mutation entry points in Show Mode;
- disabled View creation, camera assignment and Output configuration;
- continued Output Start/Stop/Restart availability;
- continued polling and local View switching with unchanged Output routing;
- stable RTSP/decoder and View totals across fullscreen state and preview-host
  transfer;
- no Output restart during fullscreen transfer;
- Escape state handling, edit re-enable, new-workspace Edit default and clean
  disposal from Show Mode/fullscreen.

Existing Gate 6A–6D Domain, NativeInterop, Runtime, Application and native
tests remain the regression boundary for scene, preview, compositor, ingest and
NDI ownership.

## macOS manual validation

The Release application was exercised on 2026-09-06 on macOS 14.7.1 x86_64
with the installed official NDI SDK 6.3.2.0. The default 1920×1080 View had no
configured cameras, so the native missing-source composition was used rather
than claiming a real-camera proof.

The operator created and started one official-SDK Output, entered Show Mode,
confirmed the editor canvas/toolbars and creation/configuration affordances
were removed or disabled, entered the independent fullscreen Space, exited via
the persistent control, restarted the Output in Show Mode, returned to Edit
Mode and closed the application normally. The Output remained `Running` across
the Show Mode and fullscreen transitions without a sender restart. The explicit
Restart action remained available and returned the Output to `Running`.

Observed local diagnostics were approximately 59.9–60.1 View fps and 29.5–30.0
preview fps. NDI reported 60.5 fps before and through the mode/fullscreen
transition; immediately after the deliberate sender restart and while UI
automation/screenshots were active it reported approximately 43–45 fps before
recovering. There were no configured camera owners, so RTSP-session/decoder
totals remained exactly 0/0. Five active-Output process samples measured
104.6–109.7% CPU and 381–446 MiB RSS. These short samples are workflow/cadence
sanity evidence, not a performance benchmark or leak proof, and no receiver was
connected.

The first UI pass exposed two implementation awkwardnesses that were corrected:
an owned monitor window did not enter a true macOS fullscreen Space, and an
auto-hidden control could not reliably receive pointer-reveal events across the
native-host airspace. The final monitor is independent, truly fullscreen and
keeps the slim control strip visible. Preview attachment returned to the normal
workspace after each exit and the application exited normally from both Edit
and Show Mode. Synthetic macOS accessibility input did not conclusively deliver
Escape to the Avalonia window; the tunnelled Escape handler and session-state
behavior remain deterministically covered, while a physical-key confirmation
is still required in the next hands-on hardware pass.

## Current limitations and manual boundary

- Gate 6E provides one fullscreen monitor window on the current display. Saved
  multi-monitor placement and multiple fullscreen monitors are out of scope.
- The minimal control strip remains visible. Reliable pointer-reveal over the
  cross-platform native-host airspace would require additional platform input
  plumbing and is deferred.
- The current Gate 5C CPU/native presentation costs remain unchanged. This gate
  does not optimize the compositor or introduce GPU sharing.
- Real-camera, official NDI receiver, cadence, CPU/RSS and platform-specific
  fullscreen observations require the documented manual validation run; normal
  deterministic tests prove ownership and routing behavior but do not replace
  hardware evidence.
