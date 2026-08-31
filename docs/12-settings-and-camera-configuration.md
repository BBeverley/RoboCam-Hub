# 12 — Settings and Camera Configuration

## Purpose

Define a clean configuration workflow for RoboCam-Hub while keeping camera-side settings read-only.

RoboCam-Hub should configure how it ingests camera streams and how it publishes NDI outputs. It should not attempt to edit camera encoder, network or credential settings on the physical RoboSpot cameras.

## Recommended overall workflow

Use a single **Settings modal** rather than a full application page for general configuration.

The modal should be large enough to behave like a proper workspace, but remain visually separate from the live canvas.

Recommended structure:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ Settings                                                            ✕   │
├──────────────────┬───────────────────────────────────────────────────────┤
│ General          │                                                       │
│ Cameras          │                                                       │
│ NDI Outputs      │                  ACTIVE SETTINGS                      │
│ Network          │                                                       │
│ Performance      │                                                       │
│ Diagnostics      │                                                       │
│                  │                                                       │
└──────────────────┴───────────────────────────────────────────────────────┘
```

This keeps the main application uncluttered while making all configuration accessible from one consistent location.

## General settings

### Appearance

Theme options:

- **Auto** — default; follow Windows/system appearance;
- **Light**;
- **Dark**.

The selected theme should apply immediately to the application UI and should not affect NDI output styling, which is defined by each View.

### Other general options

Suggested v1 fields:

- reopen last show on startup;
- autosave show configuration;
- default show file location;
- UI scale if required;
- confirmation preferences for disruptive operations.

## Camera configuration philosophy

RoboCam-Hub must remain **read-only toward physical camera configuration**.

The user may configure only the local ingest relationship to a camera.

Editable ingest settings include:

- logical camera name;
- camera IP / hostname;
- selected ingest NIC;
- RTSP transport: UDP / TCP;
- enable / disable source;
- optional credentials when required for stream access;
- advanced custom RTSP URL only where explicitly enabled;
- reconnect policy if exposed per source.

For the normal Robe workflow, the application should construct the standard Profile 2 RTSP path rather than exposing arbitrary camera profile configuration.

The following must remain read-only / unavailable from RoboCam-Hub:

- camera IP configuration;
- subnet/gateway configuration on the camera;
- encoder resolution/frame-rate settings;
- codec settings;
- GOV/GOP settings;
- camera user accounts / credential management;
- camera reboot;
- camera factory reset;
- profile changes;
- any other device-side configuration write.

## Cameras section layout

Use a combined two-pane camera configuration view inside the Settings modal.

```text
┌──────────────────┬──────────────────────────────────────────────────────┐
│ CAMERAS          │ Spot 3                                               │
│                  │                                                      │
│ ● Spot 1         │ Name             Spot 3                              │
│ ● Spot 2         │ Camera IP        10.110.0.14                         │
│ > Spot 3         │ Ingest NIC       RoboSpot VLAN 110 ▼                 │
│ ● Spot 4         │ Transport        UDP ▼                               │
│                  │ Profile          profile2/media.smp  [read-only]     │
│ + Add Camera     │                                                      │
│ Discover         │ Stream Status    Healthy                             │
│                  │ Resolution       1280×720                            │
│                  │ Frame Rate       60 fps                              │
│                  │ Freshness        34 ms                               │
└──────────────────┴──────────────────────────────────────────────────────┘
```

The left list remains compact and follows logical source order.

The right pane combines editable ingest fields with useful read-only runtime information.

## Camera row behaviour

From the View editor Camera Source Rail:

- single click highlights that logical camera in the current View;
- right-click → `Properties…` opens Settings directly to `Cameras` with that camera selected;
- right-click → `Reconnect` retries ingest without opening Settings;
- right-click → `Diagnostics…` opens the camera diagnostics context.

This should work even if the Settings modal was previously closed.

## Add Camera workflow

`+ Add Camera` should create a new logical source and immediately select it in the right pane.

Minimum fields required before connecting:

- logical name;
- camera IP / hostname;
- ingest NIC;
- transport.

Suggested defaults:

- logical name auto-proposed as next available `Spot N`;
- transport defaults to UDP;
- standard Robe Profile 2 RTSP path applied automatically;
- source enabled once required fields are valid.

## Discovery workflow

Discovery should be optional and operate only on selected camera NICs.

Suggested flow:

```text
Settings → Cameras → Discover
```

Then show a temporary discovery panel:

```text
Discovered Cameras

