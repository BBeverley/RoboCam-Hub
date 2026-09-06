# Phase 6 Developer Preview packaging

## Status and scope

This checkpoint produces unsigned, copyable operator-review builds. It is not a
release process, installer, signing/notarisation design, or a change to media,
Show Mode, persistence, or NDI behaviour.

Supported preview targets are:

- Apple Silicon (`osx-arm64`), macOS 14 or later, matching the current .NET 10
  support floor. CI builds and smoke-tests on macOS 15; macOS 14 still requires
  product-owner validation.
- Windows x64 (`win-x64`), Windows 11 24H2 or later, or Windows 10 Enterprise
  LTSC 2021 (21H2). CI builds and smoke-tests on Windows Server 2025;
  interactive desktop validation remains manual.

Intel macOS is not a supported package target for this checkpoint, consistent
with the Apple-Silicon-first decision in `docs/20-technology-stack-decision.md`.

## One-command builds

From a fresh checkout after installing the prerequisites:

```bash
./scripts/package-macos.sh
```

This creates `dist/macos/RoboCam-Hub.app`.

```powershell
.\scripts\package-windows.ps1
```

This creates `dist\windows-x64\RoboCam-Hub.exe` and adjacent dependencies.
Both scripts delete only their own `dist` target and `.packaging` staging area.

### Build prerequisites

Both platforms require the .NET 10 SDK, CMake 3.25 or later, a C++20 compiler,
`pkg-config`, and the official GStreamer 1.28.6 runtime/development package.
The macOS package must be built on Apple Silicon with Xcode command-line tools;
the official package must be installed at
`/Library/Frameworks/GStreamer.framework/Versions/1.0` unless
`GSTREAMER_ROOT` points elsewhere. Windows uses the official MSVC x86-64
GStreamer package and `GSTREAMER_ROOT_X86_64` when it is not installed at
`C:\gstreamer\1.0\msvc_x86_64`.

These are build-host prerequisites only. The extracted application does not
require the .NET runtime, CMake, compiler tools, source checkout, Homebrew,
Visual Studio, Xcode, or a developer shell.

## Bundle contents and runtime lookup

The package contains:

- a self-contained .NET 10 application and Avalonia resources, including the
  packaged Inter font assets;
- `librobocamhub_native.dylib` or `robocamhub_native.dll`;
- GStreamer core/application libraries and runtime dependencies;
- an isolated plugin directory containing the elements used by current RTSP,
  RTP/H.264, decode, conversion, image, queue, and appsink pipelines;
- the GStreamer plugin scanner and third-party notices.

The Windows packager copies the official runtime DLL set because recursively
resolving the MSVC runtime graph is fragile across GStreamer patch releases. It
does not copy the whole SDK and exposes only the constrained plugin directory.
The macOS packager computes the dependency closure of the native core, plugin
scanner, and allow-listed plugins, then rewrites their install names into the
bundle. This is why neither package depends on a machine-wide GStreamer install.

At startup the application detects the packaged plugin directory before native
initialisation and sets GStreamer's system-plugin and scanner paths. It clears
the optional plugin path, preventing a developer-machine plugin installation
from masking an incomplete package. The package smoke test also rejects an
absolute Homebrew or GStreamer-framework reference in the macOS native core.

The application uses normal per-user application-data locations for preferences,
autosave/recovery, and caches; packaging does not redirect them into the bundle
or source tree. `.rchshow` Open and Save therefore use the same Gate 6F paths and
schema as a development launch.

## NDI policy

Public CI artifacts do **not** contain NDI SDK headers or proprietary runtime
binaries and build the existing deterministic sender-core backend. Installing
an NDI runtime beside those artifacts does not convert them into an SDK-backed
build.

An SDK-backed private package currently requires rebuilding the native core with
`RCH_ENABLE_NDI_SDK=ON` against an installed official NDI SDK and arranging its
runtime under the vendor's current distribution licence. On macOS the development
SDK is normally `/Library/NDI SDK for Apple`; on Windows set `RCH_NDI_SDK_ROOT` or
`NDI_SDK_ROOT` to the installed SDK. The application distributor must retain the
vendor licence file, include the required NDI attribution/link in end-user
documentation, and confirm the current NDI SDK agreement before copying a binary.
This checkpoint intentionally does not automate that legally sensitive copy.

## Runtime prerequisites and unsigned warnings

The .NET and GStreamer runtimes are bundled. Windows packages include the MSVC
runtime DLL dependencies delivered by the official GStreamer redistributable,
so Visual C++ Build Tools are not required on the target machine. Windows may
show a SmartScreen warning because the executable is not Authenticode-signed.

Relocated macOS Mach-O libraries are ad-hoc signed so they can load, but the app
bundle has no production identity and is not notarised. Gatekeeper may warn or
block a downloaded artifact; use Finder's **Open** confirmation for this trusted
developer-preview build. Do not remove quarantine globally.

## Verification

Each packaging command performs a package-specific smoke test that checks the
managed executable, native core, required plugin files, packaged lookup setup,
and successful native/GStreamer engine creation. CI also runs the existing native
fixture suite, including deterministic local RTSP/H.264 decode and image/view
composition coverage, before packaging. That provides deterministic media-path
coverage but is not evidence of interactive preview rendering.

Perform this macOS operator check from an extracted artifact:

1. Launch `RoboCam-Hub.app` from Finder and create, save, close, and reopen an
   `.rchshow` outside the checkout.
2. Start the deterministic RTSP fixture described in
   `docs/21-technical-spike-spec.md`, add it as a camera, and confirm native preview.
3. Enter and exit fullscreen Show Mode, then quit normally.
4. For a separately authorised SDK-backed private build, start an NDI sender and
   confirm it from a receiver on the selected NDI network.

On Windows, copy/extract the complete `windows-x64` directory to a clean temporary
location, double-click `RoboCam-Hub.exe`, and repeat the show-file, deterministic
RTSP/native-preview, fullscreen, and normal-quit checks. Check NDI only with an
SDK-backed private build. Never move only the executable away from its DLL and
`gstreamer-1.0` siblings.

Record OS version, CPU, package SHA, package size, warnings, and results. Physical
camera/NDI soak and performance acceptance remain the manual hardware validation
defined by `docs/21-technical-spike-spec.md`.

## CI artifacts

For pushes and pull requests targeting `main`, each successful matrix job uploads:

- `RoboCam-Hub-macos-<full-commit-sha>.zip`
- `RoboCam-Hub-windows-x64-<full-commit-sha>.zip`

The artifact name and zip filename contain the exact checked-out commit SHA. CI
does not create a GitHub Release and does not claim Finder/double-click, video,
fullscreen, NDI receiver, or normal interactive quit validation.

## Licensing references

- GStreamer deployment and LGPL guidance: https://gstreamer.freedesktop.org/documentation/deploying/
- GStreamer licence FAQ: https://gstreamer.freedesktop.org/documentation/frequently-asked-questions/licensing.html
- NDI SDK licensing: https://docs.ndi.video/all/developing-with-ndi/sdk/licensing
- NDI software distribution: https://docs.ndi.video/all/developing-with-ndi/sdk/software-distribution
- Current .NET operating-system support: https://learn.microsoft.com/dotnet/core/install/
