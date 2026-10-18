#!/usr/bin/env bash
# Certifies a fresh DEB install, an installed upgrade from the previous release, and rollback to it.
#
# Installs the candidate on a clean host with no operator configuration and purges it. Then installs
# the previous package as an operator would leave it, records what a browser would cache, upgrades in
# place without touching the services, and checks what is running, what it serves, and what
# configuration survived. It then reinstalls the previous package over the candidate and purges
# everything. Every check is reported; the exit code is non-zero when any failed.
#
# Mutates the machine: run as root on a disposable host or WSL distro with systemd and no
# etl-sql package installed. Node 22 must be on PATH (or set NODE) for the asset checks.
#
# Usage: sudo scripts/test-deb-upgrade.sh <previous.deb> <current.deb> <prepared-checkout> <evidence-dir>
set -uo pipefail

PREVIOUS_DEB=$(realpath "$1"); CURRENT_DEB=$(realpath "$2"); CHECKOUT=$(realpath "$3"); OUT=$4
NODE=${NODE:-node}
INSTALL=/usr/lib/etl-sql/bin
CFG=$INSTALL/appsettings.json
FIRST_RUN=/etc/etl-sql/first-run-password
URL=http://localhost:5002
CHECKER=$CHECKOUT/scripts/test-installed-portal-assets.mjs

[ "$(id -u)" -eq 0 ] || { echo "Run as root." >&2; exit 2; }
dpkg -s etl-sql >/dev/null 2>&1 && { echo "etl-sql is already installed; use a clean host." >&2; exit 2; }
# A stale secrets file makes the "fresh" install adopt old secrets and skip the one-time sign-in.
[ -e /usr/lib/etl-sql ] && { echo "/usr/lib/etl-sql exists (an earlier run's leftovers?); use a clean host." >&2; exit 2; }
[ "$(systemctl is-system-running 2>/dev/null)" != "offline" ] || { echo "systemd is required." >&2; exit 2; }
mkdir -p "$OUT"; OUT=$(realpath "$OUT"); : > "$OUT/summary.txt"
FAILED=0

log() { echo "$*" | tee -a "$OUT/summary.txt"; }
check() { local name=$1; shift; if "$@" >> "$OUT/checks.log" 2>&1; then log "PASS $name"; else log "FAIL $name"; FAILED=1; fi; }
package_version() { dpkg-deb -f "$1" Version; }
SECRETS=$INSTALL/appsettings.secrets.json
# The effective value, as the hosts read it: the installed secrets file overrides appsettings.json.
setting() { python3 - "$CFG" "$SECRETS" "$1" <<'PY'
import json, os, sys
value = ""
for path in sys.argv[1:3]:
    if not os.path.isfile(path):
        continue
    node = json.load(open(path, encoding="utf-8-sig"))
    for key in sys.argv[3].split(":"):
        node = node.get(key, {}) if isinstance(node, dict) else {}
    if isinstance(node, str) and node:
        value = node
print(value)
PY
}
# appsettings.json is readable by every local user, so it must hold none of the generated secrets.
shared_config_holds_no_secrets() {
    python3 - "$CFG" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8-sig"))
found = []
for dotted in ["Portal:Jwt:Secret", "Portal:Jwt:PreviousSecrets", "Portal:Dataset:AtRestKey", "Portal:Dataset:PreviousAtRestKeys", "Portal:FirstRun:AdminPassword", "Orchestrator:ApiKey", "Orchestrator:PreviousApiKeys", "Orchestrator:IdentitySigningSecret", "Portal:Orchestrator:ApiKey", "Portal:Orchestrator:IdentitySigningSecret"]:
    node = data
    for key in dotted.split(":"):
        node = node.get(key) if isinstance(node, dict) else None
    if node not in (None, "", [], {}):
        found.append(dotted)
print("secrets in appsettings.json:", found)
sys.exit(1 if found else 0)
PY
}
secrets_file_protected() {
    local mode; mode=$(stat -c '%a %U %G' "$SECRETS" 2>&1); echo "$SECRETS: $mode"
    [ "$mode" = "640 root etlsql" ] || return 1
    # An ordinary account can read appsettings.json but not the secrets.
    su -s /bin/sh nobody -c "cat '$CFG' > /dev/null" && ! su -s /bin/sh nobody -c "cat '$SECRETS'" > /dev/null 2>&1
}

