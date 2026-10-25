#!/bin/bash
# ETL-SQL Linux Package Builder
# Usage: ./build-linux-packages.sh <version>

set -euo pipefail
if ! command -v dpkg-deb >/dev/null; then
    echo '[ERROR] dpkg-deb is required to build the Linux package.' >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
INSTALLER_ROOT="$(cd "$REPO_ROOT/src/ETL-SQL.Installer" && pwd -P)"
if [[ "$INSTALLER_ROOT" != "$REPO_ROOT/src/ETL-SQL.Installer" ]]; then
    echo '[ERROR] Installer staging must stay inside the repository.' >&2
    exit 1
fi
cd "$REPO_ROOT"

VERSION=${1:-"0.21.0"}
PUBLISHED_BIN_DIR=${2:-"release/linux-x64/bin"}
ARCH="amd64"
PKG_NAME="etl-sql"
BASE_DIR="$(cd "$INSTALLER_ROOT/linux" && pwd -P)"
if [[ "$BASE_DIR" != "$INSTALLER_ROOT/linux" ]]; then
    echo '[ERROR] Linux installer staging must stay inside the repository.' >&2
    exit 1
fi
BUILD_ROOT="$BASE_DIR/build"
if [[ -L "$BUILD_ROOT" ]]; then
    echo '[ERROR] Linux staging output must not be a symbolic link.' >&2
    exit 1
fi

echo "--- ETL-SQL Linux Package Builder ---"

source "$REPO_ROOT/scripts/lib/native-publish-input.sh"
assert_clean_native_publish "$PUBLISHED_BIN_DIR"

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
# COMPAT_BREAK: 0.22 — published program files must not retain group/other write access.
# Preserve executable/read-only bits while removing write access from non-owner accounts.
chmod -R go-w "$BUILD_ROOT/usr/lib/etl-sql/bin"
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

id -u etlsql >/dev/null 2>&1 || useradd -r -d /var/lib/etl-sql -s /usr/sbin/nologin etlsql

CFG=/usr/lib/etl-sql/bin/appsettings.json
SAVED=/var/lib/etl-sql-upgrade/appsettings.json
FIRST_RUN=/etc/etl-sql/first-run-password
if command -v python3 >/dev/null 2>&1 && [ -f "$CFG" ]; then
    python3 - "$CFG" "$SAVED" "$FIRST_RUN" "${2:-}" <<'PY'
import json, sys, os, base64
import stat
path, saved, first_run_file, previous_version = sys.argv[1:5]
# Written as root inside a folder the service user owns: never follow a link planted in its place.
if not stat.S_ISREG(os.lstat(path).st_mode):
    sys.exit(f"[ETL-SQL] {path} is not a regular file; refusing to rewrite it.")
with open(path, encoding="utf-8-sig") as f:
    data = json.load(f)
if not isinstance(data, dict):
    sys.exit("[ETL-SQL] Installer settings must be a JSON object.")
changed = False
# An upgrade replaced this file with the package's defaults; preinst kept the installed one. Its
# values win (the operator's settings and the generated secrets), and keys new in this release are
# added. Losing the secrets would sign everyone out and make cached datasets unreadable.
def root_owned_file(p):
    try:
        info = os.lstat(p)
    except FileNotFoundError:
        return False
    return stat.S_ISREG(info.st_mode) and info.st_uid == 0
if root_owned_file(saved):
    with open(saved, encoding="utf-8-sig") as f:
        installed = json.load(f)
    if not isinstance(installed, dict):
        sys.exit("[ETL-SQL] Saved installer settings must be a JSON object.")
    def keep(target, source):
        for key, value in source.items():
            if isinstance(value, dict) and isinstance(target.get(key), dict):
                keep(target[key], value)
            else:
                target[key] = value
    keep(data, installed)
    changed = True
# Generated service secrets live in a file only root and the service account can read, because
# appsettings.json is shared with the CLI and readable by every local user. Its values are the ones
# in use, so they win over anything in appsettings.json and nothing below regenerates them.
# Keep in sync with configure-portal-jwt.ps1 (InstallerConfigurationScriptTests compares them).
SECRET_PATHS = ["Portal:Jwt:Secret", "Portal:Jwt:PreviousSecrets", "Portal:Dataset:AtRestKey", "Portal:Dataset:PreviousAtRestKeys", "Portal:FirstRun:AdminPassword", "Orchestrator:ApiKey", "Orchestrator:PreviousApiKeys", "Orchestrator:IdentitySigningSecret", "Portal:Orchestrator:ApiKey", "Portal:Orchestrator:IdentitySigningSecret"]
secrets_path = os.path.join(os.path.dirname(path), "appsettings.secrets.json")
def read_secrets(p):
    try:
        info = os.lstat(p)
    except FileNotFoundError:
        return {}
    if not stat.S_ISREG(info.st_mode):
        sys.exit(f"[ETL-SQL] {p} is not a regular file; refusing to read it.")
    fd = os.open(p, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, encoding="utf-8-sig") as f:
        loaded = json.load(f)
    # COMPAT_BREAK: 0.22 — invalid document roots must not regenerate installed keys.
    if not isinstance(loaded, dict):
        sys.exit("[ETL-SQL] Protected installer settings must be a JSON object.")
    return loaded
