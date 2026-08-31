# 16 — New Show and First-Run Workflow

## Purpose

Define the cleanest path from opening RoboCam-Hub with no configured show to having live camera sources, one or more Views and working NDI outputs.

The setup workflow should minimise technical friction while keeping experienced users in control. It must never assume that camera sources are named `Spot 1`, `Spot 2`, `Spot A`, `Spot B` or any other fixed convention.

## Core principles

- camera names are always user-defined logical names;
- `Spot 1`, `Spot 2`, etc. may be suggested defaults but are never required;
- camera naming happens during source setup and can be changed later in Camera Settings;
- Views reference the logical camera identity, not the physical IP address;
- discovery is explicit rather than automatically scanning production networks;
- camera-side configuration is never edited;
- the normal Robe workflow uses the supported Profile 2 RTSP path automatically;
- the user can skip the guided setup and configure an empty show manually;
- every newly opened or created Show enters Edit Mode, never Show Mode.

## Entry screen

Suggested initial screen:

```text
RoboCam-Hub

[ New Show ]
[ Open Show ]

Recent Shows
C7RIEL 2026
Festival Test
```

## Step 1 — Create Show

```text
NEW SHOW

Show Name:
[ C7RIEL & PACO AMOROSO 2026 ]

Save Location:
[ Documents\RoboCam-Hub\Shows ]

[ Create Show ]
```

Creating a show should immediately create a valid `.rchshow` container and begin autosave/recovery tracking.

## Step 2 — Network Setup

The initial guided workflow should create sensible logical network roles behind the scenes.

Example:

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

The user is not required to understand logical network roles during first setup.

Internally the show may create roles such as:

```text
Camera Network A
NDI Network A
```

which are mapped to machine-specific adapters.

## Step 3 — Add Cameras

The camera stage provides two paths:

```text
CAMERAS

[ Discover Cameras ]      [ + Add Manually ]
```

### Camera names are user-defined

Every camera added to the show must have a user-editable logical name.

Examples of valid names:

```text
Spot 1
Spot 2
Stage Left
Stage Right
FOH Spot
Balcony Spot
Robospot A
Robospot B
Spot Emma
Spot Dave
```

RoboCam-Hub must not derive long-term logical identity from the IP address or discovery order.

The UI may pre-fill a suggested name such as `Spot 1`, but the field remains editable before the camera is added.

### Manual Add

Suggested workflow:

```text
ADD CAMERA

Camera Name:
[ Stage Left Spot ]

IP Address:
[ 10.110.0.11 ]

Camera Network:
[ Camera Network A ▼ ]

Transport:
[ UDP ▼ ]

[ Add Camera ]
```

The standard Robe workflow automatically constructs the expected Profile 2 RTSP address.

The normal workflow should not expose camera encoder or Profile configuration.

### Discovery

Discovery should run only when the user explicitly chooses `Discover Cameras` and only on selected Camera Network interfaces.

Example:

```text
DISCOVERED CAMERAS

10.110.0.11    Wisenet XNZ-L6320A
Camera Name: [ Stage Left Spot ]
[ Add Camera ]

10.110.0.12    Wisenet XNZ-L6320A
Camera Name: [ Stage Right Spot ]
[ Add Camera ]
```

The discovery result identifies the physical device; the user still chooses its logical name.

The application may suggest sequential names for convenience, but discovery order must never become the permanent identity implicitly.

## Camera list during setup

After cameras are added:

```text
● Stage Left Spot     10.110.0.11    Healthy
● Stage Right Spot    10.110.0.12    Healthy
● FOH Spot            10.110.0.13    Healthy
● Balcony Spot        10.110.0.14    Healthy
```

Logical names shown here are the same names used by:

- Camera Source Rail;
- View editor source list;
- camera labels where dynamic camera-name text is used;
- Camera Settings;
- diagnostics.

## Renaming later

A logical camera may be renamed from Camera Settings.

Renaming changes the displayed source name everywhere while preserving the internal source ID.

For example:

```text
Old name: Spot 3
New name: FOH Spot
```

must not break existing Views.

Views should therefore reference a stable camera/source ID rather than storing the display name as the relationship key.

## Step 4 — Create First View

Once cameras exist, the setup wizard offers a fast layout creation step.

```text
CREATE YOUR FIRST VIEW

Template:
[ 2 × 2 ]

Available Cameras
☑ Stage Left Spot
☑ Stage Right Spot
☑ FOH Spot
☑ Balcony Spot

[ Create View ]
```

Selection order determines initial slot assignment unless the user manually reorders the selected sources.

Example:

```text
Slot A → Stage Left Spot
Slot B → Stage Right Spot
Slot C → FOH Spot
Slot D → Balcony Spot
```

The resulting layout remains fully editable in the free-form View editor.

## Eight-camera convenience workflow

For eight cameras the wizard may offer:

```text
8 CAMERAS

○ One 4×2 View

● Two 2×2 Views
```

If two Views are selected, the user should be able to confirm or edit the camera grouping rather than RoboCam-Hub assuming `1–4` and `5–8`.

Example:

```text
VIEW A
Stage Left Spot
Stage Right Spot
FOH Spot
Balcony Spot

VIEW B
Spot Upper SL
Spot Upper SR
Pit Spot
Spare Spot
```

Suggested View names can be provided but must be editable:

```text
View A Name: [ Spots A ]
View B Name: [ Spots B ]
```

The terms `Spots A` and `Spots B` are convenience defaults only.

## Step 5 — Create NDI Output(s)

Suggested workflow:

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

When the split-view workflow created multiple Views, matching NDI outputs may be offered automatically.

The output names remain editable.

## Completion

```text
SETUP COMPLETE

8 Cameras Configured
2 Views Created
2 NDI Outputs Configured

[ Open Workspace ]
```

The workspace always opens in Edit Mode.

## Skip / advanced workflow

Every wizard stage should allow an experienced user to skip guided setup.

Possible action:

```text
[ Skip Setup — Open Empty Workspace ]
```

The user can then configure Cameras, Views and Outputs manually through the normal application UI.

## What should not appear in first-run setup

Avoid asking for:

- theme selection;
- decoder backend;
- GPU compositor settings;
- RTSP jitter/reconnect threshold tuning;
- camera encoder settings;
- arbitrary camera Profile selection;
- diagnostics thresholds;
- advanced NDI performance settings.

These belong in Settings and should have sensible defaults.

## Initial acceptance tests

- create a new show from an empty application;
- map separate Camera and NDI network adapters;
- manually add a camera using an arbitrary user-defined name;
- discover a camera and assign a custom logical name before adding it;
- rename a logical camera later without breaking Views;
- create a View from named cameras with no dependency on `Spot N` naming;
- create two 2×2 Views from eight cameras with manually chosen groupings;
- edit suggested View and NDI names;
- skip the wizard and open an empty workspace;
- always enter Edit Mode after setup or show load.

## Decisions currently adopted

- Camera names are user-defined logical names.
- Sequential `Spot 1`, `Spot 2`, etc. are suggestions only.
- View grouping is user-selectable and not derived from camera naming.
- Logical camera identity uses a stable internal ID so renaming does not break Views.
- Camera discovery remains explicit.
- Standard Robe Profile 2 handling remains automatic in the normal workflow.
- Every Show opens in Edit Mode.
