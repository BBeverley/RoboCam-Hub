# Gate 6F — `.rchshow` persistence, autosave and recovery

Gate 6F completes Phase 6 by making editor projects portable and restart-safe.
The implementation follows [ADR 0004](adr/0004-rchshow-container-and-recovery.md).

## Operator workflow

The File menu provides New, Open, Recent, Save, and Save As. The window shows
the current filename and a `*` while durable configuration is dirty. New, Open,
and application close use a Save / Don't Save / Cancel prompt when needed.
The default extension is `.rchshow`.

Every opened or recovered project starts in Edit Mode with fullscreen off and
fresh runtime status. A valid candidate is built completely before it replaces
the current runtime. Enabled cameras and enabled NDI Outputs start only after
the replacement is installed; one startup failure does not roll back or block
the rest of the project.

## Container schema v1

The ZIP contains:

```text
manifest.json
assets/<sha256-of-asset-id>.png
assets/<sha256-of-asset-id>.jpg
```

The manifest stores show identity, logical cameras, Views, every supported
scene-element property, stable asset metadata, and Output definitions/routing.
Local preview selection is operational state in schema v1, so a show opens on
its first View deterministically. The manifest excludes native handles,
preview/fullscreen/Show Mode state, runtime diagnostics, receiver counts,
transient errors, physical NIC mappings, machine paths, and machine preferences.

The original imported path is used only while packaging an asset. On load,
valid embedded content is placed in a disposable app-managed cache and supplied
to the native compositor through `RuntimeSourceReference`; elements continue to
refer durably to `AssetId`.

The loader has explicit schema dispatch. Version 1 is accepted; newer versions
are rejected with a clear error. A future version must add an explicit migration
stage rather than deserialize new data directly into current domain objects.

## Failure and recovery policy

Normal save writes and flushes a temporary archive beside the destination,
reopens and validates it, preserves the previous primary as `.bak`, then
atomically replaces the destination. A failed write or validation leaves the
previous primary intact.

Durable edits debounce for five seconds and then create a recovery container in
the per-user application-data directory. Writes are serialized, run away from
the UI/native media path, and never clear the dirty marker. Startup offers each
newer recovery as Recover, Discard, or Later. Recovering keeps the project dirty;
the main file changes only after the operator chooses Save.

Asset failure is intentionally partial and explicit: a missing or corrupt
declared asset emits an operator warning and omits its dependent image elements.
No substitute asset is chosen. Invalid IDs, references, transforms, schema,
container structure, or non-asset configuration fail atomically and leave the
currently open workspace alive.

Machine preferences are independently versioned JSON under the OS per-user local
application-data directory. Version 1 includes theme, window state/bounds,
recent files, last folder, physical NIC role mappings, and future decoder/
compositor preferences. None is copied into a show.

## Security and limits

The loader validates archive paths, entry uniqueness, the allow-listed layout,
PNG/JPEG signatures and metadata, SHA-256 hashes, dimensions, expanded sizes,
and compression ratios. Current ceilings are 4 MiB for the manifest, 64 MiB per
asset, 512 MiB expanded, 300 entries, and 200:1 compression. Domain limits cap
cameras, Views, Outputs, elements, dimensions, and transforms.

Schema v1 does not have secure credential indirection. An RTSP URI containing
user-info is rejected during save and load so credentials cannot be placed in
portable plaintext. Authenticated sources require a future machine-secure
credential-reference design.

## Verification scope and known limitations

Deterministic tests cover round trips for all durable model types and IDs,
portable PNG/JPEG packaging, machine-state exclusion, dirty/autosave/recovery,
corrupt input, security validation, atomic failure/backup, transactional load,
and post-load camera/Output startup order. Hardware camera and real NDI behavior
remain subject to the manual procedures in
[the technical spike specification](21-technical-spike-spec.md).

A macOS arm64 manual acceptance run used a representative project with two
cameras, two Views, seven elements (including freeform camera transform, text,
transparent PNG, JPEG, rectangle and frame), and one NDI Output. The resulting
container was 2,458 bytes for the deliberately tiny fixture images. On this
development machine, normal save took 246 ms, recovery/autosave serialization
took 63 ms (after the configured debounce), and load/materialization took 54 ms.
The app was closed, relaunched, and reopened with IDs/routing and native visual
output intact in Edit Mode with fullscreen off. A further camera edit was
autosaved, the process was terminated without a normal Save, and startup Recover
restored the camera while keeping the project dirty. These figures are smoke
measurements, not performance guarantees for production-size artwork.

Only one last-known-good `.bak` is retained. Cloud storage, collaborative
editing, templates, authenticated camera credential storage, and new NIC
selection UX are outside Gate 6F.
