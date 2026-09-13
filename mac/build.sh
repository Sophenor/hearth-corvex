#!/bin/bash
set -euo pipefail
BASE="$(cd "$(dirname "$0")" && pwd)"
OUT="$BASE/build"
APP="$OUT/Hearth.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OUT/reports" "$OUT/Hearth.iconset"
SDK="$(xcrun --sdk macosx --show-sdk-path)"
for ARCH in arm64 x86_64; do
  xcrun swiftc -swift-version 5 -parse-as-library -O -sdk "$SDK" \
    -target "$ARCH-apple-macosx13.0" -framework AppKit -framework WebKit -framework Security \
    "$BASE"/Sources/*.swift -o "$OUT/Hearth-$ARCH"
done
lipo -create "$OUT/Hearth-arm64" "$OUT/Hearth-x86_64" -output "$APP/Contents/MacOS/Hearth"
cp "$BASE/Info.plist" "$APP/Contents/Info.plist"
ditto "$BASE/Resources" "$APP/Contents/Resources"
python3 "$BASE/prepare_resources.py" "$APP/Contents/Resources"
swift "$BASE/make_icon.swift" "$OUT/Hearth.iconset"
iconutil -c icns "$OUT/Hearth.iconset" -o "$APP/Contents/Resources/Hearth.icns"
codesign --force --sign - --options runtime --identifier com.sophenor.hearth "$APP"
codesign --verify --deep --strict "$APP"
lipo -archs "$APP/Contents/MacOS/Hearth"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUT/Hearth-Mac.zip"
shasum -a 256 "$OUT/Hearth-Mac.zip" | sed 's|  .*/|  |' > "$OUT/SHA256SUMS-Mac.txt"
echo "Built universal Hearth.app for macOS 13 or later. This is ad-hoc signed, not Developer-ID signed or notarized."
