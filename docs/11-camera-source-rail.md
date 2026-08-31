# 11 — Camera Source Rail

## Purpose

Define the compact camera/source rail used throughout RoboCam-Hub, especially within the View editor and Operate screen.

The rail should provide immediate camera health and essential source information without consuming space with live thumbnails. The design priority is fast visual scanning during setup and show operation.

## Core design

The Camera Source Rail is a collapsible left-side panel.

It lists configured logical camera sources such as:

```text
● Spot 1
  10.110.0.12
  34 ms · Camera NIC A · 60 fps

● Spot 2
  10.110.0.13
  41 ms · Camera NIC A · 60 fps

● Spot 3
  10.110.0.14
  126 ms · USB Camera NIC · 52 fps
```

No live preview thumbnails are shown in the rail.

## Health indicator

Each source row begins with a small coloured status dot.

### Green — healthy

Camera is online and operating within expected thresholds.

Typical conditions:

- RTSP/RTP session active;
- recent frames arriving normally;
- received/decoded frame rate close to expected rate;
- packet loss / dropped frames below warning threshold;
- latency/freshness within normal bounds.

### Amber — degraded

Camera remains usable but one or more health metrics are outside normal thresholds.

Examples:

- sustained frame drops;
- unstable packet delivery;
- received frame rate materially below expected rate;
- freshness/latency elevated;
- reconnect attempts occurring intermittently;
- network jitter or loss exceeding warning threshold.

Amber must mean `online but degraded`, not merely `connecting`.

### Red — offline

No usable live camera stream is available.

Examples:

- camera unreachable;
- RTSP session failed;
- no RTP received;
- decoder stopped;
- source stale beyond the offline threshold;
- selected NIC unavailable and no path exists to the camera.

### Transitional states

Short transitional states such as Connecting or Reconnecting should use text/substatus rather than introducing many additional dot colours.

Example:

```text
● Spot 3
  Reconnecting…
```

The dot may remain red until usable frames return.

## Row layout

Expanded rail row:

```text
● Spot 1
  10.110.0.12
  34 ms · Camera NIC A · 60 fps
```

Recommended information hierarchy:

1. Health dot.
2. Logical camera name.
3. IP address.
4. Current latency/freshness estimate.
5. Active NIC.
6. Current received/decoded frame rate.

The camera name should be visually dominant.

Secondary information should use smaller, lower-contrast text.

## Recommended compact metrics

The rail should avoid becoming a diagnostics panel. Only information that is genuinely useful at a glance belongs here.

### Always useful

- IP address;
- latency / frame-age estimate;
- active NIC;
- current frame rate.

### Useful when degraded

A concise warning line may replace or supplement the normal secondary data:

```text
● Spot 3
  10.110.0.14
  ⚠ High frame loss · 48 fps
```

or:

```text
● Spot 4
  10.110.0.15
  ⚠ Network unstable · 142 ms
```

### Better kept out of the rail

The following should normally remain in Camera Details / Diagnostics rather than permanently occupying the rail:

- codec profile;
- GOP/GOV value;
- decoder implementation;
- packet counts;
- reconnect count;
- RTSP URL;
- credentials;
- camera model/serial;
- detailed jitter statistics;
- CPU/GPU decode timing.

These are valuable for troubleshooting but not for constant operational scanning.

## Latency metric

The word `Latency` needs a precise product definition because absolute camera-to-display latency cannot always be measured directly from an RTSP stream.

For v1, the rail should preferably display a clearly defined measurable value such as:

- current frame age at the application;
- pipeline freshness delay;
- or another internally measured ingest-to-ready-frame metric.

If true end-to-end camera latency cannot be measured reliably, the UI must not present an estimated value as an absolute end-to-end latency measurement.

The user-facing label may still be `Latency` if the measurement method is well defined in diagnostics/help, but internally the metric should be explicit.

## Collapsed rail

The entire left rail should collapse to maximise canvas space.

Expanded:

```text
┌──────────────────────┐
│ CAMERAS           ‹  │
│                      │
│ ● Spot 1             │
│   10.110.0.12        │
│   34 ms · NIC A      │
│                      │
│ ● Spot 2             │
│   10.110.0.13        │
│   39 ms · NIC A      │
│                      │
│ ● Spot 3             │
│   10.110.0.14        │
│   128 ms · NIC B     │
└──────────────────────┘
```

Collapsed:

```text
┌────┐
│  › │
│ ●  │
│ ●  │
│ ●  │
│ ●  │
└────┘
```

In collapsed form, camera health dots should remain visible where practical so the rail still acts as a compact system-health indicator.

Hovering a dot may show a tooltip with the camera name and status.

## Drag behaviour in View editor

When Show Mode is OFF, configured camera rows are draggable sources.

The user may drag the camera name/row:

