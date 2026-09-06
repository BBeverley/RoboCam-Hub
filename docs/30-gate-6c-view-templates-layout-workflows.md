# 30 — Gate 6C View Templates and Layout Workflows

## Scope

Gate 6C makes common touring layouts quick to create without introducing a
template-aware compositor. Templates, placeholder assignment and duplication
remain in Domain/Application/Avalonia. Runtime and native receive only an
ordinary immutable `ViewDefinition.SceneElements` collection:

```text
portable template slots + optional logical-camera choices
                         ↓ instantiate
ordinary CameraElementDefinition values with fresh element IDs
                         ↓ existing Gate 6A atomic scene path
native View compositor → clean preview + existing NDI Outputs
```

No native ABI, compositor, ingest/decode, preview, NDI, persistence, Show Mode,
fullscreen, licensing or GPU behavior changes in this gate.

## Template data model

`ViewTemplateDefinition` contains a stable template ID, display name and an
ordered read-only slot collection. Each `ViewTemplateSlotDefinition` contains:

- a stable portable slot ID and optional display label;
- normalized X, Y, width and height;
- Z-order;
- crop, clockwise rotation and horizontal/vertical flip;
- visible/enabled flags and Stretch/Contain/Cover fit mode.

A slot deliberately has no camera ID, RTSP URL, credential, NIC identity,
runtime handle or native resource. Slot IDs identify positions inside a
template; they never become scene element IDs.

The built-in catalog is data-driven and read-only. Grid boundaries are computed
from rational row/column boundaries rather than repeatedly accumulating tile
widths, keeping adjacent edges deterministic and the final edge exactly at the
normalized canvas boundary.

## Built-in templates

The compact Gate 6C catalog is:

| Template | Slot geometry | Z-order |
| --- | --- | --- |
| 1-Up | one full-canvas slot | 0 |
| 2-Up Horizontal | two equal left/right slots | 0, 1 |
| 2-Up Vertical | two equal top/bottom slots | 0, 1 |
| 3-Up | three equal horizontal slots | 0–2 |
| 4-Up / 2×2 | four equal two-column/two-row slots | 0–3 |
| 4×2 | eight equal four-column/two-row slots | 0–7 |
| Picture-in-Picture | full-canvas main plus a 0.30 × 0.30 bottom-right inset | 0, 1 |

Built-in slots use Stretch so tiled destinations contain no transparent gaps
and retain the established legacy grid behavior. Each instantiated element can
immediately change fit, crop or any other Gate 6B property.

## Creation and placeholder semantics

`Add View…` opens one compact modal:

```text
choose Blank or template
→ name the View
→ optionally assign a logical camera to each slot
→ Create
```

Blank creates a valid explicit freeform View with no scene elements. A template
slot may be left empty; no fake-camera or placeholder native element is emitted.
The operator can replace a pending assignment through its camera picker or use
Clear to return it to empty. The same logical camera may be selected in multiple
slots because all resulting elements consume the same native latest-frame state.

Instantiation walks slots in template order. Assigned slots become ordinary
`CameraElementDefinition` values with newly generated stable element IDs and
the slot's complete transform. Unassigned slots are omitted. Template and slot
identity is not retained as authoritative behavior in the resulting View.
Polling updates camera health only and does not rebuild or overwrite the modal's
pending assignment draft.

After successful creation, the new View becomes the selected editor View and
the local preview switches through the existing preview path. Existing Output
definitions keep their original `ViewId`; selecting or creating a local View
does not reroute an NDI Output.

## View duplication

`Duplicate View…` copies the currently selected View and asks only for the new
name. Duplication:

- generates a new stable View ID;
- generates a new stable ID for every scene element;
- preserves logical camera references;
- preserves X/Y/width/height, crop, rotation, flips, visibility, enabled state,
  fit mode and Z-order;
- creates an independent `ViewWorkspaceViewModel` and Gate 6B editor;
- does not create or copy Output definitions;
- does not alter existing Output routing.

Editing the duplicate applies only to its own immutable scene. Repeated camera
references across original/duplicate Views remain consumers of the configured
logical camera and do not create additional RTSP sessions or decoders.

## Validation

Deterministic Domain/Application coverage verifies template validation and
portability, stable slot IDs, every built-in layout, gap-free grid boundaries,
picture-in-picture overlap/Z-order, partial and repeated assignments, fresh
View/element IDs, complete transform preservation, duplicate independence,
polling-safe drafts, Gate 6B editing after creation, preview selection and
unchanged Output routing/ingest ownership.

Manual validation was completed on 2026-09-06 on macOS 14.7.1 x86_64 using
four independent local RTSP/H.264 test sources and NDI Video Monitor 5.2. The
following workflows were exercised through the Avalonia application:

- created and switched to a blank View;
- created assigned 2-Up Horizontal and 4-Up / 2×2 Views;
- created a Picture-in-Picture View, then changed the inset position, width and
  rotation through the existing Gate 6B properties editor;
- duplicated that edited View, changed the duplicate independently, and
  switched back to confirm that the original transform was unchanged;
- observed the local clean native preview update for each selected View;
- kept a running NDI Output routed to the pre-existing Main 2×2 View while
  creating, selecting, editing and duplicating other Views; NDI Video Monitor
  continued to show the original routed View;
- observed RTSP-session/decoder ownership remain exactly 4/4 throughout View
  creation, switching and duplication, then reach 0/0 after all cameras were
  stopped normally.

The only awkwardness found during this pass was clipped helper copy at the top
of the creation sheet. The sheet now wraps that text; no blocking workflow or
assignment ambiguity remained in the repeated manual pass.

The local test sources prove deterministic workflow and ownership behavior,
not four-physical-camera interoperability or network NDI discovery. Gate 4A's
separate official-SDK validation covers the live NDI transport path.

## Current limitations

- Built-in templates contain camera slots only; Gate 6D text/image/shape
  elements are outside this gate.
- Templates are process-owned built-ins. Saving, importing, sharing or syncing
  user templates awaits the persistence design.
- Source assignment is performed in the creation modal; template placeholders
  do not persist in instantiated Views.
- Template thumbnails, a permanent browser, drag-and-drop slot assignment,
  custom layout generation and automatic Output creation are intentionally
  omitted.
- The 4×2 template exposes eight optional slots; practical live population is
  bounded by the currently configured cameras and existing runtime capability.