10.110.0.12   Wisenet XNZ-L6320A   [ Add as Spot 1 ]
10.110.0.13   Samsung SNZ-6320     [ Add as Spot 2 ]
```

Discovery must not change any settings on the camera.

## Read-only camera runtime information

Useful fields in the camera settings pane:

- connection state;
- camera manufacturer/model where available;
- resolved IP;
- RTSP profile/path;
- codec;
- negotiated resolution;
- expected and current frame rate;
- ingest freshness/frame-age metric;
- active NIC;
- current transport;
- dropped-frame / packet-loss indicator where measurable;
- reconnect state;
- last successful frame timestamp;
- health reason.

Detailed counters belong under Diagnostics rather than the normal camera settings pane.

## Network section

Network settings should manage host-side NIC selection and friendly naming only.

Two logical groups:

- Camera ingest adapters;
- NDI output adapters.

Users may select multiple adapters in each group.

Each NIC should show:

- friendly alias;
- Windows adapter name;
- IPv4 addresses;
- connection state;
- stable adapter identifier where available.

Users may assign aliases such as:

```text
RoboSpot VLAN 110
Lighting Network
NDI Backup
```

Previously selected USB NICs should remain remembered as `Missing` when disconnected and automatically re-associate when they return.

## NDI Outputs section

NDI configuration remains fully editable because this is application-side output configuration.

Users should be able to create and edit NDI Output objects with:

- internal output name;
- NDI source name;
- referenced View;
- output enabled/disabled;
- output resolution;
- frame rate;
- NDI mode;
- selected NDI NIC(s), subject to final SDK behaviour;
- auto-start with show option.

Reference layout:

```text
┌──────────────────┬──────────────────────────────────────────────────────┐
│ NDI OUTPUTS      │ Spots A                                              │
│                  │                                                      │
│ ● Spots A        │ Output Name      Spots A                             │
│ ● Spots B        │ NDI Name         ROBOCAM - SPOTS A                   │
│                  │ View             Spots A ▼                           │
│ + New Output     │ Resolution       1920×1080 ▼                         │
│                  │ Frame Rate       60 ▼                                │
│                  │ Mode             High Bandwidth                      │
│                  │ NICs             [x] Lighting Network                │
│                  │                  [ ] NDI Backup                      │
└──────────────────┴──────────────────────────────────────────────────────┘
```

Changes that require restarting an active NDI sender should display a concise warning before being applied.

## Performance section

Performance settings should be deliberately limited in normal UI.

Possible v1 options:

- decoder mode: Auto / Software / available hardware backend;
- compositor backend if multiple stable options exist;
- local preview quality / frame-rate reduction;
- performance warnings enabled/disabled.

Advanced implementation-specific controls should remain hidden unless they provide genuine operational value.

## Diagnostics section

Diagnostics is read-only and intended for troubleshooting.

Suggested content:

- camera pipeline state;
- dropped frames;
- packet loss where measurable;
- decode timing;
- frame-age/freshness trends;
- reconnect count;
- selected NIC information;
- NDI sender state;
- current receiver count where available;
- CPU/GPU usage summary;
- logs/export diagnostics action.

## Show Mode interaction

Opening Settings while Show Mode is active should remain allowed.

However:

- View/canvas editing remains locked;
- camera ingest settings that materially alter a live source should show a clear warning before applying;
- NDI output settings that restart or interrupt an active sender should show a clear warning;
- non-disruptive settings such as theme changes may apply immediately.

Do not require leaving Show Mode merely to inspect health or diagnostics.

## Save/apply behaviour

Recommended approach:

- UI-only preferences such as theme apply immediately;
- simple non-disruptive settings may apply immediately;
- source connection changes apply when field validation succeeds and user confirms if the source is active;
- NDI sender changes that require restart use an explicit `Apply` action or confirmation;
- modal may still have a global `Close` button rather than forcing users through a large Save/Cancel transaction.

This avoids one giant settings transaction where unrelated changes are committed together.

## Decisions currently adopted

- General configuration uses a large modal Settings window.
- Theme options are Auto / Light / Dark.
- Auto is the default and follows the system appearance.
- RoboCam-Hub does not modify physical camera settings.
- Users configure only ingest-side camera settings and NDI output settings.
- Camera settings use a combined left-list/right-detail layout.
- Right-clicking a Camera Source Rail entry → `Properties…` deep-links directly to that camera in Settings.
- Camera discovery is read-only and optional.
- Network adapter selection and aliases are application-side settings.
