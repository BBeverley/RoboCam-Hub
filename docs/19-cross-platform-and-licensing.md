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

## Licence model

Initial commercial licensing model:

- licence belongs to a user/customer entitlement;
- one paid licence permits **two activated computers**;
- the two computers may be any supported combination of Windows and macOS;
- the licence is not tied to a Show;
- Show files remain portable between licensed machines;
- a **7-day full-access trial** is available before purchase.

A third simultaneous paid activation must not silently revoke another computer.

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

## Two-device enforcement

Every successful paid licence validation must also confirm that the entitlement does not have more than two currently registered/active devices.

Server-side activation state is authoritative.

Expected behaviour:

```text
Licence validation
      ↓
Entitlement valid?
      ↓
Current device registered?
      ↓
Registered-device count <= 2?
      ├─ Yes → issue/refresh 30-day lease
      └─ No  → do not refresh lease; require licence-manager action
```

A third device must not be activated while two other devices are already registered.

Suggested message:

```text
This licence already has 2 activated computers.

Deactivate an existing device before activating this computer.

[ Open Licence Manager ]
[ Retry ]
[ Cancel ]
```

The licensing backend must prevent race conditions that could otherwise allow more than two devices to be activated simultaneously.

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
Confirm entitlement + registered-device count
        ↓
Licence valid until = server time + 30 days
        ↓
App may run offline until that date
```

Whenever RoboCam-Hub has Internet connectivity, it should attempt to revalidate the licence in the background. A successful validation refreshes the local licence lease back to a full 30-day validity window.

The user does not need to manually refresh the licence during normal connected operation.

## Offline startup behaviour

While the signed local licence lease remains inside its 30-day validity period, RoboCam-Hub runs normally without Internet access.

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

Once the local paid-licence lease has expired, RoboCam-Hub must not enter normal application operation until the licence is successfully revalidated with the licensing server.

Suggested screen:

```text
ROBOCAM-HUB LICENCE REFRESH REQUIRED

This computer has not validated its licence for more than 30 days.
Connect to the Internet to refresh your licence.

[ Retry Licence Check ]
```

When validation succeeds and the entitlement/device registration is still valid, a new 30-day lease is issued and the application can continue.

## Licence expiry while the application is already running

Licence validity is enforced at application startup, not by interrupting a currently running production session.

If the paid licence becomes invalid, expires, is revoked, or the 30-day offline lease expires while RoboCam-Hub is already running:

- **do not stop or disable the application during that session**;
- do not stop camera ingest;
- do not stop View rendering;
- do not stop NDI Outputs;
- do not force the application to close;
- show a clear persistent warning that the licence will require attention before the next application launch/restart.

Example warning:

```text
Licence validation required

RoboCam-Hub will continue to operate for this session.
The licence must be refreshed before the application can be started again.

[ Check Licence Now ]
```

If the user restores a valid licence during the current session, the warning clears and the new lease is stored normally.

This behaviour is intentional: licensing must never become a live-show failure mode.

## 7-day full-access trial

RoboCam-Hub should provide a **7-day full-access evaluation trial** so a prospective user can test the real product with their own camera, NDI and grandMA3 environment.

The trial should include the normal core functionality, including:

- camera discovery and manual add;
- low-latency RTSP ingest;
- up to the normal supported camera count;
- View creation and editing;
- Show save/load;
- Show Mode;
- fullscreen monitoring;
- NDI output;
- normal diagnostics and network configuration.

The trial should not artificially watermark the NDI output or remove core features that are required to evaluate latency and production suitability.

Suggested first-run licensing screen:

```text
ROBOCAM-HUB

[ Start 7-Day Full Trial ]
[ Activate Licence ]
```

## Trial activation and timing

The 7-day period should be server-authoritative.

Recommended model:

1. user requests the trial while online;
2. licensing service creates a trial entitlement/start timestamp;
3. server returns a signed local trial token containing the expiry;
4. the application may operate offline until the signed trial expiry;
5. changing the local system clock must not trivially restart or extend the trial.

The trial should be a genuine seven calendar days from activation rather than seven launches or seven usage days.

## Trial expiry while running

Trial expiry follows the same live-production rule as paid-licence expiry.

If the seven-day trial reaches its expiry while RoboCam-Hub is already running:

- the current application session continues normally;
- camera and NDI operation continue;
- a clear warning is shown;
- the next application launch requires a paid activation before normal operation can continue.

Example:

```text
Your RoboCam-Hub trial has ended.

This session will continue normally.
A licence is required the next time RoboCam-Hub starts.