def lookup(root, dotted):
    node = root
    for part in dotted.split(":")[:-1]:
        node = node.get(part) if isinstance(node, dict) else None
    return node, dotted.split(":")[-1]
def empty(value):
    return value is None or value == "" or value == [] or value == {}
installed_secrets = read_secrets(secrets_path)
for dotted in SECRET_PATHS:
    source, key = lookup(installed_secrets, dotted)
    target, _ = lookup(data, dotted)
    if isinstance(source, dict) and isinstance(target, dict) and not empty(source.get(key)):
        target[key] = source[key]
portal = data.get("Portal") if isinstance(data.get("Portal"), dict) else {}
jwt = portal.get("Jwt")
if isinstance(jwt, dict) and not (jwt.get("Secret") or "").strip():
    jwt["Secret"] = base64.b64encode(os.urandom(32)).decode("ascii")
    changed = True
# The Portal refuses to start without a dataset at-rest key. It must survive upgrades (see above):
# a new key makes every cached dataset unreadable.
dataset = portal.get("Dataset")
if isinstance(dataset, dict) and not (dataset.get("AtRestKey") or "").strip():
    dataset["AtRestKey"] = base64.b64encode(os.urandom(32)).decode("ascii")
    changed = True
# A fresh install has no administrator, and the Portal will not create one without a bootstrap
# password. Generate a one-time password; the Portal forces a change at first sign-in.
first_run = portal.get("FirstRun")
sign_in = None
if (not previous_version and isinstance(first_run, dict)
        and not os.path.isfile(first_run_file)):
    # A retry can have saved its password before publishing the root-only sign-in file.
    password = first_run.get("AdminPassword") or ""
    if not password.strip():
        password = base64.urlsafe_b64encode(os.urandom(18)).decode("ascii") + "9!"
    first_run["AdminPassword"] = password
    sign_in = f"{first_run.get('AdminUsername') or 'admin'}\n{password}\n"
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
# Federated identity: the Portal signs caller assertions that the Orchestrator verifies with the
# same secret. The Orchestrator refuses to start without it.
if isinstance(orch, dict) and isinstance(portal_orch, dict):
    signing = (orch.get("IdentitySigningSecret") or portal_orch.get("IdentitySigningSecret") or "").strip() \
        or base64.b64encode(os.urandom(32)).decode("ascii")
    for side in (orch, portal_orch):
        if not (side.get("IdentitySigningSecret") or "").strip():
            side["IdentitySigningSecret"] = signing
            changed = True
# Record the job-history file the Orchestrator service writes: its default, LocalApplicationData
# under the units' HOME. `admin backup` and `migrate-database` run as root from the install folder
# and would otherwise look beside the binaries. An operator's path is kept.
if isinstance(portal_orch, dict) and not (portal_orch.get("DatabasePath") or "").strip():
    portal_orch["DatabasePath"] = "/var/lib/etl-sql/.local/share/etlsql-data/etlsql.db"
    changed = True
sec = data.get("Security")
# The install folder must be a safe zone; zones the operator added stay.
zones = sec.get("ApprovedSafeZones") if isinstance(sec, dict) else None
if isinstance(sec, dict) and "/usr/lib/etl-sql/bin" not in (zones if isinstance(zones, list) else []):
    sec["ApprovedSafeZones"] = (zones if isinstance(zones, list) else []) + ["/usr/lib/etl-sql/bin"]
    changed = True
# Move every generated secret out of the shared file, leaving its empty default behind.
# Preserve operator-owned protected settings without copying them into the shared document.
secrets = installed_secrets
for dotted in SECRET_PATHS:
    node, key = lookup(data, dotted)
    if not isinstance(node, dict) or key not in node:
        continue
    value = node[key]
    if not empty(value):
        target = secrets
        for part in dotted.split(":")[:-1]:
            target = target.setdefault(part, {})
        target[key] = value
    node[key] = [] if isinstance(value, list) else {} if isinstance(value, dict) else ""
def write_json(target, document, mode, gid):
    # The folder belongs to the service user: create a fresh file that follows no link, set its
    # owner and mode before any content, then rename it into place.
    temp = f"{target}.{os.getpid()}.tmp"
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    os.fchown(fd, 0, gid)
    os.fchmod(fd, mode)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(document, f, indent=2)
    os.replace(temp, target)
