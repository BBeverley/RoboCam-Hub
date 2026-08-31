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

## Licence model

Initial commercial licensing model:

- licence belongs to a user/customer entitlement;
- one licence permits **two activated computers**;
- the two computers may be any supported combination of Windows and macOS;
- the licence is not tied to a Show;
- Show files remain portable between licensed machines.

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

After activation, the device receives a cryptographically signed local licence lease/token stored securely on that computer.

No normal Show workflow should require the licence code to be repeatedly entered.

## Two-device limit

When two devices are already activated, a third activation should display a clear message rather than automatically removing an existing device.

```text
This licence already has 2 activated computers.

Deactivate an existing device in the RoboCam-Hub licence manager before activating this computer.

[ Open Licence Manager ]
[ Cancel ]
```

## Device identity

Licence activation needs a stable local device identity.

Do not use a removable NIC or MAC address as the sole identity because users may frequently change USB Ethernet adapters, Thunderbolt docks and network hardware.

Changing venue networking or swapping a USB/Thunderbolt NIC must not consume a new activation.

## 30-day licence revalidation policy

A paid activation is valid offline for **30 days from the most recent successful licence-server validation**.

The application should maintain a server-authoritative `valid_until` value or equivalent signed expiry inside the local licence lease.

Conceptually:

```text
Successful online validation
        ↓
Licence valid until = server time + 30 days
        ↓
App may run offline until that date
```

Whenever RoboCam-Hub has Internet connectivity, it should attempt to revalidate the licence in the background. A successful validation refreshes the local licence lease back to a full 30-day validity window.

Example:

```text
1 Sep  — licence validates online
         Offline validity → 1 Oct

10 Sep — Internet available, validation succeeds
         Offline validity → 10 Oct

25 Sep — Internet available, validation succeeds
         Offline validity → 25 Oct
```

The user does not need to manually refresh the licence during normal connected operation.

## Offline behaviour

While the signed local licence lease remains inside its 30-day validity period, RoboCam-Hub runs normally without Internet access.

Internet availability is therefore not a runtime dependency for camera ingest, View rendering, NDI output, Show Mode or fullscreen monitoring.

The normal startup sequence is:

```text
Launch RoboCam-Hub
        ↓
Validate signed local licence lease
        ↓
Lease still valid?
   ├─ Yes → app may start immediately
   │        └─ if Internet is available, refresh in background
   │
   └─ No  → online licence refresh required before app can run
```

## More than 30 days offline

Once the local licence lease has expired, RoboCam-Hub must not enter normal application operation until the licence is successfully revalidated with the licensing server.

Suggested screen:

```text
ROBOCAM-HUB LICENCE REFRESH REQUIRED

This computer has not validated its licence for more than 30 days.
Connect to the Internet to refresh your licence.

Last validated: 01 Sep 2026

[ Retry Licence Check ]

Network status: Offline
```

When Internet access becomes available and the entitlement is still valid:

```text
Licence validated successfully.
Offline access renewed for 30 days.

[ Continue ]
```

The user should not need to re-enter the licence code unless the local activation itself has been removed, corrupted or explicitly deactivated.

## Behaviour if licence entitlement is no longer valid

If the licence server reports that the entitlement has expired, been revoked or otherwise become invalid, RoboCam-Hub should not refresh the local lease.

The application should clearly distinguish this from an Internet/connectivity failure.

Examples:

```text
Cannot reach licence server
```

versus:

```text
Licence is no longer active
```

The latter may provide a link to the future account/licence website.

## Do not interrupt a currently running show

The 30-day validity check controls whether a new normal application session may start.

RoboCam-Hub must not terminate camera ingest or NDI output merely because the local lease reaches its expiry time while the application is already running.

If a lease expires during an active session:

- show a clear warning;
- continue the current application session;
- continue camera ingest, View rendering and NDI output;
- require successful online revalidation before the next normal application launch.

This prevents a licence timer from becoming a live-show failure mode.

## Clock and tamper handling

The 30-day policy must not rely only on the user's editable local wall clock.

The licence token should contain server-issued signed timestamps, including the current lease expiry. The client should use reasonable anti-rollback protection so manually changing the computer clock cannot trivially extend a licence indefinitely.

The implementation should favour predictable user recovery over aggressive hardware DRM. If clock state appears invalid, require an online revalidation rather than permanently locking the installation.

## Background refresh behaviour

When Internet is available, licence refresh should be lightweight and unobtrusive.

Recommended behaviour:

- attempt refresh shortly after application startup;
- refresh asynchronously without delaying media startup when the existing lease is valid;
- retry occasionally while connected if the first check fails;
- do not repeatedly hammer the service;
- a temporary licensing API outage does not make an otherwise valid 30-day lease unusable;
- successful validation immediately replaces the previous local lease with a newly signed 30-day lease.

