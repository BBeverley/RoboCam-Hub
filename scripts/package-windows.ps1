param([string]$OutputRoot = "")
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot "dist\windows-x64" }
$gstRoot = if ($env:GSTREAMER_ROOT_X86_64) { $env:GSTREAMER_ROOT_X86_64 } else { "C:\gstreamer\1.0\msvc_x86_64" }
$stage = Join-Path $repoRoot ".packaging\windows"
$publish = Join-Path $stage "publish"

if (-not (Test-Path (Join-Path $gstRoot "lib\gstreamer-1.0"))) {
    throw "Official GStreamer runtime not found below $gstRoot. Set GSTREAMER_ROOT_X86_64."
}
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $OutputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publish, $OutputRoot, (Join-Path $OutputRoot "gstreamer-1.0"), (Join-Path $OutputRoot "licenses") -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src\RoboCamHub.App\RoboCamHub.App.csproj") `
    --configuration Release --runtime win-x64 --self-contained true --output $publish `
    -p:RchPackageWithoutNdi=true
Copy-Item (Join-Path $publish "*") $OutputRoot -Recurse -Force

# Copy the runtime DLL set for reliable deployment. Plugin discovery remains restricted
# to the explicit media allow-list below, avoiding unrelated codec/plugin activation.
Copy-Item (Join-Path $gstRoot "bin\*.dll") $OutputRoot -Force
$plugins = @(
    "gstapp.dll", "gstcoreelements.dll", "gstjpeg.dll", "gstlibav.dll",
    "gstplayback.dll", "gstpng.dll", "gstrtp.dll", "gstrtsp.dll",
    "gsttypefindfunctions.dll", "gstvideoconvertscale.dll", "gstvideoparsersbad.dll"
)
foreach ($plugin in $plugins) {
    $source = Join-Path $gstRoot "lib\gstreamer-1.0\$plugin"
    if (-not (Test-Path $source)) { throw "Required GStreamer plugin is missing: $source" }
    Copy-Item $source (Join-Path $OutputRoot "gstreamer-1.0")
}
$scannerCandidates = @(
    (Join-Path $gstRoot "libexec\gstreamer-1.0\gst-plugin-scanner.exe"),
    (Join-Path $gstRoot "bin\gst-plugin-scanner.exe")
)
$scanner = $scannerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $scanner) { throw "GStreamer plugin scanner was not found below $gstRoot." }
Copy-Item $scanner (Join-Path $OutputRoot "gstreamer-1.0\gst-plugin-scanner.exe")
Copy-Item (Join-Path $repoRoot "scripts\packaging\THIRD-PARTY-NOTICES.md") $OutputRoot
$license = @(
    (Join-Path $gstRoot "share\gstreamer-1.0\LICENSE"),
    (Join-Path $gstRoot "COPYING"),
    (Join-Path $gstRoot "LICENSE")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $license) { throw "The GStreamer redistribution license was not found below $gstRoot." }
Copy-Item $license (Join-Path $OutputRoot "licenses\GStreamer-LICENSE")

& (Join-Path $repoRoot "scripts\verify-package.ps1") $OutputRoot
Write-Host "Created $OutputRoot"
