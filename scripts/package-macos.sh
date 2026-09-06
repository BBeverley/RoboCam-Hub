#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
output_root="${1:-$repo_root/dist/macos}"
gst_root="${GSTREAMER_ROOT:-/Library/Frameworks/GStreamer.framework/Versions/1.0}"
rid="osx-arm64"
stage="$repo_root/.packaging/macos"
publish="$stage/publish"
app="$output_root/RoboCam-Hub.app"
macos="$app/Contents/MacOS"
runtime="$macos/gstreamer-1.0"

[[ "$(uname -s)" == Darwin ]] || { echo "macOS packaging must run on macOS." >&2; exit 1; }
[[ "$(uname -m)" == arm64 ]] || { echo "The Developer Preview macOS package must be built on Apple Silicon." >&2; exit 1; }
[[ -d "$gst_root/lib/gstreamer-1.0" ]] || { echo "Official GStreamer runtime not found at $gst_root." >&2; exit 1; }

rm -rf "$stage" "$app"
mkdir -p "$publish" "$runtime" "$app/Contents/Resources/licenses"

dotnet publish "$repo_root/src/RoboCamHub.App/RoboCamHub.App.csproj" \
  --configuration Release --runtime "$rid" --self-contained true \
  --output "$publish" -p:RchPackageWithoutNdi=true
cp -R "$publish/." "$macos/"

plugins=(
  libgstapp.dylib libgstcoreelements.dylib libgstjpeg.dylib libgstlibav.dylib
  libgstplayback.dylib libgstpng.dylib libgstrtp.dylib libgstrtsp.dylib
  libgsttypefindfunctions.dylib libgstvideoconvertscale.dylib libgstvideoparsersbad.dylib
)
for plugin in "${plugins[@]}"; do
  source_path="$gst_root/lib/gstreamer-1.0/$plugin"
  [[ -f "$source_path" ]] || { echo "Required GStreamer plugin is missing: $source_path" >&2; exit 1; }
  cp "$source_path" "$runtime/$plugin"
done

scanner="$gst_root/libexec/gstreamer-1.0/gst-plugin-scanner"
[[ -x "$scanner" ]] || { echo "GStreamer plugin scanner is missing: $scanner" >&2; exit 1; }
cp "$scanner" "$runtime/gst-plugin-scanner"
chmod +x "$runtime/gst-plugin-scanner"

thin_arm64() {
  local binary="$1" architectures
  file "$binary" | grep -q 'Mach-O' || return
  architectures="$(lipo -archs "$binary")"
  [[ " $architectures " == *" arm64 "* ]] || {
    echo "Packaged Mach-O file has no arm64 slice: $binary ($architectures)" >&2; exit 1;
  }
  if [[ "$architectures" != "arm64" ]]; then
    lipo "$binary" -thin arm64 -output "$binary.thin"
    mv "$binary.thin" "$binary"
  fi
}

thin_arm64 "$macos/librobocamhub_native.dylib"
for binary in "$runtime"/*; do thin_arm64 "$binary"; done

resolve_dependency() {
  local dependency="$1" owner="$2" basename
  basename="$(basename "$dependency")"
  if [[ "$dependency" == /* && -f "$dependency" ]]; then printf '%s\n' "$dependency"; return; fi
  if [[ "$dependency" == @loader_path/* ]]; then
    local candidate="$(cd "$(dirname "$owner")" && pwd)/${dependency#@loader_path/}"
    [[ -f "$candidate" ]] && { printf '%s\n' "$candidate"; return; }
  fi
  for candidate in "$gst_root/lib/$basename" "$gst_root/lib/gstreamer-1.0/$basename"; do
    [[ -f "$candidate" ]] && { printf '%s\n' "$candidate"; return; }
  done
  return 1
}

is_system_dependency() {
  [[ "$1" == /System/* || "$1" == /usr/lib/* ]]
}

queue=("$macos/librobocamhub_native.dylib" "$runtime/gst-plugin-scanner")
queue+=("$runtime"/*.dylib)
index=0
while (( index < ${#queue[@]} )); do
  owner="${queue[$index]}"; ((index+=1))
  while IFS= read -r dependency; do
    [[ -z "$dependency" ]] && continue
    [[ "$(basename "$dependency")" == "$(basename "$owner")" ]] && continue
    is_system_dependency "$dependency" && continue
    resolved="$(resolve_dependency "$dependency" "$owner")" || {
      echo "Unable to resolve dependency '$dependency' required by '$owner'." >&2; exit 1;
    }
    target="$runtime/$(basename "$resolved")"
    if [[ ! -f "$target" ]]; then
      cp "$resolved" "$target"
      thin_arm64 "$target"
      queue+=("$target")
    fi
  done < <(otool -L "$owner" | sed -nE 's/^[[:space:]]+([^[:space:]]+).*/\1/p')
done

rewrite_binary() {
  local owner="$1" replacement_prefix="$2"
  while IFS= read -r dependency; do
    [[ -z "$dependency" ]] && continue
    [[ "$(basename "$dependency")" == "$(basename "$owner")" ]] && continue
    is_system_dependency "$dependency" && continue
    install_name_tool -change "$dependency" "$replacement_prefix/$(basename "$dependency")" "$owner"
  done < <(otool -L "$owner" | sed -nE 's/^[[:space:]]+([^[:space:]]+).*/\1/p')
}

rewrite_binary "$macos/librobocamhub_native.dylib" '@loader_path/gstreamer-1.0'
for binary in "$runtime"/*; do
  file "$binary" | grep -q 'Mach-O' || continue
  rewrite_binary "$binary" '@loader_path'
  [[ "$binary" == *.dylib ]] && install_name_tool -id "@loader_path/$(basename "$binary")" "$binary"
  codesign --force --sign - "$binary"
done

cp "$repo_root/scripts/packaging/Info.plist" "$app/Contents/Info.plist"
cp "$repo_root/scripts/packaging/THIRD-PARTY-NOTICES.md" "$app/Contents/Resources/"
for license in "$gst_root/share/gstreamer-1.0/LICENSE" "$gst_root/COPYING" "$gst_root/LICENSE"; do
  if [[ -f "$license" ]]; then
    cp "$license" "$app/Contents/Resources/licenses/GStreamer-LICENSE"
    break
  fi
done
[[ -f "$app/Contents/Resources/licenses/GStreamer-LICENSE" ]] || {
  echo "The GStreamer redistribution license was not found below $gst_root." >&2; exit 1;
}

codesign --force --deep --sign - "$app"
"$repo_root/scripts/verify-package.sh" "$app"
echo "Created $app"
