param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference = "Stop"
$exe = Join-Path $PackageRoot "RoboCam-Hub.exe"
$native = Join-Path $PackageRoot "robocamhub_native.dll"
if (-not (Test-Path $exe) -or -not (Test-Path $native)) { throw "Managed or native executable is missing." }
foreach ($plugin in @("gstrtsp.dll", "gstrtp.dll", "gstvideoparsersbad.dll", "gstlibav.dll", "gstvideoconvertscale.dll", "gstapp.dll")) {
    if (-not (Test-Path (Join-Path $PackageRoot "gstreamer-1.0\$plugin"))) { throw "Missing packaged plugin: $plugin" }
}
$process = Start-Process -FilePath $exe -ArgumentList "--package-smoke-test" -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) { throw "Packaged smoke test failed with exit code $($process.ExitCode)." }
