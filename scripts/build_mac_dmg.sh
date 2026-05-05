#!/bin/bash
# ETL-SQL macOS DMG Builder
# Usage: ./build_mac_dmg.sh <version>

VERSION=${1:-"0.6.0"}
APP_NAME="ETL-SQL"
BUILD_DIR="src/ETL-SQL.Installer/mac"
APP_BUNDLE="$BUILD_DIR/$APP_NAME.app"

echo "--- ETL-SQL macOS DMG Builder ---"

# 1. Create .app Structure
rm -rf "$BUILD_DIR"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

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

# 3. Create Launcher Script
cat <<EOF > "$APP_BUNDLE/Contents/MacOS/ETL-SQL"
#!/bin/bash
# Simple launcher that opens the TUI in a new terminal window
osascript -e 'tell application "Terminal" to do script "/usr/local/bin/ETL-SQL ui edit"'
EOF
chmod +x "$APP_BUNDLE/Contents/MacOS/ETL-SQL"

# 4. Build DMG
if command -v hdiutil &> /dev/null; then
    echo "Creating DMG image..."
    hdiutil create -volname "$APP_NAME" -srcfolder "$BUILD_DIR" -ov -format UDZO "${APP_NAME}_v${VERSION}.dmg"
    echo "[SUCCESS] DMG created: ${APP_NAME}_v${VERSION}.dmg"
else
    echo "[WARNING] hdiutil not found. Skipping DMG creation."
fi

echo -e "\nmacOS bundle process complete."
