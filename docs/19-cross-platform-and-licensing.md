# 19 — Cross-Platform Support and Licensing

## Purpose

Define the cross-platform requirement and initial licensing model for RoboCam-Hub.

RoboCam-Hub must support both Windows and macOS. A purchased licence is assigned to a user/customer and allows up to two activated computers at the same time.

The marketing, purchase and web licence-management website is a later implementation phase, but the desktop application must be architected around this licensing model from the beginning.

## Platform requirement

Initial supported desktop platforms:

- Windows;
- macOS.

The same `.rchshow` file must be portable between supported platforms. Platform-specific machine settings such as physical NIC mappings remain local to each computer.

Example:

```text
Show
├─ Camera Network A
└─ NDI Network A

Windows touring laptop
├─ Camera Network A → USB Ethernet Adapter #2
└─ NDI Network A    → Intel I225

MacBook
├─ Camera Network A → USB / Thunderbolt Ethernet
└─ NDI Network A    → second Ethernet interface
```

Cross-platform support is a first-architecture requirement rather than a future port.

## Platform abstraction

Platform-specific implementation should be isolated behind clear interfaces for:

- NIC discovery and stable adapter identity;
- secure credential and licence-token storage;
- GStreamer packaging/runtime differences;
- hardware decode backends;
- NDI SDK integration details;
- GPU/rendering backends;
- fullscreen/window behaviour;
- native file paths and application-data storage;
- installers, code signing and application updates;
- macOS signing and notarisation.

The core camera, View, Output and Show data models should remain platform-independent.

## Shared media architecture

The preferred architecture remains conceptually common across Windows and macOS:

```text
RTSP Cameras
    ↓
GStreamer ingest/decode
    ↓
Latest-frame / frame-router state
    ↓
GPU compositor
    ↓
Clean View frame
    ├─ Local application preview
    └─ NDI sender
```

Framework and rendering choices must therefore be evaluated for native-quality support on both platforms.

## Cross-platform validation

Both platforms must be tested for:

- Profile 2 RTSP ingest;
- UDP and TCP ingest;
- eight simultaneous 720p60 cameras;
- low-latency decode behaviour;
- software and practical hardware decode paths;
- GPU composition;
- multiple simultaneous 1080p60 NDI High Bandwidth outputs;
- NIC discovery and stable identity;
- USB / Thunderbolt Ethernet removal and reconnection;
- fullscreen monitoring;
- autosave and crash recovery;
- secure local credential storage;
- code signing and packaged runtime dependencies.

## Licence model

Initial commercial licensing model:

- licence belongs to a user/customer entitlement;
- one licence permits **two activated computers**;
- the two computers may be any supported combination of Windows and macOS;
- the licence is not tied to a Show;
- Show files remain portable between licensed machines.

Example:

```text
User Account
└─ RoboCam-Hub Licence
   ├─ Touring Laptop — Windows
   └─ Backup MacBook — macOS
```

A third simultaneous activation must not silently revoke another computer.

## Activation

The user should be able to activate RoboCam-Hub with a licence code or equivalent entitlement flow supplied by the future purchase system.

Conceptually:

```text
ACTIVATE ROBOCAM-HUB

Licence Code
[ XXXX-XXXX-XXXX-XXXX ]

[ Activate ]
```

The backend entitlement remains authoritative even if a human-readable licence code is used.

After activation, the device should receive a signed local licence token/lease stored securely on that computer.

No normal Show workflow should require the licence code to be repeatedly entered.

## Two-device limit

When two devices are already activated, a third activation should display a clear message rather than automatically removing an existing device.

Example:

```text
This licence already has 2 activated computers.

Deactivate an existing device in the RoboCam-Hub licence manager before activating this computer.

[ Open Licence Manager ]
[ Cancel ]
```

This makes activation predictable for touring users who may intentionally maintain a primary and backup machine.

## Device identity

Licence activation needs a stable local device identity.

Do not use a removable NIC or MAC address as the sole identity because users may frequently change USB Ethernet adapters, Thunderbolt docks and network hardware.

The implementation should use a privacy-conscious installation/device identity with limited platform fingerprinting where useful.

Changing venue networking or swapping a USB NIC must not consume a new activation.

## Offline operation

RoboCam-Hub is intended for live-event environments where Internet connectivity may be unavailable or unreliable.

A valid activated installation must therefore continue to work offline.

Recommended model:

1. initial activation requires a connection to the licensing service;
2. the service returns a cryptographically signed local licence lease/token;
3. the application validates that token locally;
4. periodic online revalidation refreshes the entitlement when Internet is available;
5. temporary lack of Internet does not prevent normal operation while the local licence remains valid.

