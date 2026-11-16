#!/usr/bin/env bash
# Requires root and an etlsql account on a disposable Linux host; never installs a package.
# A container also needs SYS_PTRACE to read the other UID's /proc file descriptors.
set -euo pipefail
SOURCE_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
. "$SOURCE_ROOT/scripts/lib/installed-linux-checks.sh"
[ "$(id -u)" = 0 ]
id etlsql >/dev/null
FIXTURE=$(mktemp -d /tmp/etlsql-installed-checks.XXXXXXXX)
worker_pid=
worker_launcher=
fixture_job_store=
cleanup() {
    if [ -n "$worker_pid" ] && [ "$(stat -c '%u' "/proc/$worker_pid" 2>/dev/null || true)" = "$(id -u etlsql)" ]; then
        kill "$worker_pid" 2>/dev/null || true
    fi
    if [ -n "$worker_launcher" ]; then wait "$worker_launcher" 2>/dev/null || true; fi
    if [ "$fixture_job_store" = /var/lib/etl-sql/.local/share/etlsql-data/etlsql.db ]; then
        rm -f -- "$fixture_job_store"
        rmdir --ignore-fail-on-non-empty -- /var/lib/etl-sql/.local/share/etlsql-data \
            /var/lib/etl-sql/.local/share /var/lib/etl-sql/.local /var/lib/etl-sql 2>/dev/null || true
    fi
    [[ "$FIXTURE" == /tmp/etlsql-installed-checks.* && ! -L "$FIXTURE" ]] || return 1
    rm -rf -- "$FIXTURE"
}
trap cleanup EXIT
INSTALL=$FIXTURE/bin
chmod 755 "$FIXTURE"
mkdir "$INSTALL"
chown root:etlsql "$INSTALL"
chmod 1775 "$INSTALL"
printf 'synthetic executable\n' > "$INSTALL/program"
chmod 755 "$INSTALL/program"
dpkg-query() { printf '%s\n' "$INSTALL" "$INSTALL/program"; }
reject() {
    if "$@"; then
        echo "Incorrectly accepted: $*" >&2
        exit 1
    fi
}
installed_package_permissions
chmod 775 "$INSTALL"
reject installed_package_permissions
chmod 1775 "$INSTALL"
chown etlsql "$INSTALL/program"
reject installed_package_permissions
chown root "$INSTALL/program"
chmod 775 "$INSTALL/program"
reject installed_package_permissions
chmod 755 "$INSTALL/program"
rm "$INSTALL/program"
reject installed_package_permissions
dpkg-query() { printf '%s\n' "$INSTALL"; }
reject installed_package_permissions
printf 'synthetic database\n' > "$INSTALL/runtime.db"
chown etlsql "$INSTALL/runtime.db"
service_owned_nonempty_file "$INSTALL/runtime.db"
chown root "$INSTALL/runtime.db"
reject service_owned_nonempty_file "$INSTALL/runtime.db"
chown etlsql "$INSTALL/runtime.db"
: > "$INSTALL/runtime.db"
reject service_owned_nonempty_file "$INSTALL/runtime.db"
reject service_owned_nonempty_file "$INSTALL/missing.db"
printf 'synthetic database\n' > "$INSTALL/runtime.db"
ln -s runtime.db "$INSTALL/linked.db"
reject service_owned_nonempty_file "$INSTALL/linked.db"
service_account_data_write
[ "$(stat -c '%U' "$INSTALL/data")" = etlsql ]
[ -z "$(find "$INSTALL/data" -type f -print -quit)" ]
chmod 555 "$INSTALL/data"
reject service_account_data_write
chmod 755 "$INSTALL/data"
# Exercise the complete assertion with a real service-UID process and inherited open log FD.
# Only systemctl's PID lookup is substituted; /proc ownership, readlink, logs and databases are real.
job_store=/var/lib/etl-sql/.local/share/etlsql-data/etlsql.db
[ ! -e "$job_store" ] && [ ! -L "$job_store" ] || { echo 'Use a clean disposable container.' >&2; exit 1; }
mkdir -p "$(dirname "$job_store")" "$INSTALL/logs"
printf 'synthetic job history\n' > "$job_store"
fixture_job_store=$job_store
printf 'synthetic portal database\n' > "$INSTALL/portal.db"
chown etlsql "$job_store" "$INSTALL/portal.db" "$INSTALL/logs"
runuser -u etlsql -- sh -eu -c '
    exec 3>"$1/logs/fixture.log"
    printf "synthetic application log\n" >&3
    printf "%s\n" "$$" > "$1/data/worker.pid"
    exec sleep 60
' sh "$INSTALL" &
worker_launcher=$!
for _ in {1..100}; do
    [ ! -s "$INSTALL/data/worker.pid" ] || break
    sleep 0.1
done
worker_pid=$(cat "$INSTALL/data/worker.pid")
[[ "$worker_pid" =~ ^[1-9][0-9]*$ ]]
readlink "/proc/$worker_pid/fd/3" >/dev/null || { echo 'Cannot inspect the service UID; a container needs SYS_PTRACE.' >&2; exit 1; }
reported_pid=$worker_pid
systemctl() {
    case "$*" in
        'show -p MainPID --value etl-sql-portal.service'|'show -p MainPID --value etl-sql-orchestrator.service')
            printf '%s\n' "$reported_pid" ;;
        *) return 1 ;;
    esac
}
installed_service_writes
chown root "$INSTALL/logs/fixture.log"
reject installed_service_writes
chown etlsql "$INSTALL/logs/fixture.log"
: > "$INSTALL/logs/fixture.log"
reject installed_service_writes
printf 'synthetic application log\n' > "$INSTALL/logs/fixture.log"
reported_pid=$$
reject installed_service_writes
reported_pid=0
reject installed_service_writes
reported_pid=$worker_pid
rm "$INSTALL/logs/fixture.log"
reject installed_service_writes

echo 'Installed Linux permission and runtime-file rejection fixtures passed'