- onto an empty Camera Slot;
- onto an existing Camera element to replace its source;
- onto blank canvas space to create a new free-form Camera element.

Dragging should originate from the logical camera source, not from a live thumbnail.

When Show Mode is ON, drag actions from the rail are disabled while health/status information remains live.

## Click behaviour

Clicking a camera row should select/open lightweight source information without navigating away from the active workspace.

Suggested behaviour:

- single click: select source and optionally highlight every occurrence of that logical camera in the current View;
- double click or explicit details action: open Camera Details panel/modal;
- context menu: Camera Details, Reconnect, Locate in Current View, Open Diagnostics.

Network/configuration changes should remain in Camera Settings rather than being exposed directly in the rail.

## Camera Details

The expanded Camera Details panel/modal can contain:

- logical camera name;
- IP address;
- camera model where known;
- active NIC;
- RTSP transport;
- profile path (`profile2` for standard Robe workflow);
- negotiated resolution;
- expected frame rate;
- received frame rate;
- decoded frame rate;
- frame-age / latency metric;
- packet loss / dropped frames where measurable;
- current health reason;
- reconnect state;
- last successful frame time;
- Open Advanced Diagnostics.

## Health calculation

Status colour should be derived from multiple metrics rather than a single ping test.

Proposed conceptual logic:

```text
RED
  no usable frames / stream offline

AMBER
  usable frames present
  AND one or more sustained health thresholds exceeded

GREEN
  usable frames present
  AND health metrics inside expected thresholds
```

Thresholds should include hysteresis so a camera does not rapidly flicker between Green and Amber due to one dropped frame.

Example:

- a momentary single-frame loss does not change colour;
- sustained degradation over a short moving window may set Amber;
- recovery must remain healthy for a short period before returning Green.

Exact thresholds are to be determined through testing with real RoboSpot camera networks.

## Sorting

Default order should follow the configured logical camera order, normally Spot 1 through Spot 8.

Potential optional sorting later:

- logical order;
- name;
- health (problems first);
- NIC.

For v1, fixed logical order is preferable because technicians will expect camera positions not to move around when one becomes unhealthy.

## Empty sources

Configured logical slots without a physical camera assignment should remain visible if they are part of the show configuration.

Example:

```text
○ Spot 7
  Not configured
```

This state is distinct from Red/Offline: an unconfigured source is intentionally empty rather than failed.

A neutral grey indicator is appropriate for this case.

## Multiple NIC clarity

Because RoboCam-Hub supports multiple camera NICs simultaneously, the rail should display the friendly NIC alias/name rather than only a long Windows hardware description.

Users should be able to assign friendly aliases in Settings, for example:

```text
Windows Adapter: Intel(R) Ethernet Controller I225-V
Friendly Alias:  RoboSpot VLAN 110
```

The rail then shows:

```text
34 ms · RoboSpot VLAN 110 · 60 fps
```

This is more useful on tour than exposing raw OS device names everywhere.

## Visual density

The rail should support eight configured cameras comfortably on a typical laptop screen without requiring excessive scrolling.

Rows should therefore be compact and avoid card-style oversized padding.

Suggested approximate structure:

```text
● Spot 1
  10.110.0.12 · 34 ms
  RoboSpot VLAN 110 · 60 fps
```

or an even denser two-line form where space is limited:

```text
● Spot 1
  10.110.0.12 · 34 ms · NIC A · 60 fps
```

The exact density should be tested visually during implementation.

## Show Mode

Show Mode does not hide the Camera Rail.

During Show Mode:

- health indicators remain active;
- metrics continue updating;
- Camera Details remain accessible;
- diagnostics remain accessible;
- reconnect actions remain available;
- canvas/source assignment drag actions are disabled;
- camera naming and configuration changes that could alter show behaviour should require leaving Show Mode or an explicit confirmation.

## Initial acceptance tests

- display eight configured cameras without thumbnails;
- correctly show Green / Amber / Red health states;
- display neutral state for an intentionally unconfigured source;
- collapse/expand the rail without disrupting the canvas;
- retain visible health indicators in collapsed mode;
- update IP, latency/freshness, NIC and fps metrics live;
- trigger Amber using sustained frame/network degradation;
- trigger Red when usable frames stop;
- recover to Green without rapid colour flicker;
- drag a source onto a Camera Slot when Show Mode is off;
- prevent source dragging when Show Mode is on;
- open detailed diagnostics without leaving the View editor;
- display friendly NIC aliases.

## Decisions currently adopted

- Camera/source rail is a collapsible left sidebar.
- It does not contain live video thumbnails.
- Each configured source has a leading health dot.
- Green means online/healthy.
- Amber means online but degraded/network unstable or dropping significant frames.
- Red means camera offline/no usable stream.
- IP, latency/freshness, active NIC and frame rate are shown as compact secondary information.
- Detailed technical diagnostics remain outside the rail.
