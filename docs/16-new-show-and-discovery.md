# 16 — New Show and Camera Discovery

## Purpose

Define the first-time/new-show workflow for RoboCam-Hub, including network selection, camera discovery, manual camera naming, preview-assisted identification, View creation and NDI output creation.

The objective is to get a user from an empty application to live camera Views and NDI outputs quickly without exposing unnecessary camera-side settings.

## Core workflow

Recommended new-show sequence:

```text
New Show
   ↓
Choose Camera + NDI networks
   ↓
Discover / Add Cameras
   ↓
Name Cameras
   ↓
Create View(s) from template
   ↓
Create NDI Output(s)
   ↓
Open Workspace in Edit Mode
```

Every stage should allow experienced users to skip the guided step and continue to the normal workspace.

## Step 1 — Create Show

Suggested fields:

```text
NEW SHOW

Show Name:
[ C7RIEL & PACO AMOROSO 2026 ]

Save Location:
[ Documents\RoboCam-Hub\Shows ]

[ Create Show ]
```

Creating the show creates a portable `.rchshow` show file and opens the guided setup flow.

## Step 2 — Network Setup

The user selects the local adapters used for camera ingest and NDI output.

Suggested UI:

```text
NETWORK SETUP

Camera Network
[ USB Ethernet Adapter 2 ▼ ]

NDI Output Network
[ Intel Ethernet I225 ▼ ]

Friendly Names
Camera Network → [ RoboSpot VLAN ]
NDI Network    → [ MA3 / NDI Network ]

[ Continue ]
```

The show should reference logical network roles such as `Camera Network A` and `NDI Network A`, while the selected physical Windows adapters remain machine-specific mappings.

## Step 3 — Camera Discovery / Manual Add

Camera discovery should be user-initiated rather than automatically scanning the network as soon as an adapter is selected.

The Camera step should provide:

```text
CAMERAS

[ Discover Cameras ]     [ + Add Manually ]
```

### Camera names are user-defined

RoboCam-Hub must not force discovered cameras into fixed names such as `Spot 1`, `Spot 2`, `Camera A`, or `Camera B`.

When adding or assigning a discovered camera, the user chooses the logical camera name.

Examples:

```text
SR Spot
SL Spot
FOH Spot
Balcony Spot
Spot 1
Spot 2
Camera A
Camera B
```

The logical camera name is the identity referenced by Views.

### Camera Location metadata as suggested name

Supported cameras may expose a read-only `Location` metadata field. Robe-installed cameras may use this field as the physical camera/spot label, for example `SPOT_1`.

When discovery can read a non-empty Location value, RoboCam-Hub should use it to pre-fill the logical Camera Name field.

Example:

```text
Camera reports Location: SPOT_1

Camera Name:
[ SPOT_1 ]
```

The pre-filled name is only a suggestion. The user must be able to edit it freely before adding the camera:

```text
Camera reports Location: SPOT_1

Camera Name:
[ SR Followspot ]
```

RoboCam-Hub must preserve the distinction between:

- camera-reported Location metadata; and
- the user-defined RoboCam-Hub logical camera name.

Changing the logical name in RoboCam-Hub must never write back to the camera Location field.

If Location metadata is blank or unavailable, the Camera Name field remains user-editable and may use a simple temporary suggestion such as the device IP or `Camera`.

If multiple discovered cameras report the same Location value, RoboCam-Hub should flag the duplicate for the user rather than assuming they are the same logical source.

## Discovery results

Discovered cameras should appear in a temporary list and should not automatically become configured sources.

Where Location metadata is available, it should be visually useful in the discovery list:

```text
DISCOVERED CAMERAS

SPOT_1   10.110.0.11   Wisenet XNZ-L6320A      [ Preview ] [ Add ]
SPOT_2   10.110.0.12   Wisenet XNZ-L6320A      [ Preview ] [ Add ]
          10.110.0.13   Samsung SNZ-6320         [ Preview ] [ Add ]
```

Discovery results may include, where available and reliable:

- camera Location metadata;
- device name;
- IP address;
- manufacturer;
- model;
- serial number where exposed;
- MAC address;
- discovery/network interface;
- availability of the expected Profile 2 stream.

Camera discovery data is informational only. RoboCam-Hub remains read-only with respect to physical camera configuration.

## Preview-assisted camera identification

A preview remains useful during discovery even when Location metadata exists, because a label may be blank, stale, duplicated or incorrect after a fixture/camera swap.

The discovery UI should allow the user to preview a selected discovered camera before naming/adding it.

Recommended interaction:

```text
┌──────────────────────────────┬───────────────────────────────┐
│ DISCOVERED CAMERAS           │ CAMERA PREVIEW                │
│                              │                               │
│ > SPOT_1                    │      [ live picture ]         │
│   10.110.0.11               │                               │
│   XNZ-L6320A                 │ Location: SPOT_1             │
│                              │ IP: 10.110.0.11               │
│   SPOT_2                    │ Model: XNZ-L6320A             │
│   10.110.0.12               │                               │
│                              │ Camera Name:                  │
│   10.110.0.13               │ [ SPOT_1                   ]  │
│   SNZ-6320                    │                               │
│                              │ [ Add Camera ]                │
└──────────────────────────────┴───────────────────────────────┘
```

The user may immediately edit `SPOT_1` to any preferred logical name before adding it.

The preview exists only for setup/identification. It is intentionally different from the normal Camera Source Rail, which should not contain live thumbnails.

### Preview behaviour

To avoid unnecessary load and bandwidth during discovery:

- do not automatically open live previews for every discovered camera simultaneously;
- preview only the selected camera by default;
- use the normal low-latency Profile 2 ingest path where possible;
- use the same low-latency buffering principles as normal ingest;
- stop or reuse the temporary preview connection when another camera is selected;
- once the camera is added, hand over cleanly to the normal configured ingest pipeline where practical.

A future implementation may optionally prefetch still thumbnails, but v1 should prioritise a single selected live preview because it is most useful for identifying the physical spot position.

## Add discovered camera

When the user clicks `Add`, present a compact assignment form with the Camera Name pre-filled from Location metadata where available:

```text
ADD CAMERA

Camera Location:
SPOT_1

Camera Name:
[ SPOT_1 ]

IP Address:
10.110.0.11

Camera Network:
[ Camera Network A ▼ ]

Transport:
[ UDP ▼ ]

[ Add Camera ]
```

The Camera Name remains fully editable.

The IP may be read-only in the discovery flow because it came from the discovered device; manual add remains available for entering a different IP.

RoboCam-Hub should construct the normal Robe-compatible stream path using `profile2` rather than asking the user to choose camera encoder profiles during normal setup.

## Manual Add

Manual add should use the same naming model as discovery.

Suggested fields:

```text
ADD CAMERA MANUALLY

Camera Name:
[ SR Followspot ]

IP Address / Hostname:
[ 10.110.0.11 ]

Camera Network:
[ Camera Network A ▼ ]

Transport:
[ UDP ▼ ]

[ Add Camera ]
```

Optional credentials may be provided if required, but should be stored securely on the machine rather than plaintext in the portable show file.

Advanced arbitrary RTSP URL support may be available behind an Advanced option, but should not clutter the standard Robe workflow.

## Camera list after assignment

Once configured, the setup screen shows the user-defined logical names rather than generated Spot numbering.

Example:

```text
CAMERAS

● SR Followspot      10.110.0.11
● SL Followspot      10.110.0.12
● FOH Followspot     10.110.0.13
● Balcony Followspot 10.110.0.14

[ + Add Camera ]     [ Discover More ]
```

Names remain editable later through Camera ingest settings.

The camera-reported Location may remain available in Properties/Diagnostics as read-only metadata.

## Step 4 — Create View(s)

The View setup step should present layout templates while using the configured logical camera names.

Example:

```text
CREATE YOUR FIRST VIEW

Template:
[ 2×2 ]

Camera Slot A   [ SR Followspot ▼ ]
Camera Slot B   [ SL Followspot ▼ ]
Camera Slot C   [ FOH Followspot ▼ ]
Camera Slot D   [ Balcony Followspot ▼ ]

View Name:
[ Main Spots ]

[ Create View ]
```

Users can leave slots unassigned and fill them later in the free-form View editor.

## Eight-camera split workflow

For eight configured cameras, offer a convenience setup for two 2×2 Views.

The user must still be able to choose the camera assignment rather than assuming cameras 1–4 and 5–8.

Suggested UI:

```text
SPLIT 8 CAMERAS

View A — [ Spots A ]
Slot A   [ SR Spot 1 ▼ ]
Slot B   [ SL Spot 1 ▼ ]
Slot C   [ SR Spot 2 ▼ ]
Slot D   [ SL Spot 2 ▼ ]

View B — [ Spots B ]
Slot A   [ FOH Spot 1 ▼ ]
Slot B   [ FOH Spot 2 ▼ ]
Slot C   [ Balcony 1 ▼ ]
Slot D   [ Balcony 2 ▼ ]
```

The wizard may provide an automatic initial assignment based on configured-camera order, but the user must be able to change every slot before creation.

## Step 5 — Create NDI Outputs

For each created View, the wizard may offer a matching NDI output.

Example:

```text
CREATE NDI OUTPUT

View:
[ Spots A ▼ ]

NDI Name:
[ ROBOCAM - SPOTS A ]

Resolution:
[ 1920×1080 ▼ ]

Frame Rate:
[ 60 ▼ ]

NDI Network:
[ NDI Network A ▼ ]

☑ Start output now

[ Create Output ]
```

For the split-eight workflow:

```text
Create matching NDI outputs?

☑ ROBOCAM - SPOTS A
☑ ROBOCAM - SPOTS B
```

## Completion

Setup completes into the normal View workspace in Edit Mode.

Example:

```text
SETUP COMPLETE

8 Cameras Configured
2 Views Created
2 NDI Outputs Running

[ Open Workspace ]
```

Show Mode is always OFF after loading or creating a show.

## UX principles

- user-defined camera naming is the default model;
- camera Location metadata should pre-fill the logical name when available, but never lock it;
- discovered device identity, camera-reported Location and logical camera name are separate concepts;
- preview remains available to verify which physical camera/spot a discovered device belongs to;
- discovery never writes to camera configuration;
- no normal setup step exposes camera-side encoder/profile controls;
- camera preview should not become a permanent source-rail thumbnail;
- users can skip wizard stages and configure manually;
- View templates accelerate setup but never constrain later free-form editing;
- NDI outputs remain separate objects from Views.

## Initial acceptance tests

- create a new show;
- select camera and NDI network adapters;
- discover supported cameras on the selected camera network;
- read camera Location metadata where exposed;
- pre-fill Camera Name from Location metadata;
- edit the pre-filled Camera Name before adding the camera;
- verify changing the logical name does not modify camera Location metadata;
- flag duplicate Location values without merging devices;
- select a discovered camera and display a low-latency preview;
- identify a physical camera from that preview;
- add a discovered camera without modifying the physical camera;
- manually add a camera with a user-defined name;
- create a 2×2 View using named cameras;
- create two 2×2 Views from eight cameras with custom slot assignments;
- create matching NDI outputs;
- finish in the normal workspace with Edit Mode active.

## Decisions currently adopted

- Camera names are always user-defined logical names.
- Camera Location metadata is used as the default suggested name when available.
- The user can always edit the suggested name before adding the source.
- Fixed names such as Spot 1/Spot 2 are examples only, not enforced naming.
- Discovery is explicitly initiated by the user.
- Discovery provides a selected-camera live preview for physical identification/verification.
- Discovery preview does not change the normal no-thumbnail Camera Source Rail design.
- Only one discovery preview needs to run at a time in v1.
- RoboCam-Hub remains read-only with respect to physical camera settings.
- Normal Robe ingest uses Profile 2 without exposing camera-side profile configuration.
