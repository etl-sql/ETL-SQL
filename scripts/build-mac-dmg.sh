#!/bin/bash
# ETL-SQL macOS DMG Builder
# Usage: ./build-mac-dmg.sh <version>

set -euo pipefail
if ! command -v hdiutil >/dev/null; then
    echo '[ERROR] hdiutil is required to build the macOS DMG.' >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
INSTALLER_ROOT="$(cd "$REPO_ROOT/src/ETL-SQL.Installer" && pwd -P)"
if [[ "$INSTALLER_ROOT" != "$REPO_ROOT/src/ETL-SQL.Installer" ]]; then
    echo '[ERROR] Installer staging must stay inside the repository.' >&2
    exit 1
fi
cd "$REPO_ROOT"

VERSION=${1:-"0.22.0"}
PUBLISHED_BIN_DIR=${2:-"release/osx-x64/bin"}
APP_NAME="ETL-SQL"
RELEASE_ROOT="$REPO_ROOT/release"
if [[ -L "$RELEASE_ROOT" ]]; then
    echo '[ERROR] macOS release output must not be a symbolic link.' >&2
    exit 1
fi
BUILD_DIR="$RELEASE_ROOT/mac-dmg"
if [[ -L "$BUILD_DIR" ]]; then
    echo '[ERROR] macOS staging output must not be a symbolic link.' >&2
    exit 1
fi
APP_BUNDLE="$BUILD_DIR/$APP_NAME.app"

echo "--- ETL-SQL macOS DMG Builder ---"

source "$REPO_ROOT/scripts/lib/native-publish-input.sh"
assert_clean_native_publish "$PUBLISHED_BIN_DIR"
PUBLISHED_BIN_DIR="$(cd "$PUBLISHED_BIN_DIR" && pwd -P)"
if [[ "$PUBLISHED_BIN_DIR" == "$BUILD_DIR" || "$PUBLISHED_BIN_DIR" == "$BUILD_DIR/"* || "$BUILD_DIR" == "$PUBLISHED_BIN_DIR/"* ]]; then
    echo '[ERROR] Published input and macOS staging must not overlap.' >&2
    exit 1
fi

# 1. Create .app Structure
mkdir -p "$RELEASE_ROOT"
rm -rf -- "$BUILD_DIR"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"
mkdir -p "$APP_BUNDLE/Contents/Resources/bin"

# 2. Create Info.plist
cat <<EOF > "$APP_BUNDLE/Contents/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>ETL-SQL</string>
    <key>CFBundleIdentifier</key>
    <string>com.americansuperstar.etl-sql</string>
    <key>CFBundleName</key>
    <string>ETL-SQL</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
</dict>
</plist>
EOF

# 3. Copy Binaries and Create Launcher Script
if [ ! -d "$PUBLISHED_BIN_DIR" ]; then
    echo "[ERROR] Published binary directory not found: $PUBLISHED_BIN_DIR"
    exit 1
fi

if [ ! -f "$PUBLISHED_BIN_DIR/ETL-SQL" ]; then
    echo "[ERROR] Required CLI binary not found: $PUBLISHED_BIN_DIR/ETL-SQL"
    exit 1
fi

echo "Copying published osx-x64 binaries from $PUBLISHED_BIN_DIR..."
cp -a "$PUBLISHED_BIN_DIR/." "$APP_BUNDLE/Contents/Resources/bin/"
chmod +x "$APP_BUNDLE/Contents/Resources/bin/ETL-SQL"*

cat <<EOF > "$APP_BUNDLE/Contents/MacOS/ETL-SQL"
#!/bin/bash
SCRIPT_DIR="\$(cd "\$(dirname "\$0")" && pwd)"
CLI="\$SCRIPT_DIR/../Resources/bin/ETL-SQL"
osascript -e "tell application \"Terminal\" to do script \"\\\"\$CLI\\\" ui edit\""
EOF
chmod +x "$APP_BUNDLE/Contents/MacOS/ETL-SQL"
# COMPAT_BREAK: 0.22 — a copied application must not retain non-owner write access.
chmod -R go-w "$APP_BUNDLE"

# 4. Build DMG
echo "Creating DMG image..."
# Hosted macOS runners intermittently report "Resource busy" while another process holds the
# disk-image subsystem (Native Package Certification 41676292135, 2026-11-03). Retry only that
# error, a bounded number of times; any other failure stops the build immediately.
DMG_ATTEMPTS=3
for attempt in $(seq 1 "$DMG_ATTEMPTS"); do
    if output=$(hdiutil create -volname "$APP_NAME" -srcfolder "$BUILD_DIR" -ov -format UDZO "${APP_NAME}_v${VERSION}.dmg" 2>&1); then
        [ -n "$output" ] && printf '%s\n' "$output"
        break
    fi
    printf '%s\n' "$output" >&2
    if [[ "$output" != *"Resource busy"* || "$attempt" -eq "$DMG_ATTEMPTS" ]]; then
        echo "[ERROR] hdiutil create failed (attempt $attempt of $DMG_ATTEMPTS)." >&2
        exit 1
    fi
    echo "hdiutil reported Resource busy; retrying in ${DMG_RETRY_DELAY_SECONDS:-15}s (attempt $attempt of $DMG_ATTEMPTS)." >&2
    sleep "${DMG_RETRY_DELAY_SECONDS:-15}"
done
echo "[SUCCESS] DMG created: ${APP_NAME}_v${VERSION}.dmg"

echo -e "\nmacOS bundle process complete."
