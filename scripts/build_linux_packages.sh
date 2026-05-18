#!/bin/bash
# ETL-SQL Linux Package Builder
# Usage: ./build_linux_packages.sh <version>

VERSION=${1:-"0.7.0"}
ARCH="amd64"
PKG_NAME="etl-sql"
BASE_DIR="src/ETL-SQL.Installer/linux"
BUILD_ROOT="src/ETL-SQL.Installer/linux/build"

echo "--- ETL-SQL Linux Package Builder ---"

# 1. Cleanup and Create Structure
rm -rf "$BUILD_ROOT"
mkdir -p "$BUILD_ROOT/usr/bin"
mkdir -p "$BUILD_ROOT/usr/lib/etl-sql/bin"
mkdir -p "$BUILD_ROOT/usr/lib/etl-sql/orchestrator"
mkdir -p "$BUILD_ROOT/usr/lib/etl-sql/portal"
mkdir -p "$BUILD_ROOT/etc/systemd/system"
mkdir -p "$BUILD_ROOT/DEBIAN"

# 2. Publish Binaries (Simulated - assume pre-built for speed in this script)
echo "Publishing binaries (win-x64 used for demo, would be linux-x64)..."
# In a real run: dotnet publish ../src/ETL-SQL.App/ETL-SQL.App.csproj -c Release -r linux-x64 ...

# 3. Create DEBIAN/control
cat <<EOF > "$BUILD_ROOT/DEBIAN/control"
Package: $PKG_NAME
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: AmericanSuperstar <chuck@example.com>
Description: ETL-SQL Enterprise Suite
 Hybrid engine that executes SQL-like syntax against diverse data sources.
EOF

# 4. Copy Service Files
cp "$BASE_DIR/etl-sql-orchestrator.service" "$BUILD_ROOT/etc/systemd/system/"
cp "$BASE_DIR/etl-sql-portal.service" "$BUILD_ROOT/etc/systemd/system/"

# 5. Create post-install script (user creation)
cat <<EOF > "$BUILD_ROOT/DEBIAN/postinst"
#!/bin/bash
id -u etlsql &>/dev/null || useradd -r -s /usr/sbin/nologin etlsql
chown -R etlsql:etlsql /usr/lib/etl-sql
systemctl daemon-reload
EOF
chmod 755 "$BUILD_ROOT/DEBIAN/postinst"

# 6. Build .deb
if command -v dpkg-deb &> /dev/null; then
    echo "Building .deb package..."
    dpkg-deb --build "$BUILD_ROOT" "${PKG_NAME}_${VERSION}_${ARCH}.deb"
    echo "[SUCCESS] Package created: ${PKG_NAME}_${VERSION}_${ARCH}.deb"
else
    echo "[WARNING] dpkg-deb not found. Skipping .deb creation."
fi

echo -e "\nLinux package build process complete."