The exact offline revalidation period is a later commercial/security decision, but it should be generous enough for touring. A very short daily-style online requirement is inappropriate.

## Never interrupt an active show

A temporary licensing-server failure or loss of Internet must not abruptly stop an already running production session.

In particular it must not immediately stop:

- camera ingest;
- View rendering;
- NDI Outputs;
- fullscreen monitoring.

Licence problems should be surfaced at startup or in Settings in a controlled way. Active media output should not be dependent on continuous server connectivity.

## Local deactivation

Settings should provide a licence area with the ability to release the current computer's activation.

Example:

```text
Settings → Licence

Licence: Active
Devices: 2 of 2

[ Deactivate This Computer ]
[ Manage Licence Online ]
```

Successful deactivation releases the device slot on the server.

## Future web licence manager

The marketing / purchase website will later include an authenticated customer licence manager.

Expected capabilities include:

- purchase RoboCam-Hub;
- customer account sign-in;
- view licence entitlement/status;
- view activated devices;
- show device name and platform;
- show activation / last-validation information;
- remotely deactivate a lost, broken or inaccessible device;
- manage billing/renewal if the eventual commercial model requires it;
- download Windows and macOS installers;
- access invoices/receipts where appropriate.

Example:

```text
ROBOCAM-HUB LICENCE

Devices: 2 / 2

Tour-Laptop
Windows
[ Deactivate ]

Ben-MacBook
macOS
[ Deactivate ]
```

The website itself is a later workstream. Its required API boundary should nevertheless be considered when the desktop licence client is designed.

## Licensing service boundary

Licensing should be architecturally separate from the real-time media engine.

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
Future purchase + licence-management website
```

Camera ingest, composition and NDI must not require continuous availability of the licensing API.

## Server responsibilities

The future licensing backend will likely need to manage:

- customers/users;
- licence entitlements;
- activation limit;
- registered devices;
- activation and deactivation;
- remote device deactivation;
- signed licence token issuance;
- token refresh/revalidation;
- entitlement revocation;
- billing/subscription state if later required;
- audit history for licence actions.

Privileged signing secrets must remain server-side and must never ship inside the desktop application.

## Security principles

At minimum:

- HTTPS/TLS for licensing API communication;
- cryptographically signed local licence tokens;
- desktop validation using an embedded public verification key rather than a server signing secret;
- platform-secure local token storage where practical;
- rate limiting and abuse controls on activation endpoints;
- collect the minimum machine information required for device identity;
- assume the desktop client can eventually be reverse engineered;
- never send camera credentials to the licensing service.

## Packaging implications

Windows distribution requires a signed native application/installer and a suitable update mechanism if automatic updates are adopted.

macOS distribution requires a signed application, Apple notarisation and an appropriate DMG/PKG or equivalent delivery path. Apple Silicon should be treated as a primary macOS target.

Whether older Intel Macs are formally supported should be decided after dependency and performance testing rather than assumed.

## Initial acceptance tests

- open the same `.rchshow` on Windows and macOS;
- independently map logical camera/NDI network roles on each platform;
- ingest supported camera streams on Windows and macOS;
- publish NDI on Windows and macOS;
- activate one licence on a Windows computer;
- activate the same licence on a Mac;
- confirm both consume the two available device slots;
- reject a third simultaneous activation cleanly;
- deactivate one computer and activate a replacement;
- remotely deactivate a device through a test licence-management API;
- remain operational offline after valid activation;
- survive temporary licence-server outage without interrupting active media;
- change USB/Thunderbolt network adapters without consuming another activation;
- update/reinstall within defined rules without accidental duplicate activation.

## Decisions adopted

- RoboCam-Hub will support both Windows and macOS.
- Cross-platform support influences the first architecture and framework choice.
- The portable Show format is shared between Windows and macOS.
- One licence allows a maximum of two simultaneously activated computers.
- Activation slots are platform-agnostic: any supported Windows/macOS combination is valid.
- Licensing is associated with the user/customer entitlement, not individual Shows.
- The application must remain usable offline after successful activation for a suitable touring-friendly period.
- Temporary Internet or licence-server loss must not immediately interrupt an active show.
- The current device can be deactivated locally.
- The future marketing/purchase website will include a web licence manager with remote device deactivation.
- Website implementation is a later phase, but the desktop licensing architecture must support it from the beginning.

## Still to decide later

1. Perpetual licence, subscription licence or another commercial pricing model.
2. Exact offline validation/grace period.
3. Whether activation uses licence code only, account login, or both.
4. Custom licensing backend versus a suitable third-party licensing platform.
5. Whether Intel macOS devices are formally supported or Apple Silicon only.
6. Desktop application update mechanism.
7. Final web purchase/billing stack.