wait_portal() {
    for _ in $(seq 1 90); do
        [ "$(curl -s -o "$OUT/$1-health.json" -w '%{http_code}' "$URL/health")" = 200 ] && return 0
        sleep 2
    done
    systemctl status etl-sql-portal.service --no-pager > "$OUT/$1-portal-status.txt" 2>&1
    journalctl -u etl-sql-portal.service --no-pager -n 200 > "$OUT/$1-portal-journal.txt" 2>&1
    return 1
}

# A running service whose executable was replaced underneath it reports the old inode as deleted.
service_runs_installed_binary() {
    local pid; pid=$(systemctl show -p MainPID --value "$1")
    if [ -z "$pid" ] || [ "$pid" = 0 ]; then
        echo "$1 has no main process"
        journalctl -u "$1" --no-pager -n 80 > "$OUT/${1%.service}-$(date +%s)-journal.txt" 2>&1
        return 1
    fi
    local exe; exe=$(readlink "/proc/$pid/exe"); echo "$1 pid $pid exe $exe"
    [[ "$exe" != *"(deleted)"* ]]
}

cli_reports() { local out; out=$(etl-sql --version 2>&1); echo "$out"; grep -q -- "${1%%[-~+]*}" <<<"$out"; }
mask() { [ -n "$1" ] && echo "${1:0:4}…(${#1})" || echo "<empty>"; }
setting_is() { local value; value=$(setting "$1"); echo "$1: was $(mask "$2") now $(mask "$value")"; [ -n "$value" ] && [ "$value" = "$2" ]; }
setting_present() { local value; value=$(setting "$1"); echo "$1: $(mask "$value")"; [ -n "$value" ]; }
root_only_file() { local mode; mode=$(stat -c '%a %U' "$1" 2>&1); echo "$1: $mode"; [ "$mode" = "600 root" ] && [ -s "$1" ]; }
# postinst writes generated secrets into appsettings.json, so dpkg reports it as modified by design.
# Packages up to v0.20.1 also shipped Portal snapshot files that the running Portal later prunes.
dpkg_verify_clean() {
    local problems; problems=$(dpkg --verify etl-sql 2>&1 | grep -v -e ' /usr/lib/etl-sql/bin/appsettings.json$' -e ' /usr/lib/etl-sql/bin/Snapshots/')
    echo "$problems"; [ -z "$problems" ]
}

# Releases up to v0.20.0 ship units that cannot start (the service user has no home directory for
# its state or for single-file extraction). Apply the drop-in an operator needs for such a package,
# so the upgrade starts from a working install; it is removed again before the candidate is installed.
DROPIN_DIRS="/etc/systemd/system/etl-sql-portal.service.d /etc/systemd/system/etl-sql-orchestrator.service.d"
operator_workaround_if_needed() {
    grep -q DOTNET_BUNDLE_EXTRACT_BASE_DIR /etc/systemd/system/etl-sql-portal.service && return 0
    log "NOTE $1: package units lack an extraction directory; applying the operator drop-in"
    for d in $DROPIN_DIRS; do
        mkdir -p "$d"
        printf '[Service]\nStateDirectory=etl-sql\nCacheDirectory=etl-sql\nEnvironment=HOME=/var/lib/etl-sql\nEnvironment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/cache/etl-sql\nExecStartPre=/bin/mkdir -p /var/lib/etl-sql/.local/share\n' > "$d/extract.conf"
    done
    systemctl daemon-reload
    systemctl restart etl-sql-orchestrator.service etl-sql-portal.service
}
remove_operator_workaround() { rm -f /etc/systemd/system/etl-sql-*.service.d/extract.conf; systemctl daemon-reload; }

