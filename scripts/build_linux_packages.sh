#!/bin/bash
# ETL-SQL Linux Package Builder
# Usage: ./build_linux_packages.sh <version>

VERSION=${1:-"0.9.0"}
PUBLISHED_BIN_DIR=${2:-"release/linux-x64/bin"}
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

# 2. Copy Published Binaries
if [ ! -d "$PUBLISHED_BIN_DIR" ]; then
    echo "[ERROR] Published binary directory not found: $PUBLISHED_BIN_DIR"
    exit 1
fi

if [ ! -f "$PUBLISHED_BIN_DIR/ETL-SQL" ]; then
    echo "[ERROR] Required CLI binary not found: $PUBLISHED_BIN_DIR/ETL-SQL"
    exit 1
fi

echo "Copying published linux-x64 binaries from $PUBLISHED_BIN_DIR..."
# The single self-contained publish already contains every host executable
# (CLI, TUI, LSP, Report, Player, Portal, Service). Install it once instead of
# triplicating ~1.4 GB of identical runtime — three copies pushed the .deb past
# GitHub's 2 GiB asset limit. The orchestrator and portal services launch their
# hosts directly from bin/ (see the *.service ExecStart paths) while keeping
# their own empty working directories below for runtime-writable state.
cp -a "$PUBLISHED_BIN_DIR/." "$BUILD_ROOT/usr/lib/etl-sql/bin/"

ln -s /usr/lib/etl-sql/bin/ETL-SQL "$BUILD_ROOT/usr/bin/etl-sql"
ln -s /usr/lib/etl-sql/bin/ETL-SQL-Report "$BUILD_ROOT/usr/bin/etl-sql-report"
ln -s /usr/lib/etl-sql/bin/ETL-SQL-LSP "$BUILD_ROOT/usr/bin/etl-sql-lsp"
ln -s /usr/lib/etl-sql/bin/ETL-SQL-TUI "$BUILD_ROOT/usr/bin/etl-sql-tui"

# 3. Create DEBIAN/control
cat <<EOF > "$BUILD_ROOT/DEBIAN/control"
Package: $PKG_NAME
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: Charles Clemens <etlsqlsoftware@gmail.com>
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
