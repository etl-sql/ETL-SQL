#!/usr/bin/env bash
# Acceptance checks for the installed candidate. Source after defining INSTALL.

installed_package_permissions() {
    local mode shipped count=0
    mode=$(stat -c '%a %U %G' "$INSTALL") || return 1
    echo "Install directory: $mode"
    [ "$mode" = '1775 root etlsql' ] || return 1
    local inventory
    inventory=$(dpkg-query -L etl-sql) || return 1
    while IFS= read -r shipped; do
        case "$shipped" in
            "$INSTALL"/*)
                [ -e "$shipped" ] || [ -L "$shipped" ] || { echo "Missing shipped path: $shipped"; return 1; }
                mode=$(stat -c '%u %a' "$shipped") || return 1
                [ "${mode%% *}" = 0 ] || { echo "Shipped path is not root-owned: $shipped"; return 1; }
                # A service account must not be able to rewrite shipped code or configuration.
                if [ ! -L "$shipped" ]; then
                    (( (8#${mode#* } & 8#022) == 0 )) || { echo "Writable shipped path: $shipped"; return 1; }
                fi
                count=$((count + 1)) ;;
        esac
    done <<< "$inventory"
    echo "Verified $count shipped paths"
    [ "$count" -gt 0 ]
}

service_owned_nonempty_file() {
    local path=$1 uid
    uid=$(id -u etlsql) || return 1
    [ -f "$path" ] && [ ! -L "$path" ] && [ -s "$path" ] || { echo "Missing nonempty service file: $path"; return 1; }
    [ "$(stat -c '%u' "$path")" = "$uid" ] || { echo "File is not service-owned: $path"; return 1; }
    # Metadata only: database, log and key contents can contain secrets.
    stat -c '%n owner=%U bytes=%s' "$path"
}

installed_service_writes() {
    local service pid uid fd target found
    uid=$(id -u etlsql) || return 1
    service_owned_nonempty_file "$INSTALL/portal.db" || return 1
    service_owned_nonempty_file /var/lib/etl-sql/.local/share/etlsql-data/etlsql.db || return 1
    for service in etl-sql-portal.service etl-sql-orchestrator.service; do
        pid=$(systemctl show -p MainPID --value "$service") || return 1
        [[ "$pid" =~ ^[1-9][0-9]*$ ]] && [ -d "/proc/$pid" ] || return 1
        [ "$(stat -c '%u' "/proc/$pid")" = "$uid" ] || return 1
        found=0
        for fd in /proc/"$pid"/fd/*; do
            target=$(readlink "$fd") || continue
            case "$target" in
                "$INSTALL"/logs/*.log)
                    service_owned_nonempty_file "$target" || return 1
                    echo "$service has an open nonempty application log"
                    found=1; break ;;
            esac
        done
        [ "$found" = 1 ] || { echo "$service has no open application log"; return 1; }
    done
    service_account_data_write
}

service_account_data_write() {
    # Exercise the same account's data-directory writes without running any operation as root.
    runuser -u etlsql -- sh -eu -c '
        directory=$1/data
        mkdir -p "$directory"
        probe=$(mktemp "$directory/installed-write.XXXXXXXX")
        trap '\''rm -f -- "$probe"'\'' EXIT
        printf "%s\n" installed-service-write > "$probe"
        test "$(cat "$probe")" = installed-service-write
    ' sh "$INSTALL" || return 1
    echo 'Service account created, read and removed runtime data'
}