# A v0.20.0 operator follows the documented first-start steps (docs/administration/portal/
# deployment.md and portal-configuration.md) in the installed configuration file: a bootstrap
# administrator password and the dataset at-rest key. The upgrade must carry both forward, and the
# setting of the operator's own.
configure_previous() {
    python3 - "$CFG" <<'CONFIG'
import base64, json, os, sys
path = sys.argv[1]
data = json.load(open(path, encoding="utf-8-sig"))
data["Portal"]["FirstRun"]["AdminPassword"] = "Upgrade-" + base64.urlsafe_b64encode(os.urandom(9)).decode() + "9!"
data["Portal"]["Dataset"]["AtRestKey"] = base64.b64encode(os.urandom(32)).decode()
data["UpgradeSentinel"] = {"Value": "kept"}
json.dump(data, open(path, "w", encoding="utf-8"), indent=2)
CONFIG
    systemctl restart etl-sql-portal.service
}
# `admin backup` runs as root, not the service user, so it must be told where the service's job
# history is. Before v0.21.0 it silently backed up no Orchestrator database.
backup_has_job_history() {
    local db=/var/lib/etl-sql/.local/share/etlsql-data/etlsql.db dir="$OUT/fresh-backup"
    setting_is Portal:Orchestrator:DatabasePath "$db" || return 1
    [ -f "$db" ] || { echo "The Orchestrator wrote no $db."; return 1; }
    systemctl stop etl-sql-portal.service etl-sql-orchestrator.service
    mkdir -p "$dir"
    (cd /tmp && etl-sql admin backup --output-dir "$dir") || return 1
    python3 - "$dir" <<'PY'
import glob, sys, zipfile
names = set(zipfile.ZipFile(glob.glob(sys.argv[1] + "/etl-sql-backup-*.zip")[0]).namelist())
print(sorted(names))
sys.exit(0 if {"db/portal.db", "db/etlsql.db"} <= names else 1)
PY
}
assets() { "$NODE" "$CHECKER" --url "$URL" --install "$INSTALL" --dump "$OUT/served-mismatch" "$@"; }

PREV_V=$(package_version "$PREVIOUS_DEB"); CUR_V=$(package_version "$CURRENT_DEB")
log "previous $PREV_V $(sha256sum "$PREVIOUS_DEB" | cut -c1-64)"
log "current  $CUR_V $(sha256sum "$CURRENT_DEB" | cut -c1-64)"
log "checkout $(git -C "$CHECKOUT" rev-parse HEAD 2>/dev/null || echo unknown)"

# 0. A fresh install of the candidate starts with no operator configuration at all.
log "== fresh install of $CUR_V"
check "fresh: dpkg install" dpkg -i "$CURRENT_DEB"
check "fresh: Portal healthy" wait_portal fresh
check "fresh: Portal runs the installed executable" service_runs_installed_binary etl-sql-portal.service
check "fresh: Orchestrator runs the installed executable" service_runs_installed_binary etl-sql-orchestrator.service
check "fresh: one-time sign-in written for root only" root_only_file "$FIRST_RUN"
check "fresh: dataset at-rest key generated" setting_present Portal:Dataset:AtRestKey
check "fresh: secrets readable by the service account only" secrets_file_protected
check "fresh: shared appsettings.json holds no generated secret" shared_config_holds_no_secrets
check "fresh: served assets match the candidate" assets --inventory-root "$CHECKOUT" --out "$OUT/fresh-assets.json"
check "fresh: admin backup includes the job history the service writes" backup_has_job_history
check "fresh: dpkg purge" dpkg --purge etl-sql
check "fresh: purge removed state, cache and sign-in file" \
    bash -c '[ ! -e /var/lib/etl-sql ] && [ ! -e /var/cache/etl-sql ] && [ ! -e /etc/etl-sql ]'
userdel etlsql 2>/dev/null || true
rm -rf /usr/lib/etl-sql

# 1. Previous release, as an operator would leave it.
log "== install previous $PREV_V"
check "previous: dpkg install" dpkg -i "$PREVIOUS_DEB"
operator_workaround_if_needed previous
configure_previous
check "previous: Portal healthy" wait_portal previous
SECRET=$(setting Portal:Jwt:Secret); API_KEY=$(setting Orchestrator:ApiKey); AT_REST=$(setting Portal:Dataset:AtRestKey)
echo kept > "$INSTALL/data-upgrade-sentinel.txt"
check "previous: served assets recorded as a browser cache" assets --record "$OUT/cache-previous.json" --out "$OUT/previous-assets.json"
# The pre-upgrade backup a rollback needs: v0.21.0 renames the database schema on first start, and a
# previous release cannot open the renamed tables. SQLite files are copied with the services stopped.
# The Portal's database sits in the install folder; the Orchestrator's in the service user's home.
BACKUP=$OUT/pre-upgrade-backup
databases() { find "$INSTALL" /var/lib/etl-sql -name '*.db' -o -name '*.db-wal' -o -name '*.db-shm' 2>/dev/null; }
systemctl stop etl-sql-portal.service etl-sql-orchestrator.service
rm -rf "$BACKUP"; mkdir -p "$BACKUP"
databases > "$BACKUP/databases.txt"
tar -cpPf "$BACKUP/databases.tar" -T "$BACKUP/databases.txt"
cp -p "$CFG" "$BACKUP/appsettings.json"
log "NOTE previous: backed up $(wc -l < "$BACKUP/databases.txt") database file(s) and the configuration"
systemctl start etl-sql-orchestrator.service etl-sql-portal.service

