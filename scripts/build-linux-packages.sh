#!/bin/bash
# ETL-SQL Linux Package Builder
# Usage: ./build-linux-packages.sh <version>

VERSION=${1:-"0.18.0"}
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
mkdir -p "$BUILD_ROOT/usr/share/doc/etl-sql"
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
cp "LICENSE.md" "$BUILD_ROOT/usr/share/doc/etl-sql/LICENSE.md"
cp "NOTICE.md" "$BUILD_ROOT/usr/share/doc/etl-sql/NOTICE.md"
cp "THIRD-PARTY-NOTICES.md" "$BUILD_ROOT/usr/share/doc/etl-sql/THIRD-PARTY-NOTICES.md"

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
Depends: python3
Maintainer: Charles Clemens <etlsqlsoftware@gmail.com>
Description: ETL-SQL Enterprise Suite
 Hybrid engine that executes SQL-like syntax against diverse data sources.
EOF

# 4. Copy Service Files
cp "$BASE_DIR/etl-sql-orchestrator.service" "$BUILD_ROOT/etc/systemd/system/"
cp "$BASE_DIR/etl-sql-portal.service" "$BUILD_ROOT/etc/systemd/system/"

# 5. Maintainer scripts (quoted heredocs so nothing expands at build time).

# postinst: create the service user, generate a JWT secret + a matching Orchestrator/Portal API key,
# approve the install folder as a security safe zone (so the portal starts and may write data under
# /usr/lib/etl-sql/bin), then enable and start the services.
cat <<'POSTINST' > "$BUILD_ROOT/DEBIAN/postinst"
#!/bin/bash
set -e

id -u etlsql >/dev/null 2>&1 || useradd -r -s /usr/sbin/nologin etlsql

CFG=/usr/lib/etl-sql/bin/appsettings.json
if command -v python3 >/dev/null 2>&1 && [ -f "$CFG" ]; then
    python3 - "$CFG" <<'PY'
import json, sys, os, base64
path = sys.argv[1]
with open(path) as f:
    data = json.load(f)
changed = False
jwt = data.get("Portal", {}).get("Jwt")
if isinstance(jwt, dict) and not (jwt.get("Secret") or "").strip():
    jwt["Secret"] = base64.b64encode(os.urandom(32)).decode("ascii")
    changed = True
# Portal sub-choices / module enablement via ETLSQL_PORTAL_MODULES env var (e.g. Reporting,Designer,Scheduling,Operations)
env_modules = os.environ.get("ETLSQL_PORTAL_MODULES")
if env_modules and isinstance(data.get("Portal"), dict):
    portal_cfg = data["Portal"]
    if "Modules" not in portal_cfg or not isinstance(portal_cfg["Modules"], dict):
        portal_cfg["Modules"] = {}
    mod_cfg = portal_cfg["Modules"]
    enabled_list = [m.strip().lower() for m in env_modules.split(",") if m.strip()]
    mod_cfg["Reporting"] = "reporting" in enabled_list or "reports" in enabled_list
    mod_cfg["Designer"] = "designer" in enabled_list
    mod_cfg["Scheduling"] = "scheduling" in enabled_list or "scheduler" in enabled_list
    mod_cfg["Operations"] = "operations" in enabled_list or "ops" in enabled_list
    changed = True
# Orchestrator API key: the service binds to a network address and refuses to start without a key.
# Generate one and mirror it to the Portal's client config so the two halves match out of the box.
orch = data.get("Orchestrator")
portal_orch = data.get("Portal", {}).get("Orchestrator")
api_key = None
if isinstance(orch, dict) and (orch.get("ApiKey") or "").strip():
    api_key = orch["ApiKey"]
elif isinstance(portal_orch, dict) and (portal_orch.get("ApiKey") or "").strip():
    api_key = portal_orch["ApiKey"]
if api_key is None:
    api_key = base64.b64encode(os.urandom(32)).decode("ascii")
if isinstance(orch, dict) and not (orch.get("ApiKey") or "").strip():
    orch["ApiKey"] = api_key
    changed = True
if isinstance(portal_orch, dict) and not (portal_orch.get("ApiKey") or "").strip():
    portal_orch["ApiKey"] = api_key
    changed = True
sec = data.get("Security")
if isinstance(sec, dict) and sec.get("ApprovedSafeZones") != ["/usr/lib/etl-sql/bin"]:
    sec["ApprovedSafeZones"] = ["/usr/lib/etl-sql/bin"]
    changed = True
if changed:
    with open(path, "w") as f:
        json.dump(data, f, indent=2)
PY
else
    echo "[ETL-SQL] python3 or appsettings.json missing; set Portal:Jwt:Secret, Orchestrator:ApiKey (and the matching Portal:Orchestrator:ApiKey), and Security:ApprovedSafeZones manually." >&2
    echo "[ETL-SQL] NOTE: the Orchestrator binds to a network address and will NOT start without Orchestrator:ApiKey set." >&2
fi

chown -R etlsql:etlsql /usr/lib/etl-sql
systemctl daemon-reload
systemctl enable --now etl-sql-orchestrator.service etl-sql-portal.service || true

echo ""
echo "ETL-SQL installed. Once the services start:"
echo "  Portal:    http://localhost:5002"
echo "  Orchestrator API: http://localhost:5001"
POSTINST
chmod 755 "$BUILD_ROOT/DEBIAN/postinst"

# prerm: stop and disable the services before files are removed.
cat <<'PRERM' > "$BUILD_ROOT/DEBIAN/prerm"
#!/bin/bash
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    systemctl disable --now etl-sql-portal.service etl-sql-orchestrator.service >/dev/null 2>&1 || true
fi
PRERM
chmod 755 "$BUILD_ROOT/DEBIAN/prerm"

# postrm: on purge (apt purge), delete runtime data that dpkg does not track. apt remove keeps it.
cat <<'POSTRM' > "$BUILD_ROOT/DEBIAN/postrm"
#!/bin/bash
systemctl daemon-reload >/dev/null 2>&1 || true
if [ "$1" = "purge" ]; then
    # Keep this list in sync with DataPurgeService / the MSI CleanData action until the installers
    # are unified to call `etl-sql purge --yes` (tracked under the installer-parity TODO).
    rm -rf /usr/lib/etl-sql/bin/logs /usr/lib/etl-sql/bin/Snapshots /usr/lib/etl-sql/bin/Reports /usr/lib/etl-sql/bin/data
    rm -f /usr/lib/etl-sql/bin/portal.db* /usr/lib/etl-sql/bin/etlsql.db*
fi
POSTRM
chmod 755 "$BUILD_ROOT/DEBIAN/postrm"

# 6. Build .deb
if command -v dpkg-deb &> /dev/null; then
    echo "Building .deb package..."
    dpkg-deb --build "$BUILD_ROOT" "${PKG_NAME}_${VERSION}_${ARCH}.deb"
    echo "[SUCCESS] Package created: ${PKG_NAME}_${VERSION}_${ARCH}.deb"
else
    echo "[WARNING] dpkg-deb not found. Skipping .deb creation."
fi

echo -e "\nLinux package build process complete."
