# 01 — User Workflows

## 1. First-time setup

1. Install RoboCam-Hub and required media dependencies.
2. Launch the application.
3. Select the network adapter connected to the RoboSpot / camera VLANs.
4. Select the network adapter that should carry NDI output.
5. Discover cameras or add them manually by IP / RTSP URL.
6. Test each feed.
7. Assign operator-facing names such as `Spot 1`, `Spot 2`, `SL Spot`, `SR Spot`.
8. Create a multiview or accept the automatically generated layout.
9. Name the NDI output.
10. Publish and verify the source in grandMA3.
11. Save the setup as a show file.

## 2. Daily touring startup

The intended daily workflow should be extremely short:

1. Connect the computer to the camera and NDI networks.
2. Open RoboCam-Hub.
3. Load the show configuration automatically or choose it from Recent Shows.
4. The app validates network adapters and attempts to reconnect all saved cameras.
5. A readiness view indicates which cameras are live, degraded or missing.
6. NDI output starts when the required readiness condition is met, or immediately if configured to do so.

The user should not need to rebuild layouts, re-enter RTSP URLs or recreate NDI sources each day.

## 3. Camera discovery

### Automatic

Where supported, the user chooses **Discover Cameras** and the application scans only the selected camera-facing adapter / network.

Discovered devices should show:

- manufacturer;
- model;
- IP address;
- hostname where available;
- supported discovery protocol;
- whether a known low-latency profile has been detected;
- connection state.

### Manual

The user can always add a camera manually using:

- display name;
- IP address or hostname;
- RTSP path / URL;
- credentials if required;
- transport preference;
- optional model information.

Manual configuration must remain available even when discovery fails.

## 4. Camera assignment

A discovered camera is not automatically assumed to be a particular followspot.

The user assigns an operational identity such as:

- Spot 1
- Spot 2
- Spot 3
- SL Followspot
- SR Followspot
- FOH Spot

This identity is what appears in multiview labels and diagnostics.

## 5. Low-latency validation

When a feed is opened, RoboCam-Hub should validate as much of the stream as possible without disrupting the camera.

The app should report items such as:

- codec;
- resolution;
- frame rate;
- transport;
- observed receive frame rate;
- decoder state;
- dropped frames;
- reconnect count;
- whether the configured stream resembles a known low-latency reference profile.

Warnings should be advisory unless a setting makes the feed unusable.

## 6. Multiview creation

### Automatic layout

The user can choose **Auto Layout**.

Example defaults:

- 1 camera → 1×1
- 2 cameras → 2×1
- 3–4 cameras → 2×2
- 5–6 cameras → 3×2
- 7–9 cameras → 3×3

The exact layout policy is configurable later, but the first release should make the common 1–6 camera touring case effortless.

### Manual layout

The user can override the automatic layout and:

- move tiles;
- resize tiles;
- hide sources;
- rename labels;
- choose label visibility;
- select an output resolution;
- choose background behaviour for missing sources.

## 7. Camera disconnect during show

If a camera disappears:

1. Its tile immediately stops advancing.
2. Other camera pipelines continue unaffected.
3. The tile indicates `RECONNECTING` or equivalent.
4. The application repeatedly attempts to reconnect using an appropriate backoff strategy.
5. When the feed returns, it resumes without restarting the multiview or NDI sender.

A dead or slow feed must never cause healthy feeds to queue behind it.

## 8. NDI publishing

The user creates an output with:

- output name;
- resolution;
- frame rate;
- selected multiview;
- network adapter;
- NDI mode where applicable.

Initial target:

- NDI High Bandwidth;
- 1920×1080 or other practical operator-selected output resolution;
- 60 fps where system performance allows.

## 9. grandMA3 workflow

1. RoboCam-Hub publishes the configured NDI source.
2. The source appears on the MA network.
3. The user selects it in grandMA3's video workflow.
4. The multiview can then be displayed in an MA layout / video view as required.

The MA receiving path must be latency-tested independently because RoboCam-Hub cannot control buffering or rendering inside grandMA3.

## 10. Show file workflow

A show configuration should contain at minimum:

- camera definitions;
- camera operational labels;
- RTSP configuration;
- selected camera NIC;
- selected NDI NIC;
- multiview definitions;
- NDI output definitions;
- reconnect preferences;
- UI preferences relevant to the show.

Sensitive credentials should not be stored in plain text.

## 11. Emergency / degraded workflow

The user must be able to quickly:

- disable a problematic source;
- force reconnect one source;
- restart one media pipeline;
- temporarily switch RTSP transport;
- fall back to a different camera profile;
- stop / restart one NDI output;

without restarting the entire application.

## 12. Shutdown

On exit, the application should:

- stop NDI output cleanly;
- stop camera sessions;
- persist the current show state if configured;
- avoid leaving background media processes running.
