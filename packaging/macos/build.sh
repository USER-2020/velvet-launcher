#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "$0")/../.." && pwd)"
publish_dir="$root_dir/packaging/macos/publish"
app_dir="$root_dir/packaging/macos/artifacts/VELVET Launcher.app"
artifact_dir="$root_dir/packaging/macos/artifacts"

rm -rf "$publish_dir" "$app_dir"
mkdir -p "$publish_dir" "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources" "$artifact_dir"

dotnet publish "$root_dir/src/Velvet.Launcher.App/Velvet.Launcher.App.csproj" \
  --configuration Release --runtime osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -o "$publish_dir"

cp -R "$publish_dir"/. "$app_dir/Contents/MacOS/"
chmod +x "$app_dir/Contents/MacOS/Velvet.Launcher.App"
cp "$root_dir/packaging/macos/Info.plist" "$app_dir/Contents/Info.plist"

hdiutil create -volname "VELVET Launcher" -srcfolder "$app_dir" \
  -ov -format UDZO "$artifact_dir/VELVET-Launcher-macos-arm64.dmg"
