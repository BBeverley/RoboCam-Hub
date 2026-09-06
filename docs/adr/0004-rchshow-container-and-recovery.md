# ADR 0004 — `.rchshow` container and recovery architecture

Status: Accepted for Gate 6F

## Context

RoboCam-Hub projects must survive restart, move between computers as one file,
and retain Gate 6D PNG/JPEG assets without making their original machine paths
durable. Persistence must also be isolated from native media threads and must
not allow a failed load to dismantle the show currently on air.

The current camera model represents an RTSP endpoint as a URI. It has no secure
credential-reference abstraction or platform credential-store adapter.

## Decision

`.rchshow` is a ZIP container with one UTF-8 `manifest.json` and embedded files
under `assets/`. Schema version 1 uses explicit persistence DTOs and explicit
DTO/domain mapping; runtime/domain objects are not serialized implicitly.

- Stable camera, View, element, asset, Output, and show IDs are preserved.
- Asset archive names are derived from the SHA-256 hash of the stable asset ID,
  plus the validated media extension. Original source paths never enter the
  archive. Each manifest asset records media type, byte length, dimensions, and
  a SHA-256 content digest.
- Loading materializes valid assets into an application-managed cache directory
  and creates local `RuntimeSourceReference` values there. Missing or corrupt
  declared assets produce explicit warnings and only their dependent image
  elements are omitted. Invalid references and invalid non-asset configuration
  fail the complete load.
- A parsed show is validated and used to construct a separate, stopped runtime.
  The active runtime is swapped only after candidate construction succeeds.
  Enabled cameras and NDI Outputs start after the full swap; an individual
  startup failure degrades that source/output without invalidating the project.
- Normal saves use a same-directory temporary file, durable flush where the OS
  supports it, validation of the completed archive, last-known-good `.bak`, and
  atomic rename/replace. The primary is never opened for in-place overwrite.
- Durable edits schedule one machine-local recovery write after five seconds of
  inactivity. Recovery writes are serialized and do not clear normal dirty
  state. New unsaved shows use their stable show ID; saved shows use a hash of
  their normalized source path. Recovery is offered only when newer than the
  normal file and never overwrites it without an explicit Save.
- UI state, runtime status, fullscreen/Show Mode, native handles, and physical
  NIC identifiers stay outside the container. Machine preferences use the
  operating system's per-user local application-data directory.
- Local preview selection is operational in schema v1. A loaded show selects
  its first View deterministically; Output routing remains independently durable.
- Schema loading dispatches explicitly by version. Unsupported future versions
  fail without mutating the active workspace; future migrations can add an
  explicit version-to-version step before domain mapping.

Until a secure credential-reference design is adopted, schema v1 rejects RTSP
URIs containing user-info on both save and load. This prevents accidental
plaintext secret persistence without inventing a credential architecture in
this gate.

## Security limits

An `.rchshow` is untrusted input. Version 1 permits PNG and JPEG only and caps:

- `manifest.json`: 4 MiB;
- each embedded asset: 64 MiB;
- total expanded archive: 512 MiB;
- archive entries: 300;
- compression ratio: 200:1;
- cameras, Views, and Outputs: 64 each, plus the existing scene/resource limits.

Absolute paths, `..`, empty path components, backslashes, duplicate archive
entries, directory/symlink-like entries, unexpected entries, invalid signatures,
unknown scene kinds, duplicate IDs, malformed JSON/ZIP data, invalid transforms,
and invalid references are rejected before candidate activation.

## Consequences

Portable shows are self-contained and deterministic. Asset degradation is
visible but does not sacrifice the rest of a valid design. Save/load/autosave
work stays in managed background orchestration and never copies decoded frames
or alters the native ABI.

Schema v1 cannot persist authenticated RTSP endpoints. Secure platform-backed
credential references, rolling backup history beyond one `.bak`, templates,
and cross-device preference synchronization remain future work.