Exact retry intervals are an implementation detail and should include backoff.

## Licence status UI

Settings should expose enough information for the user to understand their offline state.

Example:

```text
Settings → Account / Licence

Licence:          Active
Devices:          2 of 2
Last Validated:   10 Sep 2026
Offline Until:    10 Oct 2026

[ Check Licence Now ]
[ Deactivate This Computer ]
[ Manage Licence Online ]
```

As expiry approaches, the application may show a non-blocking warning, for example at seven days remaining, so touring users have time to connect the machine before it becomes unusable on the next launch.

## Local deactivation

Settings should provide the ability to release the current computer's activation.

Successful deactivation releases the device slot on the server and removes the local activation token.

If the computer is offline when the user requests deactivation, the exact behaviour should be handled conservatively; do not pretend the server slot has been released when it has not.

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
       ↓ periodic HTTPS validation
Licensing API
       ↓
Customer / entitlement database
       ↓
Future purchase + licence-management website
```

Camera ingest, composition and NDI do not require continuous availability of the licensing API while the local lease is valid.

## Server responsibilities

The future licensing backend will likely need to manage:

- customers/users;
- licence entitlements;
- two-device activation limit;
- registered devices;
- activation and deactivation;
- remote device deactivation;
- signed 30-day licence lease issuance;
- lease refresh/revalidation;
- entitlement revocation;
- billing/subscription state if later required;
- audit history for licence actions.

Privileged signing secrets must remain server-side and must never ship inside the desktop application.

## Security principles

At minimum:

- HTTPS/TLS for licensing API communication;
- cryptographically signed local licence leases;
- desktop validation using an embedded public verification key rather than a server signing secret;
- server-authoritative lease expiry timestamps;
- platform-secure local token storage where practical;
- reasonable local clock rollback/tamper detection;
- rate limiting and abuse controls on activation endpoints;
- collect the minimum machine information required for device identity;
- assume the desktop client can eventually be reverse engineered;
- never send camera credentials to the licensing service.

## Cross-platform validation

Both platforms must be tested for:

- Profile 2 RTSP ingest;
- UDP and TCP ingest;
- eight simultaneous 720p60 cameras;
- low-latency decode behaviour;
- GPU composition;
- multiple simultaneous 1080p60 NDI High Bandwidth outputs;
- NIC discovery and stable identity;
- USB / Thunderbolt Ethernet removal and reconnection;
- fullscreen monitoring;
- autosave and crash recovery;
- secure local credential and licence storage;
- code signing and packaged runtime dependencies.

## Licensing acceptance tests

- activate one licence on two computers in any supported Windows/macOS combination;
- reject a third simultaneous activation cleanly;
- issue a signed local 30-day lease after successful validation;
- launch normally with no Internet while that lease remains valid;
- successfully validate online and reset the offline window to 30 days from that validation;
- automatically refresh a valid licence in the background when Internet is available;
- refuse normal app startup after more than 30 days without a successful validation;
- allow the user to connect to the Internet, refresh the licence and immediately continue;
- distinguish licence-server connectivity failure from an invalid/revoked entitlement;
- do not terminate a currently running media session if its lease crosses the expiry point;
- require validation on the next launch after such an expiry;
- tolerate temporary licensing-server outage while the existing local lease remains valid;
- change USB/Thunderbolt network adapters without consuming another activation;
- detect obvious system-clock rollback and request online validation rather than extending the lease;
- deactivate one computer and activate a replacement.

## Decisions adopted

- RoboCam-Hub will support both Windows and macOS.
- Cross-platform support influences the first architecture and framework choice.
- The portable Show format is shared between Windows and macOS.
- One licence allows a maximum of two simultaneously activated computers.
- Activation slots are platform-agnostic.
- Licensing is associated with the user/customer entitlement, not individual Shows.
- Initial activation requires access to the licensing service.
- A successful licence-server check grants **30 days of offline use**.
- Every successful later licence-server check resets the offline validity period back to **30 days from that check**.
- While the local lease is valid, loss of Internet does not restrict normal application operation.
- After more than 30 days without a successful validation, the application will not enter normal operation until it connects to the licensing service and refreshes the licence.
- Licence expiry while the application is already running will not terminate an active show; revalidation is required before the next launch.
- The current device can be deactivated locally.
- The future marketing/purchase website will include a web licence manager with remote device deactivation.
- Website implementation is a later phase, but the desktop licensing architecture must support it from the beginning.

## Still to decide later

1. Perpetual licence, subscription licence or another commercial pricing model.
2. Whether activation uses licence code only, account login, or both.
3. Custom licensing backend versus a suitable third-party licensing platform.
4. Whether Intel macOS devices are formally supported or Apple Silicon only.
5. Desktop application update mechanism.
6. Final web purchase/billing stack.
