# RoboCam-Hub — Coding Agent Instructions

This repository is designed to be developed with AI coding agents such as OpenAI Codex and GitHub Copilot. These instructions are canonical for all coding agents working in this repository.

## 1. Read before changing code

Before implementing a task:

1. Read this file.
2. Read the task/issue in full.
3. Read every `/docs` file listed by the task.
4. Search for any existing ADR that governs the subsystem.
5. Inspect existing tests and nearby implementation before editing.

Do not invent product behaviour when the repository already specifies it.

## 2. Source of truth

The `/docs` directory contains adopted product and architecture decisions. When code, a task prompt, and documentation conflict, stop and report the conflict rather than silently changing the architecture.

Important references include:

- `docs/02-system-architecture.md`
- `docs/03-camera-ingest.md`
- `docs/06-multiview-engine.md`
- `docs/07-ndi-output.md`
- `docs/13-performance-targets.md`
- `docs/17-ndi-runtime-behaviour.md`
- `docs/18-ndi-autostart-policy.md`
- `docs/19-cross-platform-and-licensing.md`
- `docs/20-technology-stack-decision.md`
- `docs/21-technical-spike-spec.md`
- `docs/22-ai-agent-development-workflow.md`
- `docs/adr/` for engineering decisions

## 3. Non-negotiable architecture

### Platforms

Windows and macOS are first-class targets. Do not introduce architecture that only works on one platform unless the task explicitly concerns a platform adapter behind an existing abstraction.

### Managed application layer

Avalonia / C# owns:

- application shell and presentation;
- View/editor interaction;
- settings and setup workflows;
- show/domain state;
- persistence orchestration;
- licensing UX/orchestration;
- low-frequency runtime status presentation.

### Native media core

C++20 owns the performance-critical media path:

- RTSP sessions;
- GStreamer pipelines;
- H.264 decoding;
- latest-frame ownership;
- frame routing;
- GPU/frame resources;
- View composition;
- NDI senders;
- high-frequency media diagnostics.

Do not move these responsibilities into managed C# simply because doing so is easier for the current task.

## 4. Critical single-ingest invariant

For every configured logical camera:

```text
RoboCam-Hub RTSP sessions <= 1
Decoder pipelines         <= 1
```

A configured camera may have many consumers:

```text
Camera
  ↓ one RTSP connection
  ↓ one decode pipeline
  ↓ shared latest-frame state
  ├─ View A
  ├─ View B
  ├─ local preview
  ├─ fullscreen
  ├─ NDI Output A
  └─ NDI Output B
```

Adding a View, preview, fullscreen monitor or NDI output must never create another RTSP connection or decoder for an already configured camera.

Treat violation of this rule as a correctness bug, not merely a performance regression.

Add or preserve diagnostics that make active RTSP-session and decoder counts observable in tests and debug builds.

## 5. Frame ownership and latency

RoboCam-Hub prioritises current frames over stale continuity.

Required principles:

- do not create unbounded frame queues;
- use latest-frame semantics where specified;
- slow UI/preview/NDI consumers must not back-pressure camera ingest;
- do not copy full-resolution decoded frames repeatedly across the C++/.NET boundary;
- do not route every decoded frame through managed C# and back to native code;
- keep frame ownership explicit;
- prefer bounded/leaky asynchronous boundaries;
- a failed camera must not block healthy cameras;
- a failed NDI output must not stop ingest or other outputs.

## 6. Managed/native boundary

The interop contract is governed by `docs/adr/0001-native-interop-abi.md`.

General rule: expose a narrow versioned C ABI from the native library and consume it from .NET through P/Invoke or an equivalent thin managed wrapper.

Do not expose C++ classes, STL containers, exceptions, allocator ownership, or language-specific object layouts across the ABI.

High-frequency media frames must remain native. Managed code should exchange commands, configuration, IDs, status snapshots, error information and native rendering/surface handles where explicitly designed.