# 2. Upgrade in place. The services are left exactly as the package scripts leave them.
log "== upgrade to $CUR_V"
remove_operator_workaround
check "upgrade: dpkg install" dpkg -i "$CURRENT_DEB"
check "upgrade: Portal healthy" wait_portal upgrade
check "upgrade: Portal runs the upgraded executable" service_runs_installed_binary etl-sql-portal.service
check "upgrade: Orchestrator runs the upgraded executable" service_runs_installed_binary etl-sql-orchestrator.service
check "upgrade: CLI reports $CUR_V" cli_reports "$CUR_V"
check "upgrade: installed files match the package" dpkg_verify_clean
check "upgrade: Portal JWT secret preserved" setting_is Portal:Jwt:Secret "$SECRET"
check "upgrade: Orchestrator API key preserved" setting_is Orchestrator:ApiKey "$API_KEY"
check "upgrade: dataset at-rest key preserved" setting_is Portal:Dataset:AtRestKey "$AT_REST"
check "upgrade: operator setting preserved" setting_is UpgradeSentinel:Value kept
check "upgrade: secrets moved to the service-only file" secrets_file_protected
check "upgrade: shared appsettings.json holds no generated secret" shared_config_holds_no_secrets
check "upgrade: no one-time sign-in for an existing Portal" test ! -e "$FIRST_RUN"
check "upgrade: runtime data preserved" grep -qx kept "$INSTALL/data-upgrade-sentinel.txt"
check "upgrade: served assets match the candidate; cached browser refetches changes" \
    assets --inventory-root "$CHECKOUT" --revalidate "$OUT/cache-previous.json" --record "$OUT/cache-current.json" --out "$OUT/upgrade-assets.json"

# 3. Roll back: install the previous package over the candidate, then restore the pre-upgrade backup
#    of the databases and configuration with the services stopped.
log "== roll back to $PREV_V"
check "rollback: dpkg install" dpkg -i "$PREVIOUS_DEB"
operator_workaround_if_needed rollback
setting_is Portal:Jwt:Secret "$SECRET" > /dev/null \
    || log "NOTE rollback: $PREV_V package scripts replaced the configuration"
systemctl stop etl-sql-portal.service etl-sql-orchestrator.service
databases | xargs -r rm -f
tar -xpPf "$BACKUP/databases.tar"; cp -p "$BACKUP/appsettings.json" "$CFG"; chown etlsql:etlsql "$CFG"
systemctl start etl-sql-orchestrator.service etl-sql-portal.service
check "rollback: Portal healthy on the restored database" wait_portal rollback
check "rollback: Portal runs the reinstalled executable" service_runs_installed_binary etl-sql-portal.service
check "rollback: CLI reports $PREV_V" cli_reports "$PREV_V"
check "rollback: installed files match the previous package" dpkg_verify_clean
check "rollback: served assets are the previous release's; cached browser refetches changes" \
    assets --revalidate "$OUT/cache-current.json" --out "$OUT/rollback-assets.json"

# 4. Purge leaves nothing behind.
log "== purge"
check "purge: dpkg purge" dpkg --purge etl-sql
rm -rf $DROPIN_DIRS; systemctl daemon-reload; rm -rf /var/cache/etl-sql /var/lib/etl-sql /etc/etl-sql
# The purge runs the previous package's scripts, which predate these v0.21.0 files: record them,
# then remove them so the next run starts clean.
find "$INSTALL" -maxdepth 2 \( -name appsettings.secrets.json -o -name .portal-keys \) > "$OUT/left-by-previous-purge.txt" 2>/dev/null
rm -rf "$INSTALL/appsettings.secrets.json" "$INSTALL/.portal-keys"
rm -f "$INSTALL/data-upgrade-sentinel.txt"; rmdir --ignore-fail-on-non-empty "$INSTALL" /usr/lib/etl-sql 2>/dev/null
check "purge: services removed" bash -c '! systemctl is-active --quiet etl-sql-portal.service && ! systemctl is-active --quiet etl-sql-orchestrator.service'
journalctl -u etl-sql-portal.service --no-pager --since "-40 min" > "$OUT/portal-journal.txt" 2>&1
userdel etlsql 2>/dev/null || true
find /usr/lib/etl-sql -maxdepth 3 2>/dev/null > "$OUT/left-after-purge.txt"

[ $FAILED -eq 0 ] && log "RESULT PASS" || log "RESULT FAIL"
exit $FAILED
