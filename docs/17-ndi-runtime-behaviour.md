# 17 — NDI Runtime Behaviour

## Purpose

Define how NDI Outputs behave at runtime in RoboCam-Hub: creation, naming, startup, stop/start, View changes, scaling, NIC loss, recovery, status reporting and failure isolation.

This document builds on `07-ndi-output.md` and focuses on operator-facing behaviour rather than the low-level NDI implementation.

## Core rule

An NDI Output publishes only the clean rendered View assigned to it.

Application UI must never be captured into NDI.

The following are always local-only and must never appear in an NDI frame:

- Camera Source Rail;
- View editor selection boxes and transform handles;
- Properties panels;
- Settings;
- Show Mode indicators;
- Fullscreen View selector controls;
- mouse pointer;
- diagnostics overlays unless an explicit future View element is created for that purpose.

Conceptually:

```text
View Compositor
      ├─ Local application preview + UI
      └─ Clean View frame → Output processing → NDI sender
```

## Output object

Each NDI Output is an independent object containing:

- internal output ID;
- user-facing output name;
- NDI source name;
- referenced View;
- output resolution;
- output frame rate;
- NDI mode;
- logical NDI network role(s);
- automatic-start preference;
- enabled/disabled state;
- runtime sender state.

A View may have zero, one or multiple Outputs.

Multiple Outputs may reference the same View.

## Native ownership and backpressure semantics

The direct native sender is intentionally attached to the clean composed View frame, not to the camera ingest pipeline. The sender consumes only the latest composed frame snapshot and drops stale frames rather than queueing them. This preserves the repository guarantee that a slow NDI consumer does not become a source-side bottleneck. The sender also remains isolated from the camera lifecycle: RTSP sessions and decoders are created once per configured logical camera and stay stable while the sender is started or stopped.

The deterministic native proof therefore covers:

- four configured cameras keep four RTSP sessions and four decoder pipelines;
- sender start/stop does not change those totals;
- newest-frame semantics prevent backlog growth on the sender path;
- sender teardown remains safe during View destruction and engine shutdown.

Sender diagnostics in this mode are sender-core/backend metrics only. They are measured from the sender worker loop and latest-composed-frame sequence progression, not from wire-level NDI interoperability.

Key deterministic semantics:

- `send_fps_milli` is measured over a bounded accepted-frame window; it is `0` until enough samples exist;
- worker ticks are tracked separately from unique composed sequences observed;
- `sent_frame_count` increments only when the backend accepts a frame publish attempt;
- dropped/skipped counts are sequence-aware where deterministically inferable (missing frame, duplicate sequence tick, observed sequence gaps);
- `receiver_count` is `0`/unknown unless the official SDK backend reports a real value.

This implementation is the proof boundary for Gate 4A until the official NDI SDK is installed and live receiver validation is performed.

## SDK and live validation status

This repository intentionally does not check in any proprietary NDI SDK content. The production integration path is to install the official NDI SDK on the build host and allow CMake to discover it through the standard vendor installation variables or prefixes. Real-time publish/discovery validation remains a host dependency and is not considered a passing Gate 4A result in CI without an installed official SDK and a known-good receiver on the same network.

As of the current environment, the official NDI SDK is not installed locally, so live NDI discovery, source name validation, and receiver-side frame verification remain deferred until an approved SDK installation is available.

## Naming

Each Output has two related names.

Example:

```text
Output Name:      Spots A
NDI Source Name:  ROBOCAM - SPOTS A
```

The Output Name is used inside RoboCam-Hub.

The NDI Source Name is what receivers such as grandMA3 see.

Default generation:

```text
<configured prefix> + <output name>
```

Default prefix:

```text
ROBOCAM -
```

The NDI Source Name remains editable.

RoboCam-Hub should warn if two enabled local Outputs would advertise the same NDI source name.

## Startup behaviour

Each Output has:

```text
Start Automatically [On/Off]
```

Recommended default for Outputs created through the New Show wizard: `On`.

When a show opens:

1. the application always opens in Edit Mode;
2. camera/network mappings are resolved;
3. Outputs marked Start Automatically attempt to start when their required View and NDI network mapping are available;
4. an Output with missing prerequisites waits rather than repeatedly throwing modal errors.

Show Mode does not control whether NDI Outputs are running.

An Output may therefore be broadcasting while the View is being edited in Edit Mode. Edits to that View are reflected in the clean NDI frame as they occur.

## Waiting states on startup

An automatic Output should not be considered failed merely because a machine-specific NIC mapping has not yet been resolved.

Example states:

```text
Waiting for Network Mapping
Waiting for NIC
Waiting for View
Starting
Broadcasting
```

When the missing prerequisite becomes available, the Output should start automatically if `Start Automatically` remains enabled.

## Manual controls

Users should be able to start and stop each Output independently.

Suggested controls:

