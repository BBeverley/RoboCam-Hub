#!/usr/bin/env bash
set -euo pipefail
app="${1:?usage: verify-package.sh /path/to/RoboCam-Hub.app}"
executable="$app/Contents/MacOS/RoboCam-Hub"
plugins="$app/Contents/MacOS/gstreamer-1.0"
[[ -x "$executable" && -f "$app/Contents/MacOS/librobocamhub_native.dylib" ]] || { echo "Managed or native executable is missing." >&2; exit 1; }
for plugin in libgstrtsp.dylib libgstrtp.dylib libgstvideoparsersbad.dylib libgstlibav.dylib libgstvideoconvertscale.dylib libgstapp.dylib; do
  [[ -f "$plugins/$plugin" ]] || { echo "Missing packaged plugin: $plugin" >&2; exit 1; }
done
if otool -L "$app/Contents/MacOS/librobocamhub_native.dylib" | grep -E '/(Library/Frameworks/GStreamer|opt/homebrew|usr/local)/'; then
  echo "Native core still references a developer-machine GStreamer path." >&2; exit 1
fi
"$executable" --package-smoke-test
