#!/usr/bin/env bash
# Shared input boundary for native DEB and DMG builders. Never read runtime contents.
assert_clean_native_publish() {
    local published="$1" state pattern name
    if [[ ! -d "$published" || ! -f "$published/ETL-SQL" ]]; then
        echo '[ERROR] Published binary directory must contain the ETL-SQL CLI.' >&2
        return 1
    fi
    # COMPAT_BREAK: 0.22 — refuse runtime state instead of distributing installation secrets.
    # Keep protected staging/recovery copies and SQLite sidecars under the same boundary.
    while IFS= read -r pattern || [[ -n "$pattern" ]]; do
        for state in "$published"/* "$published"/.[!.]* "$published"/..?*; do
            name="${state##*/}"
            if [[ "$name" == $pattern && ( -e "$state" || -L "$state" ) ]]; then
                echo '[ERROR] Runtime state in the publish folder. Publish again into a clean folder.' >&2
                return 1
            fi
        done
    done < "${BASH_SOURCE[0]%/*}/package-runtime-state.patterns"
}
