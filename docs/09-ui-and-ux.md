# 09 — Application UI and UX

## Purpose

Define the user-facing structure of RoboCam-Hub so that setup, operation, view design and diagnostics remain clear during live touring use.

The application should feel like a production tool first and a design tool second. Normal show operation must be quick to read and resistant to accidental changes. Detailed configuration should be available without cluttering the main screen.

## UX principles

RoboCam-Hub should follow these principles:

- dark, high-contrast visual design suitable for control rooms, FOH and stage environments;
- camera and NDI health visible at a glance;
- no unnecessary modal chains during normal operation;
- live video remains visible while changing non-destructive settings where practical;
- destructive or disruptive actions require clear confirmation;
- live operation and layout editing are distinct modes;
- source identity, View design and NDI Output configuration remain separate concepts;
- status colours/icons should always have text equivalents and not rely on colour alone;
- the application must remain usable on a typical touring laptop display;
- UI slowdown must never back-pressure the media pipeline.

## Primary application areas

The application has five primary working areas:

1. **Operate** — day-to-day live show screen.
2. **Cameras** — source assignment, discovery and camera health.
3. **Views** — create and edit visual compositions.
4. **Outputs** — configure and supervise NDI outputs.
5. **Settings** — application, network, camera, NDI, performance and diagnostics settings.

The user should not need to enter a separate application for any of these functions.

## Application shell

The desktop application should use a persistent top-level shell.

Reference layout:

```text
┌───────────────────────────────────────────────────────────────────────┐
│ RoboCam-Hub  [Show: C7RIEL 2026]                    ● LIVE   ⚙       │
├───────────────────────────────────────────────────────────────────────┤
│ Operate | Cameras | Views | Outputs                                  │
├───────────────────────────────────────────────────────────────────────┤
│                                                                       │
│                         CURRENT WORK AREA                             │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

The application shell should provide:

- current show name;
- global camera health summary;
- global NDI output summary;
- settings access;
- clear indication when any output is actively broadcasting;
- optional dirty/unsaved state indication;
- access to show load/save actions.

## Operate screen

The Operate screen is the default landing page during normal show use.

It should prioritise:

- a large preview of one selected View;
- camera source health;
- active NDI output status;
- quick switching between Views;
- quick access to Camera, View and Output configuration without exposing editing controls directly over the preview.

Reference layout:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ RoboCam-Hub   Show: C7RIEL 2026                 8 Cameras   2 NDI LIVE │
├──────────────┬──────────────────────────────────────────┬────────────────┤
│ CAMERAS      │              SELECTED VIEW               │ OUTPUTS        │
│              │                                          │                │
│ ● Spot 1     │     ┌────────────┬────────────┐           │ ● Spots A      │
│ ● Spot 2     │     │   Spot 1   │   Spot 2   │           │ ● Spots B      │
│ ● Spot 3     │     ├────────────┼────────────┤           │                │
│ ● Spot 4     │     │   Spot 3   │   Spot 4   │           │ + Output       │
│ ● Spot 5     │     └────────────┴────────────┘           │                │
│ ● Spot 6     │                                          │                │
│ ● Spot 7     │                                          │                │
│ ● Spot 8     │                                          │                │
│              │                                          │                │
│ + Add        │                                          │                │
│ Discover     │                                          │                │
├──────────────┴──────────────────────────────────────────┴────────────────┤
│ Views: [ Spots A ] [ Spots B ] [ All Spots ]                 + New View │
└──────────────────────────────────────────────────────────────────────────┘
```

### Camera rail

Each logical Spot row should show at minimum:

- logical name;
- health state;
- optional model/IP on hover or expanded detail;
- live / reconnecting / stale / offline state;
- warning indicator if stream characteristics are not ideal.

Normal operation should not expose passwords or detailed RTSP configuration in this rail.

Clicking a Spot opens a detail panel or modal rather than navigating away from the live preview.

### Selected View preview

The central preview should:

- show one selected View at the correct aspect ratio;
- use the same current frame state as the compositor;
- show View name;
- show whether that View is referenced by one or more active outputs;
- offer `Edit View` and `Open Fullscreen Preview` actions;
- remain display-only during Operate mode.

The preview should not include application controls in the actual NDI output frame.

### Output rail

Each Output row should show:

- output name;
- status: broadcasting / stopped / degraded / failed;
- associated View;
- output resolution / frame rate in secondary detail;
- NDI NIC summary;
- receiver count where obtainable and reliable;
- warning state if a selected NIC is unavailable.

Quick actions may include Start, Stop and Open Output Settings.

## Cameras screen

