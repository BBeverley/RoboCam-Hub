# 22 — AI Agent Development Workflow

## Purpose

Define a development workflow that assumes RoboCam-Hub will be built primarily with the assistance of AI coding agents such as Codex or GitHub Copilot.

The goal is not to let an agent freely redesign the product. The repository documentation is the source of truth, and agents work within explicit architecture, acceptance criteria and safety boundaries.

## Core approach

RoboCam-Hub should be developed as a sequence of small, testable work packages rather than large open-ended prompts.

Each work package should contain:

- objective;
- relevant architecture/docs to read first;
- files/subsystems allowed to change;
- constraints/invariants;
- acceptance criteria;
- tests/benchmarks required;
- explicit non-goals;
- expected deliverables.

The agent should implement, test, report results, and stop at the defined boundary.

## Repository as source of truth

The existing `/docs` specifications define product behaviour and architectural constraints.

Agents must not reinterpret or silently replace adopted decisions simply because another implementation is easier.

If an implementation conflicts with a documented decision, the agent should report the conflict rather than change the product behaviour automatically.

Key non-negotiable examples include:

- Windows and macOS are first-class targets;
- Avalonia is the application/UI layer;
- performance-critical media processing remains in the native C++20 media core;
- one configured camera owns exactly one RoboCam-Hub RTSP session and one decode pipeline;
- decoded frames are shared internally across all Views, previews and NDI outputs;
- no additional RTSP session may be created because a camera appears in multiple Views;
- the UI must not open independent camera streams;
- NDI uses the clean View frame, never application-window capture;
- freshness is preferred over queueing stale frames;
- enabled NDI Outputs auto-start when their prerequisites are available;
- live media is not interrupted by licence expiry during a running session.

## Root agent instructions

Create a root `AGENTS.md` before implementation begins.

It should tell every coding agent to:

1. read the relevant `/docs` files before editing code;
2. preserve documented architecture and invariants;
3. make the smallest coherent change that satisfies the task;
4. do not refactor unrelated areas without explicit reason;
5. add/update automated tests with behavioural changes;
6. run relevant builds/tests before completion;
7. report commands run and failures encountered;
8. never weaken tests merely to make CI pass;
9. never introduce a second RTSP/decode pipeline for an already configured camera;
10. never route live camera frames through managed C# memory unless the task explicitly proves that path is acceptable;
11. keep full-resolution video/frame ownership inside the native media/rendering boundary;
12. ask/report when a requirement is ambiguous rather than inventing product behaviour.

Subdirectory-specific `AGENTS.md` files may later add narrower rules for `native/`, `src/`, tests and packaging.

## Proposed repository structure

```text
RoboCam-Hub/
├─ AGENTS.md
├─ README.md
├─ docs/
│  ├─ product + UX specifications
│  ├─ architecture decisions
│  └─ spike specifications
│
├─ src/
│  ├─ RoboCamHub.App/             Avalonia application
│  ├─ RoboCamHub.Domain/          platform-neutral app/domain model
│  ├─ RoboCamHub.Persistence/     show files/settings
│  ├─ RoboCamHub.Licensing/       licence client
│  └─ RoboCamHub.NativeInterop/   managed/native boundary
│
├─ native/
│  ├─ CMakeLists.txt
│  ├─ include/
│  └─ src/
│     ├─ ingest/
│     ├─ frames/
│     ├─ compositor/
│     ├─ ndi/
│     ├─ diagnostics/
│     └─ platform/
│        ├─ windows/
│        └─ macos/
│
├─ tests/
│  ├─ managed/
│  ├─ native/
│  ├─ integration/
│  └─ fixtures/
│
├─ tools/
└─ .github/
   └─ workflows/
```

Exact project names can change during bootstrap, but the architectural separation should remain.

## Native boundary

The managed/native API should be narrow, versioned and explicit.

Good examples:

```text
CreateCamera(config)
RemoveCamera(cameraId)
UpdateCameraConfig(cameraId, config)
GetCameraStatus(cameraId)
CreateView(viewDefinition)
UpdateView(viewDefinition)
CreateNdiOutput(outputDefinition)
GetRuntimeSnapshot()
```

Avoid an API in which every decoded frame is copied into C# and then sent back to native code.

The native engine owns:

- RTSP sessions;
- GStreamer pipelines;
- decoders;
- latest-frame storage;
- GPU/frame resources;
- composition;
- NDI senders;
- high-frequency runtime metrics.

Avalonia/C# owns:

- application shell;
- editor interaction;
- settings/wizards;
- show/domain state;
- licence UX/client orchestration;
- low-frequency status presentation.

## Single-ingest invariant

This is a required automated invariant.

For every configured logical camera:

```text
active_rtsp_session_count <= 1
active_decoder_count <= 1
```

A camera may have many internal consumers:

```text
Camera A
├─ View 1
├─ View 2
├─ local preview
├─ fullscreen
├─ NDI Output 1
└─ NDI Output 2
```

but these consumers must fan out from the already decoded latest-frame state.

Integration tests should deliberately reference the same camera from multiple Views and verify RTSP/decode counts remain unchanged.

## Agent task format

Every implementation task should be written in a reusable format.

Example:

```markdown
# Task: Native camera registry and single-session ownership

## Read first
- docs/02-system-architecture.md
- docs/03-camera-ingest.md
- docs/21-technical-spike-spec.md
- AGENTS.md

## Objective
Implement the native camera registry that owns exactly one ingest pipeline per configured camera ID.

## Constraints
- C++20 only in native core.
- One camera ID may own at most one RTSP session and one decoder.
- Multiple consumers must reuse the same latest-frame state.
- Do not implement Avalonia preview in this task.

## Acceptance criteria
- creating the same camera twice does not create a second pipeline;
- two different cameras create two pipelines;
- removal releases pipeline resources;
- consumer count can increase without increasing RTSP/decode count;
- automated tests cover all cases.

## Deliverables
- implementation;
- tests;
- brief implementation notes;
- commands/results used to verify the task.
```

This format works with either Codex or Copilot-based agents and reduces prompt drift.

## Phase gates

Agents should not jump directly to the finished application.

Recommended high-level development gates:

### Gate 0 — Repository bootstrap

- root `AGENTS.md`;
- managed solution bootstrap;
- native CMake bootstrap;
- Windows/macOS CI skeleton;
- formatting/linting/testing conventions;
- documented managed/native ABI strategy.

### Gate 1 — Single-camera native ingest

- embedded GStreamer;
- one Profile 2 RTSP source;
- low-latency UDP path;
- latest-frame state;
- diagnostics/session counters;
- reconnect behaviour.

### Gate 2 — Multi-camera ownership

- camera registry;
- 1 → 4 → 8 streams;
- single-session/decode invariant tests;
- independent reconnect/failure domains.

### Gate 3 — Composition

- two independent 2×2 Views;
- shared decoded camera frames;
- same camera usable in multiple Views without additional decode;
- performance counters.

### Gate 4 — NDI

- direct clean-frame NDI output;
- two simultaneous 1080p60 outputs;
- output isolation and recovery;
- no application-window capture.

### Gate 5 — Avalonia preview/interop

- application shell;
- local preview path from native compositor;
- no additional RTSP session/decode;
- managed/native lifecycle testing.

### Gate 6 — Technical spike pass

- complete `docs/21-technical-spike-spec.md` acceptance testing on Windows and Apple Silicon macOS;
- soak test;
- performance report;
- architecture go/no-go decision.

Only after Gate 6 should agents begin broad product UI development.

## Git workflow

Prefer one branch/PR per bounded task.

Example:

```text
main
 ├─ agent/native-camera-registry
 ├─ agent/gstreamer-single-ingest
 ├─ agent/ndi-sender-spike
 └─ agent/avalonia-preview-interop
```