## 7. Scope discipline

Make the smallest coherent change that satisfies the task.

Do not:

- perform unrelated refactors;
- change adopted UX/product behaviour;
- replace Avalonia;
- replace the C++ media core;
- change supported platforms;
- weaken the single-ingest invariant;
- change licensing/trial/device-limit behaviour;
- change NDI autostart behaviour;
- introduce a major dependency with licensing implications without explicit approval;
- silently make a currently portable subsystem platform-specific.

If a broader change is genuinely required, explain why and stop for an architecture decision if necessary.

## 8. Tests are part of the change

Behavioural changes require automated tests where reasonably possible.

Prefer testing observable invariants over implementation details.

Examples:

- adding consumers does not increase RTSP-session count;
- adding consumers does not increase decoder count;
- removing a camera releases its pipeline;
- two distinct cameras own distinct pipelines;
- one camera failure does not stop another camera;
- slow output cannot create unbounded queue growth;
- show-file round trips preserve stable IDs;
- enabled outputs attempt startup when prerequisites return.

Never delete, weaken or skip a valid test merely to make CI pass.

## 9. Hardware-dependent work

Normal CI cannot prove real-camera or real-NDI performance.

Keep deterministic unit/integration tests separate from manual hardware validation defined by `docs/21-technical-spike-spec.md`.

When a task requires hardware evidence and hardware is unavailable, implement the deterministic portion, clearly report what remains unverified, and do not claim the hardware acceptance criterion passed.

## 10. Build and verification

Before completing a task:

1. build every affected project you can build in the available environment;
2. run relevant tests;
3. run formatting/static checks configured for the affected area;
4. inspect warnings/errors rather than ignoring them;
5. report commands run and results.

Do not claim a platform build passed if you did not run it or CI did not run it.

## 11. Error handling

Failures in the media layer should be explicit and diagnosable. Preserve distinctions such as:

- camera unreachable;
- RTSP session failure;
- packet loss/degraded stream;
- decoder failure;
- compositor overload;
- NDI sender failure;
- camera NIC unavailable;
- NDI NIC unavailable.

Avoid collapsing distinct runtime conditions into one generic failure when the architecture expects targeted recovery.

## 12. Licensing safety

Licensing is not allowed to become a live-show failure mode.

Do not change these adopted rules without explicit product approval:

- one paid licence allows at most two registered/activated computers;
- seven-day full-access evaluation trial;
- paid activation receives a signed 30-day offline lease after successful validation;
- licence entitlement/device count is checked by the licensing service;
- licence expiry or invalidation while the application is already running must warn but must not terminate the current media session;
- revalidation is required before the next normal launch where applicable.

## 13. NDI output safety

NDI must consume the clean composed View frame. Do not capture the application window.

Enabled outputs auto-start when their prerequisites are available. A user stopping an output for the current session does not silently rewrite the persistent Enabled state.

## 14. Git / PR behaviour

Prefer one bounded task per branch/PR.

A PR should explain:

- what changed;
- why;
- requirements/docs implemented;
- tests/builds run;
- benchmark results where relevant;
- limitations or unverified hardware behaviour;
- architecture questions requiring human input.

Do not mix unrelated cleanup into a feature PR.

## 15. When to stop and ask

Stop and report rather than guessing when a task would require:

- changing an adopted product decision;
- changing the interop ABI strategy;
- moving frame processing through managed memory;
- introducing multiple RTSP sessions for one configured camera;
- replacing GStreamer, Avalonia or the native media-core architecture;
- changing the show-file compatibility model;
- changing licence/trial/device-limit rules;
- introducing a major runtime/dependency with licence or redistribution implications.

## 16. Completion report

At the end of a coding task, provide a concise report containing:

```text
Implemented:
Tests added/updated:
Commands run:
Results:
Known limitations / hardware checks still required:
Docs/ADR impact:
```

Generated code alone is not a completed task.
