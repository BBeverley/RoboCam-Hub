# 14 — Show Mode and Fullscreen Monitoring

## Purpose

Define how RoboCam-Hub transitions from layout creation into normal show operation without introducing a separate operational workspace.

## Gate 6E implementation status

Gate 6E implements the global session-only Edit/Show capability state and one
fullscreen local monitor window. Entering Show Mode cancels pending canvas
gestures and property drafts, restores the last applied scene, clears editor
selection and disables all scene, View-creation, camera-assignment and Output-
configuration entry points. Camera and Output status polling, local View
selection, fullscreen monitoring and Output Start/Stop/Restart remain enabled.

Fullscreen transfers the one existing Gate 5C preview attachment between the
normal workspace host and a borderless fullscreen host. It never creates a
View, compositor, ingest session, decoder or Output, and local View selection
continues to be independent from stable Output `ViewId` routing. The minimal
fullscreen control strip remains deliberately slim and visible; Escape and F11
exit. See
`docs/32-gate-6e-show-mode-fullscreen.md` for the exact implementation and
validation boundary.

The operating model is intentionally simple:

- the normal View workspace is also the show-operation workspace;
- **Show Mode** locks View editing while retaining useful monitoring controls;
- the Camera Source Rail may remain expanded or be collapsed;
- the selected View preview automatically resizes to the available application space;
- **Fullscreen Mode** provides a clean local monitor view with only View-selection controls;
- NDI Outputs are always clean compositor outputs and never contain editor, operator, settings, source-rail or application UI.

## Core mode model

RoboCam-Hub should not maintain a conceptually separate `Operate` page and `Layout` page for the same View.

Instead, one View workspace has two states:

```text
EDIT MODE
Show Mode OFF
    ↓
Free-form canvas editing available

SHOW MODE
Show Mode ON
    ↓
Same workspace and same selected View
Canvas editing locked
Operational monitoring remains available
```

A third presentation state is available locally:

```text
FULLSCREEN MODE
    ↓
Selected View fills the application display
Minimal View-selection controls only
```

This keeps the workflow predictable: build the View, enable Show Mode, and operate the same workspace.

## Standard workspace — Edit Mode

With Show Mode disabled, the user has the complete layout editor:

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ RoboCam-Hub   Show: Tour 2026       Spots A       Show Mode [OFF]    ⚙   │
├────────────────┬───────────────────────────────────────┬───────────────────┤
│ CAMERAS        │                                       │ PROPERTIES        │
│                │                                       │                   │
│ ● Spot 1       │                                       │ Selected Element  │
│   10.110.0.12  │               VIEW CANVAS             │ X / Y             │
│   31ms · NIC A │                                       │ W / H             │
│                │      ┌────────────┬────────────┐       │ Rotation          │
│ ● Spot 2       │      │   Spot 1   │   Spot 2   │       │ Crop / Fit        │
│   10.110.0.13  │      ├────────────┼────────────┤       │                   │
│   34ms · NIC A │      │   Spot 3   │   Spot 4   │       │                   │
│                │      └────────────┴────────────┘       │                   │
│ ...            │                                       │                   │
├────────────────┴───────────────────────────────────────┴───────────────────┤
│ Views: [ Spots A ] [ Spots B ] [ All Spots ]                 + New View  │
└────────────────────────────────────────────────────────────────────────────┘
```

The Camera Source Rail remains collapsible.

## Standard workspace — Show Mode

Enabling Show Mode does not navigate to a new page.

It locks the editing controls around the same View.

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ RoboCam-Hub   Show: Tour 2026       Spots A       🔒 Show Mode [ON]   ⚙  │
├────────────────┬───────────────────────────────────────────────────────────┤
│ CAMERAS        │                                                           │
│                │                                                           │
│ ● Spot 1       │                                                           │
│   10.110.0.12  │                      VIEW                                 │
│   31ms · NIC A │                                                           │
│                │         ┌────────────────┬────────────────┐                │
│ ● Spot 2       │         │     Spot 1     │     Spot 2     │                │
│   10.110.0.13  │         ├────────────────┼────────────────┤                │
│   34ms · NIC A │         │     Spot 3     │     Spot 4     │                │
│                │         └────────────────┴────────────────┘                │
│ ...            │                                                           │
├────────────────┴───────────────────────────────────────────────────────────┤
│ Views: [ Spots A ] [ Spots B ] [ All Spots ]      Fullscreen             │
└────────────────────────────────────────────────────────────────────────────┘
```

The Properties/editor panel should be hidden in Show Mode because it no longer provides useful editing controls.

The View therefore gains additional space automatically.

## Camera Source Rail behaviour

The Camera Source Rail remains available in Show Mode.

The user chooses whether it is expanded or collapsed.

Expanded:

```text
● Spot 1
  10.110.0.12 · 31 ms
  RoboSpot VLAN · 60 fps
```

Collapsed:

```text
│ ● │
│ ● │
│ ● │
│ ● │
```

When the rail is collapsed, the View preview automatically grows into the recovered horizontal space.

When the rail is expanded, the View preview automatically shrinks while preserving the View's aspect ratio.

The rail state should not affect NDI output dimensions or composition.

## Responsive View preview

The View displayed inside the application should be a responsive preview of the configured View canvas.

Rules:

- preserve the View canvas aspect ratio;
- fit within the currently available workspace;
- centre the preview within unused space;
- never crop the View merely because the application window changes size;
- application-window resizing changes only preview scale, not View geometry;
- collapsing/opening the Camera Rail triggers immediate preview reflow;
- entering/leaving Show Mode may reclaim editor-panel space and resize the preview;
- changing local preview size must never alter NDI output resolution.

Example:

```text
Application wide + rail collapsed
        ↓
large 16:9 preview

Application narrower + rail expanded
        ↓
smaller 16:9 preview
```

## Show Mode restrictions

When Show Mode is ON, disable View-design operations including:

- drag/move;
- resize;
- rotate;
- crop changes;
- source reassignment by drag/drop;
- add/delete/duplicate element;
- layer reorder;
- text/image/shape styling;
- template application;
- transform keyboard shortcuts.

Operational actions remain available, including:

- View selection;
- Camera Rail expand/collapse;
- camera health monitoring;
- camera highlighting in the current View;
- right-click Camera Properties/Diagnostics;
- reconnect actions;
- NDI output Start/Stop where exposed;
- Settings access;
- Fullscreen Mode;
- leaving Show Mode.

## Fullscreen Mode

Fullscreen Mode is a local display mode for the selected View.

It should remove the normal application shell, editor controls, source rail and settings controls.

The selected View fills as much of the display as possible while preserving its aspect ratio.

Reference:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                                                                          │
│                                                                          │
│                        SELECTED VIEW                                     │
│                                                                          │
│             ┌────────────────────┬────────────────────┐                   │
│             │       Spot 1       │       Spot 2       │                   │
│             ├────────────────────┼────────────────────┤                   │
│             │       Spot 3       │       Spot 4       │                   │
│             └────────────────────┴────────────────────┘                   │
│                                                                          │
│                                                                          │
│ [ Spots A ]   [ Spots B ]   [ All Spots ]                      [ Exit ] │
└──────────────────────────────────────────────────────────────────────────┘
```

The View-selection controls should be deliberately minimal.

Potential behaviour:

- controls remain visible in a slim bottom overlay; or
- controls auto-hide after a short idle period and reappear on mouse movement.

The exact auto-hide behaviour can be tested during implementation.

Fullscreen mode is intended for local monitoring and secondary displays. It does not create a new NDI output.

## View switching

The user should be able to switch local Views quickly in both Show Mode and Fullscreen Mode.

Example configured Views:

```text
Spots A
Spots B
All Spots
```

Switching the local selected View does **not** retarget or alter any configured NDI Output.

For example:

```text
Local Preview: Spots B

NDI Output A → Spots A
NDI Output B → Spots B
```

The operator may inspect `Spots B` locally while both NDI feeds continue unchanged.

## Clean-output boundary

This is a hard architectural rule.

An NDI Output receives only the rendered View frame from the compositor.

It must never use desktop capture, window capture, screen capture or capture of the application preview.

Therefore the following are always local UI only and can never appear in NDI output:

- Camera Source Rail;
- health dots;
- IP/NIC/frame-age data;
- selection outlines;
- transform handles;
- snapping guides;
- Properties panel;
- toolbars;
- View selector buttons;
- Show Mode indicator;
- Settings windows;
- mouse pointer;
- application chrome;
- Fullscreen navigation buttons.

Conceptually:

```text
                     ┌─ Local App Preview + UI
View Compositor ─────┤
                     └─ Clean View Frame ──> NDI Sender
```

The compositor output is the authoritative image for the NDI sender.

## NDI output independence

Each NDI Output remains attached to its configured View, independent of what the operator is currently viewing locally.

Example 8-camera setup:

```text
View: Spots A (Spot 1–4)
    ↓
NDI Output: ROBOCAM - SPOTS A

View: Spots B (Spot 5–8)
    ↓
NDI Output: ROBOCAM - SPOTS B
```

The operator can locally select either View, collapse/expand the Camera Rail or enter Fullscreen Mode without affecting either NDI stream.

## Window resizing and multiple monitors

The application should respond cleanly to arbitrary desktop window sizes.

Potential user workflows include:

- normal laptop window with Camera Rail expanded;
- maximised laptop window with Camera Rail collapsed;
- Fullscreen Mode on the laptop display;
- Fullscreen Mode on an attached production monitor;
- normal application window on one display while NDI consumers run elsewhere on the network.

The exact multi-monitor window-placement persistence can be defined later, but Fullscreen Mode should ideally target the current display and remember the last-used display where practical.

## Initial acceptance tests

- enable Show Mode without navigating away from the current View;
- verify the canvas cannot be edited while Show Mode is active;
- expand/collapse the Camera Rail in Show Mode;
- verify the local View preview resizes automatically;
- resize the application window and preserve View aspect ratio;
- switch local Views without changing configured NDI Output assignments;
- enter Fullscreen Mode;
- switch Views from Fullscreen Mode;
- exit Fullscreen Mode cleanly;
- verify selection/editor/application UI never appears in NDI;
- verify collapsing panels does not alter NDI dimensions;
- verify NDI continues normally while the application preview is switched or resized.

## Decisions currently adopted

- The normal View/layout workspace doubles as the Operate screen.
- Show Mode is a lock state on that workspace rather than a separate page.
- Camera Source Rail can remain open or be collapsed during operation.
- The View preview responsively resizes to the available application/window space.
- Fullscreen Mode shows the selected View with only minimal View-selection controls.
- Switching the local View does not alter NDI Output/View assignment.
- NDI Outputs always contain only the configured View render and never any operator/application UI.