PRs should be small enough to review and revert independently.

Do not allow an agent to continuously commit large unrelated changes directly to `main`.

## Pull request requirements

Every agent-created PR should contain:

- what changed;
- why it changed;
- docs/requirements implemented;
- tests run;
- benchmark changes where applicable;
- known limitations;
- any architecture question requiring human decision.

For media-path work, include runtime metrics where meaningful.

## CI strategy

CI is essential because AI agents need an objective definition of done.

Initial CI should eventually include:

- managed build/tests on Windows and macOS;
- native CMake build/tests on Windows and macOS;
- formatting/static-analysis checks;
- ABI/interface tests;
- unit tests for persistence/domain/licensing logic;
- deterministic media-core tests using synthetic/local fixtures where real cameras are not available.

Hardware-camera and NDI benchmark tests should remain a separate manual/hardware validation suite rather than blocking every ordinary PR.

## Test design for agents

Prefer tests around observable invariants rather than implementation details.

Examples:

- one logical camera creates one pipeline;
- adding View consumers does not create another pipeline;
- a slow output does not increase camera queue depth;
- camera disconnect does not stop another camera;
- NDI output failure does not stop ingest;
- show file round-trip preserves stable IDs;
- disabled output does not start;
- enabled output attempts automatic startup when prerequisites are available.

Avoid brittle tests that merely mirror implementation internals.

## Architecture decision records

Use small ADR files for future significant engineering decisions that are not already product decisions.

Suggested path:

```text
docs/adr/
  0001-native-interop-abi.md
  0002-frame-sharing-strategy.md
  0003-gpu-backend.md
```

An agent may propose an ADR, but significant architecture changes should not be adopted solely because an agent decided them during implementation.

## Agent permissions / autonomy

Agents may autonomously:

- implement tasks with defined acceptance criteria;
- add tests;
- fix local bugs required to complete the task;
- improve diagnostics needed for validation;
- update implementation notes/docs when behaviour is unchanged.

Agents should stop/report before:

- changing an adopted product behaviour;
- replacing Avalonia or the C++ media core;
- changing the one-session/one-decode invariant;
- moving frame processing into managed memory;
- changing show-file compatibility policy;
- changing licensing/trial/device-limit behaviour;
- changing supported platforms;
- introducing a major new runtime/dependency with licensing implications.

## Codex vs Copilot

The repository should remain tool-neutral.

Both Codex and Copilot-style coding agents can work effectively if the repo provides:

- strong `AGENTS.md` guidance;
- self-contained issues/task briefs;
- deterministic tests;
- small PR boundaries;
- clear architecture docs;
- CI results;
- explicit ownership boundaries.

Do not encode essential requirements only in tool-specific prompt configuration.

If a tool-specific instruction file is later useful, it should point back to the canonical repository docs rather than duplicate product requirements that may drift.

## Human role

The human owner should primarily make:

- product decisions;
- architecture decisions at explicit gates;
- hardware/real-world acceptance decisions;
- release decisions.

Agents should handle most implementation, test authoring, routine bug fixing and documentation updates within those boundaries.

The goal is minimal intervention, not zero governance.

## Definition of done for agent tasks

A task is not complete because code was generated.

It is complete when:

```text
requirements satisfied
+ relevant tests pass
+ required platform build passes
+ documented invariants preserved
+ diagnostics/benchmarks meet task criteria
+ PR explains what changed
```

## Initial next action

Before implementing the technical spike:

1. add the root `AGENTS.md`;
2. define the repository/project skeleton;
3. choose and document the native C ABI / interop strategy;
4. establish Windows/macOS CI;
5. convert Technical Spike Gate 1 into the first agent-ready implementation tasks.

This creates an environment in which Codex, Copilot or another coding agent can operate with substantially less supervision and less architectural drift.
