# 13 — Performance Targets

## Why this document exists

RoboCam-Hub is only useful if its latency is low enough for real followspot operation. Performance therefore needs explicit budgets and repeatable measurement rather than subjective statements such as “feels close”.

## Reference baseline

The practical baseline is the RoboSpot BaseStation viewing the same physical camera feed.

The BaseStation should be treated as the operational benchmark, not necessarily as a technically reproducible latency target.

## End-to-end latency chain

```text
Scene / camera exposure
        ↓
Camera image processing
        ↓
H.264 encoding
        ↓
RTSP / RTP transport
        ↓
RoboCam-Hub receive / depay
        ↓
H.264 decode
        ↓
Frame queue / router
        ↓
Multiview composition
        ↓
NDI sender
        ↓
NDI network transport
        ↓
grandMA3 receive / decode / render
        ↓
Display scanout
```

## Primary target

The first prototype should aim for an application preview that is visually very close to the RoboSpot BaseStation when fed from the same camera.

The exact numeric target will be set after instrumented measurement.

Initial working targets:

- no intentional multi-frame buffering in operator mode;
- per-camera internal queue depth normally ≤ 1 decoded frame;
- compositor should not wait for all cameras to align;
- no progressive latency growth over time;
- reconnecting or degraded cameras must not increase healthy-camera latency;
- stable 60 fps output where input, hardware and destination permit.

## Latency budget placeholders

| Stage | Initial target | Measurement status |
|---|---:|---|
| RTSP receive / jitter handling | < 1 frame intentional buffering | To measure |
| Decode | As close to 1 frame as practical | To measure |
| Frame queue | ≤ 1 frame | Design requirement |
| Composition | ≤ 1 frame | To measure |
| NDI send path | TBD | To measure |
| grandMA3 receive / render | TBD | To measure |
| Total RoboCam-Hub local preview | TBD | To measure |
| Total to grandMA3 display | TBD | To measure |

At 60 fps, one frame is approximately **16.67 ms**.

## Frame handling policy

Operator mode should prefer frame freshness over continuity.

If the system falls behind:

- stale frames should be dropped;
- queues should remain bounded;
- no source should accumulate seconds or hundreds of milliseconds of historic video;
- the newest available completed frame should be preferred.

## Throughput target

Initial required reference load:

- 6 × 1280×720 H.264 streams;
- 60 fps per stream;
- one 1920×1080 60 fps multiview;
- one NDI High Bandwidth output;
- local preview active.

The application should later be benchmarked above this level to establish safe headroom.

## CPU / GPU utilisation

Performance goals should be expressed as headroom rather than absolute utilisation.

Under the six-camera reference load, the target system should retain enough spare capacity that:

- reconnecting a camera does not cause widespread frame loss;
- opening settings panels does not affect video timing;
- transient Windows background activity does not cause persistent latency growth.

A suitable reference hardware specification will be established during prototype testing.

## Network behaviour

Camera RTP traffic and NDI traffic should be measured separately.

Tests should include:

1. separate physical camera and NDI NICs;
2. shared physical NIC where required;
3. packet loss on one camera stream;
4. temporary switch / cable disconnect;
5. camera restart;
6. network adapter link-down / link-up.

## Measurement methods

### Visual stopwatch method

Point the camera at a high-refresh stopwatch or timer and photograph / film multiple displays in the same frame:

- real timer;
- RoboSpot BaseStation;
- RoboCam-Hub preview;
- grandMA3 NDI view.

This provides an easy operational comparison.

### Flash / LED method

Use a fast flashing LED or other sharp visual transition. Record the real-world source and displays with a high-frame-rate phone or camera, then count frame differences.

### Software instrumentation

Where possible, record timestamps at:

- RTP packet arrival;
- decoded-frame availability;
- compositor submission;
- NDI send submission.

This helps separate application latency from camera and grandMA3 latency.

## Long-duration stability test

A low-latency pipeline that slowly drifts behind is unacceptable.

The system should be tested continuously for at least several hours with:

- all reference feeds active;
- NDI output active;
- occasional source disconnects / reconnects;
- UI interaction.

Expected result:

**Latency after several hours should remain approximately the same as latency immediately after startup.**

## Acceptance test for first prototype

The first prototype is considered technically promising if:

1. six reference feeds can be ingested concurrently;
2. all six remain responsive without progressive lag;
3. the multiview can sustain near-60 fps;
4. the application preview remains close to the RoboSpot BaseStation;
5. the NDI output is stable;
6. grandMA3 can display the NDI feed;
7. total grandMA3 latency is low enough to be operationally useful.

If item 7 fails primarily because of grandMA3 receive latency, the application may still be viable, but the intended monitoring architecture will need reconsideration.
