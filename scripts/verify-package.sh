#!/usr/bin/env bash
set -euo pipefail
app="${1:?usage: verify-package.sh /path/to/RoboCam-Hub.app}"
executable="$app/Contents/MacOS/RoboCam-Hub"
plugins="$app/Contents/MacOS/gstreamer"
[[ -x "$executable" && -f "$app/Contents/MacOS/librobocamhub_native.dylib" ]] || { echo "Managed or native executable is missing." >&2; exit 1; }
for plugin in libgstrtsp.dylib libgstrtp.dylib libgstvideoparsersbad.dylib libgstlibav.dylib libgstvideoconvertscale.dylib libgstapp.dylib; do
  [[ -f "$plugins/$plugin" ]] || { echo "Missing packaged plugin: $plugin" >&2; exit 1; }
done
while IFS= read -r binary; do
  if otool -L "$binary" | grep -E '/(Library/Frameworks/GStreamer|opt/homebrew|usr/local)/'; then
    echo "Packaged binary still references a developer-machine path: $binary" >&2; exit 1
  fi
done < <(find "$app/Contents/MacOS" -type f -perm -111)
env -u DYLD_LIBRARY_PATH "$executable" --package-smoke-test
