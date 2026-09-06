# 15 — Show Files and Persistence

## Purpose

Define how RoboCam-Hub stores portable show configuration, machine-specific settings, templates, assets, recovery data and network mappings.

The goal is to make a complete show easy to move between computers without tying it to one Windows installation or one set of physical network adapters.

## Core model

RoboCam-Hub data is divided into three categories:

1. **Show data** — portable configuration required to recreate a show.
2. **Machine data** — settings specific to the current computer.
3. **Templates** — reusable layouts independent of a particular show.

A Show must not store hard dependencies on Windows NIC identifiers that will become invalid on another computer.

## Show file

A Show should be represented to the user as one portable file, for example:

```text
C7RIEL-2026.rchshow
```

The user should be able to copy, back up, email or move this file without separately collecting image assets or configuration files.

Internally, the file may be a packaged archive containing versioned structured data and managed assets, for example:

```text
manifest.json
cameras.json
views.json
outputs.json
network-roles.json

assets/
  tour-logo.png
  background.png
```

The internal implementation is not exposed as part of the normal user workflow.

## Data stored in a Show

A Show should contain:

### Show identity

- show name;
- internal unique ID;
- file/schema version;
- optional created/modified metadata.

### Logical camera sources

For each configured logical camera:

- logical name, e.g. `Spot 1`;
- camera IP address or hostname;
- enabled/disabled state;
- ingest transport, UDP by default or explicit TCP;
- logical camera network role;
- supported RTSP profile/path convention;
- any show-specific ingest overrides.

Physical camera-side encoder or device configuration must not be written by RoboCam-Hub.

### Views

Each View stores:

- View identity and name;
- canvas resolution;
- frame rate;
- background;
- camera elements and logical source references;
- free-form transforms;
- crop/fit state;
- text elements;
- image elements;
- shapes/frames;
- layer order;
- grouping where supported;
- guides/template metadata where required.

### NDI Outputs

Each Output stores:

- internal ID;
- user-facing name;
- NDI source name;
- referenced View;
- output resolution;
- output frame rate;
- NDI mode;
- enabled/start-with-show preference where applicable;
- logical NDI network role(s).

### Managed assets

Images used by Views should travel with the Show.

Examples:

- logos;
- backgrounds;
- show artwork;
- decorative assets.

The Show should reference managed internal assets rather than relying only on absolute paths such as Desktop or Downloads.

## Logical network roles

Portable Shows should reference logical network roles rather than Windows NIC IDs.

Example:

```text
Camera Network A
NDI Network A
```

A camera configuration may therefore store:

```text
Spot 1
IP: 10.110.0.12
Network Role: Camera Network A
```

and an NDI output may store:

```text
ROBOCAM - SPOTS A
Network Role: NDI Network A
```

The current computer then maps those roles to its physical network adapters.

## Per-machine network mapping

Machine-local data may contain:

```text
Camera Network A → USB Ethernet Adapter #2
NDI Network A    → Intel I225
```

The mapping should use a stable Windows/OS adapter identifier wherever possible.

Friendly aliases may also be stored locally, for example:

```text
Intel(R) Ethernet Controller I225-V
Friendly Alias: Lighting / NDI
```

If a remembered USB adapter is disconnected at startup, the mapping remains stored and the adapter is shown as Missing rather than forgotten.

If it later reconnects with the same stable identifier, RoboCam-Hub should automatically restore the mapping.

## Opening a Show on another computer

When a Show is opened on a machine without an existing mapping for one or more network roles, RoboCam-Hub should present one concise mapping step.

Example:

```text
NETWORK SETUP

Camera Network A
→ [ USB Ethernet 3             ▼ ]
   10.110.0.100

NDI Network A
→ [ Intel Ethernet             ▼ ]
   2.10.10.50

[ Apply ]
```

The selection should be remembered on that computer without modifying the portable role definition into a machine-specific NIC ID.

The application should not require the user to edit every camera individually merely because the Show is being run from another laptop.

## Show startup mode

Every Show must open in **Edit Mode**.

Show Mode is an operational lock and is never persisted as the startup state.

There is no prompt asking whether to enter Show Mode when loading a Show.

The startup sequence is therefore conceptually:

```text
Open Show
  ↓
Load portable configuration
  ↓
Resolve machine-local network mappings
  ↓
Start/prepare configured media services as appropriate
  ↓
Open View workspace in Edit Mode
```

The operator explicitly enables Show Mode only when ready.

This also ensures that missing NIC mappings, unavailable sources or other setup issues can be corrected immediately after opening.

## Machine-specific application data

The following should normally stay outside the portable Show:

- application theme: Auto / Light / Dark;
- actual Windows NIC IDs;
- NIC friendly aliases;
- GPU/decoder preference;
- compositor/backend preference;
- window position and size;
- editor zoom and local workspace state;
- recently opened Shows;
- default storage folders;
- performance/debug preferences;
- machine-local network role mappings.