The Cameras screen manages logical Spot assignments and physical camera connections.

Reference structure:

```text
┌──────────────────────────────────────────────────────────────────────┐
│ Cameras                               [ Discover ] [ + Add Manually ]│
├──────────────────────────────────────────────────────────────────────┤
│ Spot 1   ● LIVE    10.110.0.12   XNZ-L6320A   720p60   UDP         │
│ Spot 2   ● LIVE    10.110.0.13   SNZ-6320     720p60   UDP         │
│ Spot 3   ◐ RETRY   10.110.0.14   XNZ-L6320A   —        UDP         │
│ Spot 4   ○ EMPTY   —             —            —        —           │
└──────────────────────────────────────────────────────────────────────┘
```

### Add manually

For the Robe workflow, manual addition should initially focus on camera IP rather than asking users to construct RTSP URLs.

Suggested fields:

- logical Spot name;
- camera IP / hostname;
- selected camera NIC;
- transport: UDP default / TCP explicit fallback;
- optional credentials if required.

For known Robe camera workflows, the application should construct the RTSP path using the supported Profile 2 convention.

An advanced RTSP URL field may exist behind an Advanced option for compatible non-Robe cameras.

### Discovery

Discovery is optional and should operate only on user-selected camera NICs.

Discovered devices should appear in a temporary list and should not automatically become active logical Spots.

Suggested interaction:

```text
Discovered Cameras

10.110.0.12  Wisenet XNZ-L6320A    [ Assign to Spot 1 ▼ ]
10.110.0.13  Samsung SNZ-6320      [ Assign to Spot 2 ▼ ]
```

Assigning a new physical camera to an existing Spot should update every View that references that Spot.

### Camera detail

Selecting a camera opens a detail view containing:

- logical name;
- camera IP;
- model/manufacturer where known;
- active NIC;
- RTSP Profile 2 status;
- transport;
- negotiated codec;
- resolution;
- frame rate;
- stream health;
- last-frame age;
- reconnect count;
- diagnostics shortcut.

If profile/encoder information can be inspected safely without requiring unsupported credentials, display a low-latency assessment. Do not offer camera-side editing in v1.

## Views screen

The Views screen lists reusable compositions.

Reference layout:

```text
┌──────────────────────────────────────────────────────────────────────┐
│ Views                                                     + New View │
├──────────────────────────────────────────────────────────────────────┤
│ ┌────────────────┐  ┌────────────────┐  ┌────────────────┐          │
│ │  [thumbnail]   │  │  [thumbnail]   │  │  [thumbnail]   │          │
│ │ Spots A        │  │ Spots B        │  │ All Spots      │          │
│ │ 1920×1080 60  │  │ 1920×1080 60  │  │ 1920×1080 60  │          │
│ │ 1 output       │  │ 1 output       │  │ 0 outputs      │          │
│ └────────────────┘  └────────────────┘  └────────────────┘          │
└──────────────────────────────────────────────────────────────────────┘
```

Actions:

- Open / Edit;
- Duplicate;
- Rename;
- Delete;
- Create Output from View;
- Save as Template later.

### New View wizard

The New View workflow should be fast.

Initial options:

- Blank;
- 1 Camera;
- 2-Up;
- 2×2;
- 3×2;
- 4×2;
- Split 8 Cameras.

`Split 8 Cameras` is a convenience workflow that may create two matching Views:

- `Spots A` containing Spots 1–4;
- `Spots B` containing Spots 5–8.

It may optionally offer to create matching NDI Outputs at the same time.

## View editor

The View editor is a dedicated design mode.

