# RoboCam-Hub

RoboCam-Hub is a planned low-latency camera ingest, multiview and NDI gateway application for live entertainment followspot workflows.

The initial target workflow is:

```text
RoboSpot / Wisenet / Samsung RTSP cameras
            ↓
Low-latency GStreamer ingest
            ↓
Independent decode / frame handling
            ↓
Multiview compositor
            ↓
NDI output
            ↓
grandMA3 or other NDI-capable monitoring destination
```

The project is being designed from the workflow and operational requirements first, with implementation to follow from the documentation pack in `/docs`.

## Core design principles

- Minimal end-to-end latency is the primary requirement.
- Late frames should be dropped rather than allowed to build latency.
- Failure or slowdown of one camera must not stall other feeds.
- Camera ingest and NDI output may use different network interfaces.
- The application must remain fully usable offline.
- Touring use must be fast to deploy, easy to recover and simple to diagnose.
- Show configurations should be locally saved and quickly recalled.
- Initial reference camera workflow is 1280×720 at 60 fps H.264 using Wisenet / Samsung RoboSpot cameras.
- Initial output focus is NDI High Bandwidth, with other NDI modes considered later.

## Documentation

The planning pack lives in [`/docs`](docs/00-product-overview.md).

Start with:

- [`00-product-overview.md`](docs/00-product-overview.md)
- [`01-user-workflows.md`](docs/01-user-workflows.md)
- [`02-system-architecture.md`](docs/02-system-architecture.md)
- [`13-performance-targets.md`](docs/13-performance-targets.md)
- [`16-development-roadmap.md`](docs/16-development-roadmap.md)

## Project status

**Planning / technical validation.**

A low-latency GStreamer RTSP workflow has already been proven against RoboSpot camera feeds and can get substantially closer to RoboSpot BaseStation latency than generic OBS RTSP ingest. The next validation step is to prove the complete RTSP → compositor → NDI → grandMA3 chain and measure the latency budget at each stage.
