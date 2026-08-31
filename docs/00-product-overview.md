# 00 — Product Overview

## Purpose

RoboCam-Hub is a desktop-first, local-first application for ingesting low-latency followspot camera feeds, arranging them into operator-friendly multiviews, and publishing those views over NDI to grandMA3 or other NDI-capable destinations.

The initial reference use case is Robe RoboSpot-compatible Samsung / Wisenet cameras connected over dedicated camera VLANs.

## Problem

RoboSpot BaseStations display camera video with very low latency, but generic RTSP viewing applications such as OBS often add substantial receive, decode and render buffering. Low-latency GStreamer pipelines have already shown that much of this delay can be removed.

A purpose-built application can turn that proven workflow into a repeatable touring tool without requiring command-line pipelines, OBS scenes or manual reconstruction at each venue.

## Primary user outcome

A user should be able to arrive at a venue, connect the RoboCam-Hub machine to the required networks, open a saved show file, verify or discover the cameras, and have a working low-latency multiview published to NDI within minutes.

## Initial workflow

```text
Launch application
      ↓
Select camera NIC / VLAN-facing adapter
      ↓
Discover or manually add cameras
      ↓
Validate stream and low-latency settings
      ↓
Assign camera labels / spot numbers
      ↓
Create or recall multiview layout
      ↓
Select NDI output NIC
      ↓
Publish NDI output
      ↓
Select NDI source in grandMA3
```

## Core product principles

1. **Latency first** — minimise end-to-end delay above all non-safety video quality concerns.
2. **Never accumulate stale video** — drop late frames rather than allowing queues to build.
3. **Independent camera pipelines** — a degraded camera must not increase latency on healthy cameras.
4. **Touring reliability** — unplug/replug, IP changes and camera restarts must be recoverable without restarting the whole application.
5. **Local-first operation** — no internet connection is required for normal use.
6. **Clear networking** — camera ingest and NDI output must be bindable to separate NICs.
7. **Fast deployment** — saved show configurations should reduce daily setup to validation rather than reconstruction.
8. **Observable system state** — operators need clear health, frame-rate, reconnect and network diagnostics.

## Initial scope

### In scope

- Windows desktop application first.
- RTSP camera ingest.
- Wisenet / Samsung cameras used with RoboSpot as the first validated camera family.
- GStreamer-based low-latency media engine.
- Multiple simultaneous camera inputs.
- Automatic and manual multiview layouts.
- Local preview.
- NDI High Bandwidth output.
- Separate camera and NDI NIC selection.
- Saved local show configuration.
- Camera reconnect and health monitoring.
- Performance / latency diagnostics.
- grandMA3 as a primary NDI destination for validation.

### Later / optional scope

- NDI HX output.
- Additional camera manufacturers.
- ONVIF discovery and profile management.
- Automated camera configuration validation / correction.
- Multiple simultaneous NDI program outputs.
- Remote control / API.
- Companion mobile or web UI.
- Recording.
- Streaming protocols beyond NDI.

## Out of scope for first prototype

- Replacing RoboSpot BaseStation control functions.
- PTZ control.
- Lighting fixture control.
- Cloud dependency.
- Broadcast-grade frame synchronisation across sources if it increases operator latency.

## Reference camera configuration observed

The initial Wisenet low-latency stream observed during testing uses approximately:

- H.264
- 1280 × 720
- 60 fps
- GOV / GOP length: 1
- Smart Codec: disabled
- Dynamic GOV: disabled

The application should not assume all cameras will match these settings, but this is the first known-good reference configuration.

## Success criteria for first usable release

A first usable release should allow a touring operator to:

- configure at least six simultaneous camera feeds;
- maintain 60 fps where hardware and sources allow;
- create an adaptive multiview automatically;
- publish that multiview over NDI;
- select different NICs for camera and NDI networks;
- recover automatically from a temporary camera disconnect;
- save and recall the entire setup;
- operate without internet access;
- achieve latency close enough to a RoboSpot BaseStation to be operationally useful.

Exact latency targets will be defined and measured in `13-performance-targets.md`.