import grp
write_json(secrets_path, secrets, 0o640, grp.getgrnam("etlsql").gr_gid)
write_json(path, data, 0o644, 0)
# COMPAT_BREAK: 0.22 — failed settings writes must not publish unusable sign-in credentials.
if sign_in is not None:
    os.makedirs(os.path.dirname(first_run_file), exist_ok=True)
    fd = os.open(first_run_file, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(sign_in)
PY
    rm -rf /var/lib/etl-sql-upgrade
else
    echo "[ETL-SQL] python3 or appsettings.json missing; set Portal:Jwt:Secret, Portal:Dataset:AtRestKey, Portal:FirstRun:AdminPassword, Orchestrator:ApiKey (and the matching Portal:Orchestrator:ApiKey), and Security:ApprovedSafeZones manually." >&2
    echo "[ETL-SQL] NOTE: the Orchestrator binds to a network address and will NOT start without Orchestrator:ApiKey set." >&2
fi

# Shipped binaries and configuration belong to root: a service account that can rewrite them can
# change its own code or security settings. The bin folder is group-writable with the sticky bit,
# so the services still create their databases, logs and data there, but cannot delete or rename a
# file root owns. Anything the services created before this release stays theirs.
BIN=/usr/lib/etl-sql/bin
dpkg-query -L etl-sql 2>/dev/null | while IFS= read -r shipped; do
    case "$shipped" in
        /usr/lib/etl-sql|/usr/lib/etl-sql/*)
            if [ -e "$shipped" ] && [ ! -L "$shipped" ]; then chown root:root "$shipped"; fi ;;
    esac
done
chown root:root /usr/lib/etl-sql
chmod 755 /usr/lib/etl-sql
chown root:etlsql "$BIN"
chmod 1775 "$BIN"
if [ -f "$CFG" ] && [ ! -L "$CFG" ]; then
    chown root:root "$CFG"
    chmod 644 "$CFG"
fi
# Readable by the services, not writable by them, and closed to every other account.
SECRETS=/usr/lib/etl-sql/bin/appsettings.secrets.json
if [ -f "$SECRETS" ] && [ ! -L "$SECRETS" ]; then
    chown root:etlsql "$SECRETS"
    chmod 640 "$SECRETS"
fi
systemctl daemon-reload
systemctl enable etl-sql-orchestrator.service etl-sql-portal.service || true
# restart, not start: on an upgrade the running services still execute the replaced binaries.
systemctl restart etl-sql-orchestrator.service etl-sql-portal.service || true

echo ""
echo "ETL-SQL installed. Once the services start:"
echo "  Portal:    http://localhost:5002"
echo "  Orchestrator API: http://localhost:5001"
if [ -z "${2:-}" ] && [ -f "$FIRST_RUN" ]; then
    echo "  First sign-in: the user name and one-time password are in $FIRST_RUN (root only)."
    echo "  The Portal requires a new password at that sign-in; then delete the file."
fi
POSTINST
chmod 755 "$BUILD_ROOT/DEBIAN/postinst"

# preinst: an upgrade unpacks the package's appsettings.json over the installed one. Keep the
# installed file so postinst can carry its values forward.
cat <<'PREINST' > "$BUILD_ROOT/DEBIAN/preinst"
#!/bin/bash
set -e
# Root-only directory: the service user owns /var/lib/etl-sql and could plant a link there.
SAVED_DIR=/var/lib/etl-sql-upgrade
if [ "$1" = "upgrade" ] && [ -f /usr/lib/etl-sql/bin/appsettings.json ]; then
    rm -rf "$SAVED_DIR"
    install -d -m 700 -o root -g root "$SAVED_DIR"
    install -m 600 -o root -g root /usr/lib/etl-sql/bin/appsettings.json "$SAVED_DIR/appsettings.json"
fi
PREINST
chmod 755 "$BUILD_ROOT/DEBIAN/preinst"

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
    rm -f /usr/lib/etl-sql/bin/portal.db* /usr/lib/etl-sql/bin/etlsql.db* /usr/lib/etl-sql/bin/appsettings.secrets.json
    # The service user's state and extraction directories, and the one-time sign-in file.
    rm -rf /var/lib/etl-sql /var/lib/etl-sql-upgrade /var/cache/etl-sql
    rm -f /etc/etl-sql/first-run-password
    rmdir /etc/etl-sql 2>/dev/null || true
fi
POSTRM
chmod 755 "$BUILD_ROOT/DEBIAN/postrm"

# 6. Build .deb
echo "Building .deb package..."
dpkg-deb --root-owner-group --build "$BUILD_ROOT" "${PKG_NAME}_${VERSION}_${ARCH}.deb"
echo "[SUCCESS] Package created: ${PKG_NAME}_${VERSION}_${ARCH}.deb"

echo -e "\nLinux package build process complete."