```text
[ Start ]
[ Stop ]
[ Restart ]
```

`Restart` is useful after a network change or receiver problem without requiring the Output to be deleted/recreated.

Optional global actions:

```text
Start All Outputs
Stop All Outputs
Restart All Outputs
```

Global actions should be accessible from Settings / Output management rather than dominate the normal View workspace.

## Changing the referenced View

An Output references a View by stable View ID, not by name.

Example:

```text
ROBOCAM - SPOTS A
View: Spots A
```

Changing the referenced View should update the Output without requiring a new Output object.

If the change can be performed without recreating the NDI sender, do so.

If the underlying NDI sender must restart, the UI should clearly indicate that the change will momentarily interrupt the source.

## Editing a live View

RoboCam-Hub does not maintain separate staged/live copies of a View.

If a View is referenced by an active NDI Output and Show Mode is OFF, edits are live.

Show Mode is the protection mechanism against accidental layout edits during a performance.

The UI should show that a View is currently being published, for example:

```text
Spots A    ● 1 NDI Output Live
```

This is informational rather than a separate editing mode.

## Output resolution

View resolution and NDI Output resolution may differ.

Example:

```text
View:       1920×1080 @ 60
Output A:   1920×1080 @ 60
Output B:   1280×720  @ 60
```

When resolution differs, scaling occurs after View composition.

The Output should preserve the full View aspect ratio by default.

If the target aspect ratio differs from the View, the application should not silently stretch the image. Initial policy should be one of:

- Fit with letterbox/pillarbox; or
- require the user to choose an explicit scaling mode.

Default recommendation: `Fit`, preserving aspect ratio.

Possible output scaling modes:

```text
Fit
Fill / Crop
Stretch
```

`Stretch` should never be the default.

## Output frame rate

For the first release, keeping View and Output frame rates equal is preferred unless benchmarking demonstrates a clear need for independent conversion.

Typical production target:

```text
60 fps View → 60 fps NDI
```

The data model may retain an independent Output frame-rate field so future conversion does not require a schema redesign.

If an Output frame rate differs from the View, conversion must favour freshness and must not introduce a buffered frame backlog.

## NDI mode

Initial production target:

```text
NDI High Bandwidth
```

The goal is minimum practical latency rather than bandwidth efficiency.

HX-family output is not required for the initial implementation.

## Audio

RoboCam-Hub is a followspot camera monitoring application and has no current requirement to send audio.

Initial policy:

```text
Video-only NDI output
```

Do not invent or capture system audio.

If the NDI SDK or receivers require an audio-related stream state, use the minimum valid no-audio behaviour supported by the implementation.

## NDI network selection

The portable Show references logical NDI network roles rather than Windows adapter IDs.

Example:

```text
Output: ROBOCAM - SPOTS A
NDI Network Role: NDI Network A
```

On a particular machine:

```text
NDI Network A → Intel I225 / MA3 Network
```

The exact NDI SDK mechanism for constraining sender traffic to selected Windows NICs remains an implementation-validation item.

The user-facing model should support one or more NDI network selections per Output if the final SDK implementation can do so predictably.

If the SDK cannot safely bind one logical sender across multiple selected NICs, RoboCam-Hub may implement multiple internal sender instances while presenting them as one Output configuration where that remains operationally clear.

## NIC loss

If the active NDI NIC disappears while an Output is broadcasting:

- do not stop camera ingest;
- do not stop other Outputs;
- do not block the compositor;
- stop attempting to queue stale frames for that Output;
- mark the affected Output unavailable/degraded;
- wait for the configured network role/NIC to become available again;
- recover automatically when practical.

Suggested operator status:

```text
● Broadcasting
▲ Waiting for NIC
● Broadcasting
```

The Output should return using the same NDI name and referenced View after recovery.

## Missing NIC on show load

A remembered logical NDI role whose physical adapter is absent should remain visible.

Example:

```text
ROBOCAM - SPOTS A
Waiting for NIC
NDI Network A → MA3 Network [Missing]
```

The user can either:

- reconnect the remembered adapter; or
- remap the logical network role to another local adapter.

No Output or View recreation is required.

## Sender failure

A sender-level error should affect only that Output.

Recovery sequence:

1. stop/release the failed sender instance;
2. keep compositor and source pipelines running;
3. retry/recreate the sender using a bounded retry policy;
4. show a concise operator state;
5. record detailed errors in Diagnostics.

No unbounded retry loops that consume CPU or memory.

## Freshness and back-pressure

NDI Output follows the same central latency policy as camera ingest.

Fresh frames are more valuable than old complete frames.

Requirements:

- no unbounded Output queue;
- use the newest complete View frame available;
- if sender timing falls behind, drop stale pending frames;
- one slow/failing Output must not block another;
- NDI send timing must not back-pressure camera ingest;
- local preview must not back-pressure NDI.

## Output status UI

Normal UI should remain concise.