[ Buy / Activate Licence ]
```

## Trial to paid conversion

Purchasing/activating a paid licence after or during the trial must preserve:

- Shows;
- Views;
- camera configuration;
- machine NIC mappings;
- templates;
- application settings.

The user should not need to reinstall RoboCam-Hub or recreate their setup.

## Clock and tamper handling

Paid and trial validity must not rely only on the user's editable local wall clock.

Licence/trial tokens should contain signed server-issued timestamps. The client should use reasonable anti-rollback protection so manually changing the computer clock cannot trivially extend a licence or restart a trial.

If clock state appears suspicious, require online validation rather than permanently locking the installation.

## Background paid-licence refresh behaviour

When Internet is available, paid licence refresh should be lightweight and unobtrusive.

Recommended behaviour:

- attempt refresh shortly after application startup;
- refresh asynchronously when the existing lease is valid;
- verify entitlement validity and registered-device count on every successful server check;
- retry occasionally with backoff if the service is temporarily unreachable;
- successful validation replaces the previous local lease with a new signed 30-day lease.

## Licence status UI

Settings should expose enough information for the user to understand their licence state.

Paid example:

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

Trial example:

```text
Licence:          7-Day Trial
Trial Ends:       07 Sep 2026
Time Remaining:   4 days

[ Buy / Activate Licence ]
```

## Local deactivation

Settings should provide the ability to release the current computer's activation.

Successful deactivation releases the device slot on the server and removes the local paid activation token.

If the computer is offline when deactivation is requested, do not claim the server slot has been released until the server has actually confirmed it.

## Future web licence manager

The future marketing/purchase website should provide an authenticated licence manager with at least:

- purchase RoboCam-Hub;
- sign in to customer account;
- view licence/trial status;
- view activated devices;
- show device name and platform;
- show activation and last-validation information;
- remotely deactivate a lost/replaced machine;
- show available activation slots;
- download Windows and macOS installers;
- manage billing/renewal if required by the final commercial model.

The website is a later workstream, but the licensing API and entitlement data model must support this from the start.

## Licensing service responsibilities

The backend needs to manage:

- customers/users;
- paid licence entitlements;
- trial entitlements;
- two-device activation limit;
- registered devices;
- activation/deactivation;
- remote device deactivation;
- signed 30-day paid licence leases;
- signed 7-day trial leases;
- entitlement revocation;
- device-count enforcement on validation;
- audit history;
- billing/subscription state if required later.

Privileged signing secrets must remain server-side.

## Security principles

At minimum:

- HTTPS/TLS for licensing API communication;
- cryptographically signed local licence/trial leases;
- desktop validation using an embedded public verification key rather than the server signing secret;
- server-authoritative lease and trial expiry timestamps;
- platform-secure local token storage;
- reasonable clock rollback/tamper detection;
- atomic server-side enforcement of the two-device limit;
- rate limiting and abuse controls;
- collect the minimum machine information required for device identity;
- assume the desktop client can eventually be reverse engineered;
- never send camera credentials to the licensing service.

## Licensing acceptance tests

- activate one paid licence on two computers in any Windows/macOS combination;
- reject a third simultaneous activation;
- verify every paid online licence check confirms the device-count limit;
- issue a signed 30-day lease after successful paid validation;
- run offline while that lease remains valid;
- refresh the lease back to 30 days after a later successful check;
- refuse a new app session after the lease has expired until online validation succeeds;
- allow a currently running session to continue unchanged if the licence expires or is revoked while running;
- show a warning that the next restart will require licence validation;
- start a 7-day full-access trial;
- verify the trial has full core camera/View/NDI functionality;
- run the trial offline within its signed seven-day period;
- allow a running session to continue if the trial expires mid-session;
- block the next normal launch after trial expiry until a paid licence is activated;
- convert trial to paid without losing Shows/settings;
- deactivate one device and activate a replacement;
- remotely deactivate a device through the licence-management API;
- change USB/Thunderbolt NICs without consuming another activation;
- detect obvious local-clock rollback and require online validation.

## Decisions adopted

- RoboCam-Hub supports Windows and macOS from the first architecture.
- One paid licence allows a maximum of two registered/activated computers.
- Every successful paid licence check verifies the server-side registered-device count before issuing a new lease.
- A third device cannot activate while two device slots are already occupied.
- Successful paid validation grants 30 days of offline operation.
- Every later successful paid validation resets the offline validity period to 30 days.
- Once the 30-day lease has expired, a new application session requires Internet access and successful validation.
- If a paid licence becomes invalid while the application is already running, the current session continues fully and only the next restart is blocked; the user is warned clearly.
- RoboCam-Hub includes a **7-day full-access trial**.
- Trial expiry while the application is already running does not interrupt that session; the next launch requires paid activation.
- Trial-to-paid conversion preserves all user Shows and settings.
- Licensing is never allowed to interrupt live media operation mid-session.
- The future marketing/purchase website includes a web licence manager with device management and remote deactivation.

## Still to decide later

1. Perpetual licence, subscription licence or another commercial pricing model.
2. Whether activation uses licence code only, account login, or both.
3. Whether a trial requires account creation, email verification, payment method, or licence-code-style trial token.
4. Whether the trial may be activated on one or two devices.
5. Custom licensing backend versus a suitable third-party licensing platform.
6. Whether Intel macOS devices are formally supported or Apple Silicon only.
7. Desktop application update mechanism.
8. Final web purchase/billing stack.
