# 19 — Cross-Platform Support and Licensing

## Purpose

Define the cross-platform requirement and initial licensing model for RoboCam-Hub.

RoboCam-Hub must support both Windows and macOS. A purchased licence is user-based and allows up to two activated computers at the same time. A 7-day free trial is also part of the initial commercial model.

## Platform requirement

Initial supported desktop platforms:

- Windows;
- macOS.

The same `.rchshow` file should open on either platform, with only machine-specific items such as NIC mappings needing to be resolved locally.

Platform-specific code should be isolated behind clear interfaces for:

- NIC discovery and stable adapter identity;
- secure credential/licence-token storage;
- GStreamer packaging/runtime differences;
- NDI SDK integration details;
- GPU/rendering backends;
- installers, signing and notarisation;
- platform application-data/file locations.

Cross-platform support is a hard architectural requirement and must influence the first implementation choices rather than being added later.

## Licensing model

Initial commercial model:

- licence belongs to a user/customer account;
- one purchased licence allows **two activated computers**;
- the two devices may be any supported combination of Windows and macOS;
- licence is not tied to a particular Show;
- Show files remain portable between licensed machines.

Example:

```text
User Account
└─ RoboCam-Hub Licence
   ├─ Touring Laptop — Windows
   └─ Backup MacBook — macOS
```

A third activation should not silently evict another device. The user should be directed to the licence manager to deactivate an existing machine first.

## Licence activation

A human-readable licence code may be provided, but the backend customer account and entitlement record should remain authoritative.

Suggested activation flow:

```text
Activate RoboCam-Hub

Licence Code
[ XXXX-XXXX-XXXX-XXXX ]

[ Activate ]
```

On successful activation, the service registers the local device and returns a signed local licence lease/token stored using platform-secure storage.

## 7-day free trial

RoboCam-Hub should offer a **7-day free trial**.

The trial should provide the real application experience rather than a crippled demo so users can validate it with their own camera/NDI setup before purchasing.

Recommended trial behaviour:

- 7 calendar days from first successful trial activation;
- one trial entitlement per user/account, with reasonable anti-abuse controls;
- full core functionality during the trial, including camera ingest, View editing, Show Mode, NDI output and Show save/load;
- no artificial watermark on NDI output;
- no shortened runtime/session limit;
- clear trial status and remaining time in the UI;
- conversion to a paid licence should preserve existing Shows and settings.

Suggested first-run UI:

```text
RoboCam-Hub

[ Start 7-Day Free Trial ]
[ Activate Licence ]

Already purchased? Enter your licence code.
```

During the trial:

```text
Trial Active — 5 days remaining
```

Near expiry, show a non-blocking reminder rather than interrupting normal operation.

## Trial expiry

Trial enforcement must respect live-production reliability.

The application should never abruptly terminate an active NDI output or camera session in the middle of a running show simply because the trial clock expires while the application is already open.

Recommended policy:

- trial validity is checked at launch and periodically in the background;
- if the trial expires during a running session, show a clear warning and allow the current session to continue;
- the next clean application launch requires purchase/activation before normal production use continues;
- existing Show files remain accessible and are never deleted or corrupted because a trial expired.

Exact post-trial restrictions can be refined later, but active-show continuity is mandatory.

## Offline touring requirement

The purchased application must remain usable without continuous Internet access.

Recommended model:

1. online activation;
2. signed local licence lease stored securely;
3. operation offline for a substantial grace period;
4. background refresh when Internet becomes available;
5. failure to reach the licensing API never immediately stops an active show.

A starting design target is approximately 30 days between successful paid-licence refreshes, subject to later security/commercial review.

For the 7-day trial, the trial start timestamp and entitlement should similarly be server-authoritative at creation, with a signed local trial token enabling expected offline use during the trial period.

## Device identity

Use a stable local installation/device ID plus limited platform fingerprinting where necessary. Avoid invasive or fragile hardware locking.

Do not use only:

- MAC address;
- IP address;
- hostname;
- removable NIC identity;
- any single easily changed hardware property.

Users must be able to recover from hardware replacement or OS reinstall through the account portal.

## Account / Licence UI

Settings should contain an `Account / Licence` section.

Paid example:

```text
Licence:        Active
Devices:        1 of 2 used
Last Checked:   3 days ago
Offline Until:  28 Sep 2026

[ Manage Licence ]
[ Refresh Licence ]
[ Deactivate This Computer ]
```

Trial example:

```text
Licence:        Free Trial
Time Remaining: 5 days
Trial Ends:     05 Sep 2026

[ Buy RoboCam-Hub ]
```

Normal operation should not be cluttered with licensing information unless action is required.

## Future web licence manager

The later marketing/purchase website should include an authenticated licence-management area.

Expected scope:

```text
Account
├─ Profile
├─ Purchases / Billing
├─ RoboCam-Hub Licence
│  ├─ Status
│  ├─ Trial / Purchase history
│  ├─ Devices — maximum 2 paid activations
│  └─ Deactivate Device
└─ Downloads
   ├─ Windows
   └─ macOS
```

The portal should eventually allow users to view active devices, deactivate lost/replaced machines, see licence/trial status and download installers.

Website implementation is a later workstream, but the licensing API should be designed for both the desktop app and future web portal from the beginning.

## Licensing service boundary

Licensing is a separate service from the media engine.

```text
Desktop App
├─ Camera/GStreamer engine
├─ View compositor
├─ NDI engine
├─ Show persistence
└─ Licence client
       ↓ occasional HTTPS validation
Licensing API
       ↓
Customer / entitlement database
       ↓
Future account/licence website
```

Camera ingest, composition and NDI must not depend on continuous server connectivity.

## Security principles

- HTTPS-only licensing API;
- server-authoritative activation and trial state;
- signed local trial/licence leases;
- platform-secure token storage;
- no embedded master licence secret in the client;
- activation rate limiting and abuse controls;
- auditable activation/deactivation events;
- licence reset/reissue capability;
- assume the desktop client can be reverse engineered.

## Cross-platform implementation implications

Before selecting the desktop framework, evaluate candidates against:

- first-class Windows and macOS support;
- embedded GStreamer control;
- native NDI SDK access;
- high-performance GPU video surfaces;
- free-form editor performance;
- multi-NIC enumeration;
- code signing and update distribution;
- Apple Developer ID signing/notarisation;
- secure local credential/token storage;
- keeping media processing off the UI thread.

## Initial acceptance tests

- open the same `.rchshow` on Windows and macOS;
- remap logical camera/NDI network roles independently on both platforms;
- ingest supported Profile 2 camera feeds on both platforms;
- publish NDI on both platforms;
- activate one paid licence on two machines in any Windows/macOS combination;
- reject a third simultaneous paid activation cleanly;
- deactivate one machine and activate another;
- start a 7-day free trial;
- retain full core app functionality during the trial;
- convert trial to paid without losing Shows/settings;
- handle trial expiry without stopping an already running show;
- run a paid licence offline under a valid local lease;
- survive licensing API outage without disrupting active camera/NDI operation;
- follow Auto/Light/Dark system theme behaviour on both platforms.

## Decisions adopted

- RoboCam-Hub targets Windows and macOS from the first architecture.
- One paid licence allows two simultaneously activated computers.
- Activation slots are platform-agnostic.
- RoboCam-Hub offers a 7-day free trial.
- Trial should expose the real core product rather than a restricted demo.
- Trial expiry must never abruptly stop an active show session.
- A future web licence manager will be part of the marketing/purchase website.
- Desktop operation must tolerate extended loss of Internet connectivity after valid activation.
