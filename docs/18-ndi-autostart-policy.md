# 18 — NDI Auto-Start Policy

## Purpose

Lock the startup policy for configured NDI Outputs in RoboCam-Hub.

This decision supersedes any earlier wording in `07-ndi-output.md` or `17-ndi-runtime-behaviour.md` that describes NDI auto-start as optional or user-configurable.

## Core rule

**All enabled/configured NDI Outputs automatically start when a Show is opened.**

There is no per-output `Start Automatically` toggle in the normal product model.

A user should not need to remember to start NDI streams after loading a show.

## Show load sequence

```text
Open Show
   ↓
Load Views / Cameras / Outputs
   ↓
Resolve machine-specific network mappings
   ↓
Start camera ingest
   ↓
Start every enabled NDI Output as soon as its prerequisites are available
   ↓
Workspace opens/remains in Edit Mode
```

Show Mode remains independent of NDI startup. A Show always opens in Edit Mode, while its enabled NDI Outputs start automatically.

## Missing prerequisites

An enabled Output that cannot start immediately should remain armed and waiting rather than becoming permanently stopped.

Examples:

```text
Waiting for Network Mapping
Waiting for NIC
Waiting for View
Starting
Broadcasting
```

When the missing NIC or mapping becomes available, the Output should start automatically without requiring the user to press Start.

## Manual Stop / Restart

Users may still Stop or Restart an Output during the current application session for troubleshooting or operational reasons.

Stopping an Output manually is a runtime action and does not change the default startup policy.

When the Show is closed and opened again, every enabled configured Output should again attempt to start automatically.

If a user does not want an Output to start as part of that Show, the Output should be explicitly disabled or removed from the Show configuration rather than relying on an auto-start preference.

## Enabled versus stopped

The data model should distinguish:

- **Enabled** — this Output belongs to the active Show and should auto-start whenever possible;
- **Disabled** — this Output remains configured but intentionally does not run;
- **Stopped (runtime)** — an enabled Output that the operator has temporarily stopped during the current session.

This keeps persistent intent separate from temporary operational state.

## Failure recovery

Auto-start policy also applies to recovery.

If an enabled Output loses its configured NIC or sender fails:

- mark the Output degraded/waiting;
- do not affect camera ingest or other Outputs;
- do not accumulate stale video frames;
- automatically retry/recover using bounded retry behaviour;
- resume publication with the same NDI source name and View when prerequisites return.

## UI implications

The NDI Output settings do not need a `Start Automatically` checkbox.

A typical Output configuration becomes:

```text
Name:             Spots A
NDI Source Name:  ROBOCAM - SPOTS A
View:             Spots A
Enabled:          On
Resolution:       Match View
Frame Rate:       Match View
NDI Mode:         High Bandwidth
NDI Network:      NDI Network A
```

Runtime controls may still provide:

```text
Stop
Restart
```

and `Start` when an enabled Output has been manually stopped during the current session.

## Decisions adopted

- NDI auto-start is mandatory for enabled configured Outputs.
- There is no normal per-output auto-start toggle.
- Shows always open in Edit Mode; this does not prevent NDI from starting automatically.
- Missing NICs/mappings place Outputs into waiting states and they start automatically when resolved.
- Manual Stop is session-only unless the Output is explicitly disabled.
- Disabled Outputs remain configured but do not auto-start.