These settings belong to the local RoboCam-Hub installation/profile.

## Credentials

Credentials should not be stored in plaintext inside a portable `.rchshow` file.

If credentials are required for a compatible camera workflow, RoboCam-Hub should prefer secure machine-local credential storage and reference them indirectly.

A Show opened on another computer may therefore require the user to supply credentials once on that machine.

## Saving

RoboCam-Hub should support both manual Save and automatic persistence.

Expected behaviour:

```text
Ctrl+S → Save immediately
```

Normal user changes should also be autosaved using safe/atomic file replacement so a crash or power failure cannot easily corrupt the only valid copy.

## Recovery and backups

The application should keep recovery information and a limited rolling backup history.

Conceptually:

```text
Current Show
Latest Recovery State
Previous Backup 1
Previous Backup 2
Previous Backup 3
```

Exact retention count is an implementation decision.

If RoboCam-Hub detects an unclean shutdown and a recovery state is newer than the last clean save, it should offer recovery clearly.

Example:

```text
RoboCam-Hub did not close normally.

A newer recovered version of this Show is available.

[ Open Recovered ]  [ Open Last Saved ]
```

Recovery should preserve layout and configuration work without silently replacing a known-good saved file until the user accepts/saves the recovered state.

## Templates

Templates are stored independently from Shows.

### Built-in templates

Examples:

- Blank;
- Single Camera;
- 2-Up Horizontal;
- 2-Up Vertical;
- 2×2;
- 3×2;
- 4×2;
- 8 Camera View;
- Split 8 Cameras into two 2×2 Views.

### User templates

Users may save custom View designs for reuse across Shows.

A template may contain:

- generic Camera Slots;
- background artwork;
- logos;
- text styling;
- borders/frames;
- guides;
- canvas defaults;
- layout geometry.

A template should not require the IP addresses of a specific Show.

Where a completed View contains logical sources, `Save as Template` should be able to convert those camera elements into generic Camera Slots.

## New Show workflow

A clean New Show workflow could be:

```text
New Show
  ↓
Enter Show Name
  ↓
Choose Blank or Template
  ↓
Configure logical network roles
  ↓
Configure/add camera sources
  ↓
Create/assign Views and NDI Outputs
  ↓
Save .rchshow
```

Defaults should make a simple four-camera setup possible without forcing the user through every advanced option.

## Startup resilience

A Show should still open when some dependencies are missing.

Examples:

- missing camera NIC;
- missing NDI NIC;
- camera offline;
- optional asset error;
- unavailable credential.

The application should load the design and configuration, surface the missing dependency, and allow correction in Edit Mode rather than refusing to open the Show.

## Versioning and migration

The `.rchshow` format must contain a schema/version identifier.

Future versions of RoboCam-Hub should migrate older Show files forward where practical.

Migration should:

- preserve the original file until a successful new save;
- report unsupported/corrupt versions clearly;
- avoid destructive silent conversion.

## Initial acceptance tests

- save a complete Show to one `.rchshow` file;
- reopen it and reproduce Views, cameras, assets and NDI definitions accurately;
- move the Show to another computer and map network roles without editing each camera;
- remember role-to-NIC mappings per computer;
- preserve missing USB adapter mappings and restore them when the adapter reconnects;
- reopen every Show in Edit Mode regardless of the previous Show Mode state;
- verify Show Mode is not persisted as the startup mode;
- move the Show without breaking embedded image assets;
- autosave without corrupting the primary file during forced termination testing;
- recover unsaved work after an unclean shutdown;
- load a Show despite offline cameras or missing NIC mappings;
- save and reuse a custom View template with generic Camera Slots;
- validate migration from at least one older test schema once format evolution begins.

## Decisions currently adopted

- Shows are portable and should ideally be represented by one `.rchshow` file.
- Images/assets used by a Show travel with it.
- Show configuration references logical network roles rather than physical Windows NIC IDs.
- Physical NIC mappings are remembered per machine.
- Machine-specific UI/performance preferences do not travel in the Show.
- Credentials are not stored as plaintext portable Show data.
- Manual Save and autosave/recovery are both required.
- Templates are independent of Shows.
- Every Show always opens in Edit Mode.
- Show Mode is never restored automatically and the user is not prompted about this on load.

## Gate 6F implementation

Gate 6F implements the first production show-file contract. See
[`33-gate-6f-rchshow-persistence-recovery.md`](33-gate-6f-rchshow-persistence-recovery.md)
for the operator behavior, security ceilings, validation policy, and known
limitations, and
[`ADR 0004`](adr/0004-rchshow-container-and-recovery.md) for the durable archive,
asset, transactional-load, and recovery decisions.