Recommended primary states:

```text
Green   Broadcasting
Amber   Degraded / Waiting for NIC / recovering
Red     Failed / stopped unexpectedly
Grey    Stopped / disabled
```

Colour must be accompanied by text/status icon semantics.

Example compact list:

```text
● Spots A
  ROBOCAM - SPOTS A
  Spots A · 1080p60 · NDI Network A

● Spots B
  ROBOCAM - SPOTS B
  Spots B · 1080p60 · NDI Network A

○ FOH Backup
  Stopped
```

The View workspace does not require a permanently open Output rail if Output configuration is handled through the Settings modal. A compact global output-health indicator in the application header is sufficient during normal work.

## Diagnostics

Per Output diagnostics should include, where measurable:

- Output Name;
- NDI Source Name;
- referenced View;
- target resolution;
- target frame rate;
- actual send frame rate;
- sender state;
- logical network role;
- resolved physical NIC(s);
- sender restarts;
- dropped Output frames;
- render-to-send timing;
- scale/conversion time;
- last successful frame send;
- recent errors;
- receiver count if reliably exposed by the SDK.

Receiver count should not be a core health criterion because an NDI source can be perfectly healthy with zero receivers attached.

## Receiver count

If reliably available, receiver count may be shown as secondary information:

```text
Receivers: 2
```

Do not mark an Output unhealthy merely because the receiver count is zero.

If receiver identity/count is not reliably available through the selected SDK API, omit it from the normal UI rather than approximating it.

## Duplicate Output

Duplicating an Output should copy:

- referenced View;
- resolution;
- frame rate;
- NDI mode;
- scaling behaviour;
- auto-start preference;
- network role selection.

The new Output must receive a unique internal ID and should generate a new NDI Source Name that the user can edit.

Typical use:

```text
Spots A → ROBOCAM - SPOTS A / NDI Network A
Duplicate
Spots A Backup → ROBOCAM - SPOTS A BACKUP / NDI Network B
```

## Delete Output

Deleting a broadcasting Output should require confirmation because it removes a live network source.

Stopping an Output does not delete it.

## Show Mode interaction

Show Mode controls View editing, not NDI transport.

When Show Mode is enabled:

- running Outputs remain running;
- stopped Outputs remain stopped;
- NDI status continues updating;
- clean View frames continue changing as live camera frames change;
- View geometry/layout cannot be accidentally edited;
- operational Output recovery/restart controls may remain available through Settings.

## Fullscreen local monitoring

Switching the local Fullscreen View has no effect on NDI Output assignments.

Example:

```text
Local Fullscreen: Spots B

NDI Output A → Spots A
NDI Output B → Spots B
```

Both Outputs continue unchanged.

## Recommended initial defaults

Initial proposed defaults:

```text
NDI Mode:            High Bandwidth
Resolution:          Match View
Frame Rate:          Match View
Scaling:             Fit / preserve aspect ratio
Start Automatically: On for wizard-created Outputs
Audio:               None
NDI Name Prefix:     ROBOCAM -
```

These defaults should make the common touring workflow work without unnecessary configuration.

## Initial acceptance tests

- create an Output from a View;
- publish clean View video with no application UI included;
- auto-start a configured Output when its Show opens and required NIC mapping exists;
- wait cleanly when the NDI NIC is missing;
- recover automatically after reconnecting the NIC;
- start/stop/restart one Output without affecting any other Output;
- run two 1080p60 High Bandwidth Outputs simultaneously;
- run two different Views simultaneously;
- publish the same View from two independent Output objects;
- edit a live View in Edit Mode and see the clean output update;
- enable Show Mode and confirm layout editing is blocked while NDI remains live;
- change the local Fullscreen View without changing any NDI Output;
- scale a 1080p View to a 720p Output;
- verify no Output frame backlog accumulates during a sender/network fault;
- duplicate an Output and assign a different NDI network role;
- confirm zero connected receivers does not mark an Output unhealthy.

## Decisions adopted

- NDI publishes the clean configured View only, never the application UI.
- Outputs remain separate from Views.
- Multiple simultaneous Outputs are a core requirement.
- NDI High Bandwidth is the initial target.
- Initial output is video-only.
- Show Mode does not start or stop Outputs.
- Fullscreen local View switching does not change Output assignments.
- Output failure must be isolated from camera ingest and other Outputs.
- Output queues must favour freshness and drop stale frames rather than accumulating latency.
- Logical NDI network roles are stored with the portable Show; physical NIC mappings are machine-specific.

## Still to validate technically

1. Exact current NDI SDK behaviour for sender NIC binding on Windows.
2. Whether multi-NIC publication should be one sender or multiple internal sender instances.
3. Reliable receiver-count support.
4. Practical supported maximum simultaneous High Bandwidth Outputs on target hardware.
5. grandMA3 receive/render latency and simultaneous-source performance.