Reference layout:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ ← Views     Spots A                         1920×1080  60 fps    Save   │
├──────────────┬──────────────────────────────────────────┬────────────────┤
│ ELEMENTS     │                                          │ PROPERTIES     │
│              │                                          │                │
│ Cameras      │                                          │ Camera: Spot 2 │
│ + Spot       │              DESIGN CANVAS               │ X: 960         │
│              │                                          │ Y: 0           │
│ Graphics     │    ┌────────────┬────────────┐            │ W: 960         │
│ + Text       │    │   Spot 1   │   Spot 2   │            │ H: 540         │
│ + Image      │    ├────────────┼────────────┤            │ Fit: Cover     │
│ + Rectangle  │    │   Spot 3   │   Spot 4   │            │                │
│              │    └────────────┴────────────┘            │                │
├──────────────┴──────────────────────────────────────────┴────────────────┤
│ Layers: Background | Spot1 | Spot2 | Spot3 | Spot4 | Labels | Logo      │
└──────────────────────────────────────────────────────────────────────────┘
```

### Element palette

V1 element types:

- Camera;
- Text;
- Image;
- Rectangle / frame;
- Background.

### Camera element interaction

Camera elements should allow:

- selecting a logical Spot;
- drag / resize;
- numeric X/Y/W/H;
- contain / cover / stretch;
- crop/zoom where supported;
- border;
- label visibility;
- custom label text;
- source-lost behaviour.

### Text element interaction

Text should support enough styling for clean touring layouts without becoming a full desktop publishing system.

V1 fields:

- text;
- font;
- size;
- weight;
- alignment;
- position / size;
- text opacity;
- optional background block;
- optional dynamic Spot name token later.

### Image element interaction

Images are intended for logos, backgrounds and tour branding.

The View should reference a managed show asset rather than only an external absolute path.

### Rectangle / frame interaction

Rectangles should provide:

- fill;
- opacity;
- border/stroke;
- border width;
- corner radius if supported;
- layer order.

### Layers

The editor needs a lightweight explicit layer system.

Required actions:

- select;
- reorder;
- hide/show in editor where useful;
- lock/unlock;
- duplicate;
- delete.

### Snapping and alignment

V1 should include:

- snap to canvas edges;
- snap to centre;
- snap to nearby object edges;
- horizontal / vertical align;
- distribute evenly where practical;
- keyboard nudge.

The editor must still allow exact numeric placement for repeatable layouts.

## Outputs screen

The Outputs screen manages independent NDI senders.

Reference layout:

```text
┌──────────────────────────────────────────────────────────────────────┐
│ NDI Outputs                                              + New Output │
├──────────────────────────────────────────────────────────────────────┤
│ ● Spots A     ROBOCAM - SPOTS A    Spots A    1080p60    Lighting A │
│ ● Spots B     ROBOCAM - SPOTS B    Spots B    1080p60    Lighting A │
│ ○ Backup      ROBOCAM - BACKUP     Spots A    720p60     Lighting B │
└──────────────────────────────────────────────────────────────────────┘
```

### Output object

Each Output stores:

- internal ID;
- user-facing name;
- NDI source name;
- referenced View;
- enabled / started state;
- resolution;
- frame rate;
- NDI mode;
- one or more selected NDI NICs if the final NDI networking implementation supports this predictably;
- status;
- receiver information where available.

### Output editor

Suggested fields:

```text
Name:              Spots A
View:              Spots A
NDI Source Name:   ROBOCAM - SPOTS A
Resolution:        1920×1080
Frame Rate:        60
NDI Mode:          High Bandwidth
Output NICs:       [x] Lighting A
                   [ ] Lighting B
                   [ ] Wi-Fi
Start with Show:   [x]
```

### Output/View independence

A View and an Output are separate objects.

This allows:

```text
View: Spots A
  ├─ Output: ROBOCAM - SPOTS A / Lighting A
  └─ Output: ROBOCAM - SPOTS A BACKUP / Lighting B
```

and:

```text
View: Spots A        → Output A
View: Spots B        → Output B
```

for an 8-camera show split across two 2×2 NDI feeds.

### Duplicate Output

Users should be able to duplicate an Output and then change only its:

- NDI name;
- NIC selection;
- resolution;
- referenced View.

This supports quick redundant or alternate-monitor configurations.

## Settings

Settings should be a full page or substantial modal with a left-hand category list.

Categories:

### General

- startup behaviour;
- reopen last show;
- autosave;
- confirmation preferences;
- UI scale where needed;
- default show storage location.

### Network Adapters

Separate sections for Camera NICs and NDI NICs.

The app should list all currently available adapters and allow multiple selections.

For each adapter, show:

- friendly name;
- interface description;
- IPv4 address(es);
- connection state;
- adapter type where obtainable;
- stable operating-system identifier.

The application should remember the stable identifiers of previously selected NICs.

If a remembered USB NIC is not present at startup, retain it as `Missing` rather than silently forgetting it.

When it reconnects, the application should re-associate it automatically where the operating system exposes a stable identifier.

### Camera defaults

- default RTSP transport: UDP;
- explicit TCP fallback policy;
- Profile 2 path convention;
- reconnect policy;
- discovery enabled / disabled;
- default camera NIC selections;
- advanced/manual RTSP support.

### NDI defaults

- default NDI mode: High Bandwidth;
- naming prefix, e.g. `ROBOCAM -`;
- default resolution;
- default frame rate;
- default NDI NIC selections;
- start Outputs automatically with Show option.

### Performance

- decoder preference: Auto / Software / available hardware options;
- compositor preference where exposed;
- local preview quality / frame-rate reduction if required;
- diagnostics overlay;
- performance warnings.

Advanced performance settings should not be presented as casual tuning controls if poor choices could increase latency.

### Diagnostics

- camera pipeline health;
- received/decoded FPS;
- source freshness;
- packet loss where measurable;
- decoder type;
- compositor frame rate;
- output frame rate;
- NDI sender state;
- selected NICs;
- CPU / GPU use;
- application logs;
- export diagnostic bundle later.

## Show file UX

The application is local-first and should make the current Show explicit.

A Show contains at least:

- logical Spots / camera assignments;
- camera NIC selections;
- Views;
- imported View assets;
- NDI Outputs;
- show-specific settings.

Application-wide defaults remain separate from show-specific configuration.

Suggested show actions:

- New Show;
- Open Show;
- Save;
- Save As;
- Duplicate Show;
- Recent Shows.

The application should autosave safely during configuration but should still expose an explicit saved state to avoid ambiguity.

## Live-change behaviour

Not all settings should behave the same while the application is live.

### Safe live changes

Expected to apply without stopping an Output:

- text changes;
- layout position / size changes;
- image/graphic changes;
- camera assignment changes where pipeline replacement can be seamless;
- View switching in local preview.

### Potentially disruptive changes

Should be clearly flagged before applying:

- NDI resolution change;
- NDI frame-rate change;
- NDI mode change;
- NDI NIC change;
- camera transport change;
- decoder change;
- camera IP change.

The UI should tell the user whether an Output will restart.

## Status model

Use a consistent state vocabulary.

### Camera states

- Live;
- Connecting;
- Reconnecting;
- Stale;
- Offline;
- Authentication Failed;
- Warning.

### NDI Output states

- Broadcasting;
- Starting;
- Stopped;
- Degraded;
- Failed;
- Waiting for NIC.

### Global header summary

Examples:

```text
8 Cameras · 8 Live
2 NDI Outputs · 2 Broadcasting
```

or:

```text
8 Cameras · 7 Live · 1 Offline
2 NDI Outputs · 1 Broadcasting · 1 Waiting for NIC
```

## Error handling UX

Errors should be actionable.

Prefer:

`Spot 4 — No RTP received on USB Camera NIC. Retrying.`

rather than:

`Pipeline Error 47`.

Detailed technical information can live under Diagnostics.

The application should avoid blocking global error dialogs for individual camera failures during a show.

## Fullscreen preview

Any View should be able to open in a clean fullscreen local preview on a selected monitor later if desired.

The fullscreen preview must contain only View content and no application chrome.

This is separate from NDI output and should not be implemented by desktop capture.

## Keyboard shortcuts

Useful initial shortcuts may include:

- `Ctrl+S` Save Show;
- `Ctrl+D` Duplicate selected View element in editor;
- Delete selected editor element;
- Arrow keys nudge selected element;
- Shift + arrow for larger nudge;
- `F11` fullscreen selected View preview where appropriate.

Shortcuts must not make disruptive live actions too easy to trigger accidentally.

## Initial visual direction

The initial concept mockups establish the following direction:

- charcoal/near-black base UI;
- restrained accent colour;
- thin borders and clear spacing;
- large readable source labels;
- minimal visual noise;
- live video remains dominant;
- View branding belongs inside the View canvas, not hard-coded into application chrome.

The concept mockups are directional only and should not be treated as pixel-perfect implementation references.

## V1 acceptance criteria

The UI is considered functionally ready when a user can:

1. create/open a Show;
2. select multiple camera and NDI NICs and have those selections remembered;
3. manually add cameras by IP;
4. optionally discover cameras;
5. assign physical cameras to logical Spots;
6. create Views from presets or blank canvases;
7. design a View using Camera, Text, Image and Rectangle elements;
8. duplicate a View and reassign its camera elements;
9. create multiple independent NDI Outputs;
10. split 8 Spots across two 2×2 Views and two NDI Outputs;
11. start/stop each Output independently;
12. see camera/output health from the Operate screen;
13. recover from a disconnected camera without modal interruption;
14. save and reload the Show with layouts, camera assignments and Outputs intact.

## Open UX decisions

1. Final navigation style: top tabs vs compact left navigation.
2. Whether Settings is a dedicated route/page or modal shell.
3. Whether the Operate screen camera/output rails are resizable or collapsible.
4. Whether Output receiver count is reliable enough to expose as a normal status item.
5. Exact behaviour of live View edits while an NDI Output is broadcasting.
6. Whether Split 8 Cameras creates Outputs automatically or offers this as a checkbox.
7. Whether fullscreen local previews are part of v1 or the first follow-up release.
