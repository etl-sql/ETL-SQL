#!/usr/bin/env bash
# Run on Linux: inspect a real DEB and real macOS bundle staging; image creation is mocked.
set -euo pipefail

SOURCE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEMP_BASE="$(cd "${TMPDIR:-/tmp}" && pwd)"
FIXTURE="$(mktemp -d "$TEMP_BASE/etlsql-package-modes.XXXXXXXX")"
cleanup() {
    if [[ "$FIXTURE" != "$TEMP_BASE"/etlsql-package-modes.* || -L "$FIXTURE" ]]; then
        echo 'Unexpected package fixture cleanup path' >&2
        return 1
    fi
    rm -rf -- "$FIXTURE"
}
trap cleanup EXIT
command -v dpkg-deb >/dev/null
unset -f hdiutil dpkg-deb 2>/dev/null || true
mkdir -p "$FIXTURE/scripts" "$FIXTURE/src/ETL-SQL.Installer/linux" "$FIXTURE/payload"
cp "$SOURCE_ROOT/scripts/build-linux-packages.sh" "$FIXTURE/scripts/"
cp "$SOURCE_ROOT/scripts/build-mac-dmg.sh" "$FIXTURE/scripts/"
cp "$SOURCE_ROOT/"{LICENSE.md,NOTICE.md,THIRD-PARTY-NOTICES.md} "$FIXTURE/"
cp "$SOURCE_ROOT/src/ETL-SQL.Installer/linux/"*.service "$FIXTURE/src/ETL-SQL.Installer/linux/"
for name in ETL-SQL ETL-SQL-TUI ETL-SQL-LSP ETL-SQL-Report ETL-SQL-Player ETL-SQL-Portal ETL-SQL-Service; do
    printf '#!/bin/sh\nexit 0\n' > "$FIXTURE/payload/$name"
    chmod 777 "$FIXTURE/payload/$name"
done
printf 'synthetic public library\n' > "$FIXTURE/payload/library.dll"
chmod 666 "$FIXTURE/payload/library.dll"
printf 'synthetic read-only file\n' > "$FIXTURE/payload/read-only.txt"
chmod 444 "$FIXTURE/payload/read-only.txt"
ln -s ETL-SQL "$FIXTURE/payload/cli-link"
cd "$FIXTURE"
mkdir -p no-tools src/ETL-SQL.Installer/linux/build
printf 'keep until tools validate\n' > src/ETL-SQL.Installer/linux/build/sentinel
if PATH="$FIXTURE/no-tools" /bin/bash scripts/build-linux-packages.sh 0.22.0 "$FIXTURE/payload" > missing-tool.log 2>&1; then
    echo 'Missing dpkg-deb incorrectly reported success' >&2
    exit 1
fi
test -f src/ETL-SQL.Installer/linux/build/sentinel
grep -F 'dpkg-deb is required' missing-tool.log
mkdir caller
(cd caller; bash -euo pipefail "$FIXTURE/scripts/build-linux-packages.sh" 0.22.0 "$FIXTURE/payload")
PACKAGE=etl-sql_0.22.0_amd64.deb
dpkg-deb --contents "$PACKAGE" > contents.txt
awk '
    $NF ~ /^\.\/usr\/lib\/etl-sql\/bin\// && substr($1,1,1) == "-" &&
        (substr($1,6,1) == "w" || substr($1,9,1) == "w") { print "Writable packaged program: " $NF; bad=1 }
    END { exit bad }
' contents.txt
[[ "$(awk '$NF == "./usr/lib/etl-sql/bin/ETL-SQL" { print $1 }' contents.txt)" == '-rwxr-xr-x' ]]
[[ "$(awk '$NF == "./usr/lib/etl-sql/bin/library.dll" { print $1 }' contents.txt)" == '-rw-r--r--' ]]
[[ "$(awk '$NF == "./usr/lib/etl-sql/bin/read-only.txt" { print $1 }' contents.txt)" == '-r--r--r--' ]]

mkdir -p src/ETL-SQL.Installer/mac
printf 'keep until tools validate\n' > src/ETL-SQL.Installer/mac/sentinel
if PATH="$FIXTURE/no-tools" /bin/bash scripts/build-mac-dmg.sh 0.22.0 "$FIXTURE/payload" > missing-mac-tool.log 2>&1; then
    echo 'Missing hdiutil incorrectly reported success' >&2
    exit 1
fi
test -f src/ETL-SQL.Installer/mac/sentinel
grep -F 'hdiutil is required' missing-mac-tool.log
hdiutil() { printf fixture-dmg > ETL-SQL_v0.22.0.dmg; }
export -f hdiutil
# Exercise real bundle construction on POSIX; only image creation is mocked.
(umask 000; cd caller; bash "$FIXTURE/scripts/build-mac-dmg.sh" 0.22.0 "$FIXTURE/payload")
test -s ETL-SQL_v0.22.0.dmg
BUNDLE=src/ETL-SQL.Installer/mac/ETL-SQL.app
test -z "$(find "$BUNDLE" -type f -perm /0022 -print)"
test -z "$(find "$BUNDLE" -type d -perm /0022 -print)"
[[ "$(stat -c %a "$BUNDLE/Contents/MacOS/ETL-SQL")" == 755 ]]
test ! -w "$BUNDLE/Contents/Resources/bin/read-only.txt" || [[ "$(stat -c %a "$BUNDLE/Contents/Resources/bin/read-only.txt")" == 444 ]]

# A linked installer ancestor must not redirect recursive staging cleanup into another tree.
mkdir -p guarded/scripts guarded/src held-installer/linux/build held-installer/mac
cp scripts/build-linux-packages.sh scripts/build-mac-dmg.sh guarded/scripts/
printf keep > held-installer/linux/build/sentinel
printf keep > held-installer/mac/sentinel
ln -s "$FIXTURE/held-installer" guarded/src/ETL-SQL.Installer
for builder in build-linux-packages.sh build-mac-dmg.sh; do
    if bash "$FIXTURE/guarded/scripts/$builder" 0.22.0 "$FIXTURE/payload" > guarded.log 2>&1; then
        echo 'Linked staging ancestor was accepted' >&2
        exit 1
    fi
done
test -f held-installer/linux/build/sentinel
test -f held-installer/mac/sentinel
rm guarded/src/ETL-SQL.Installer
mkdir guarded/src/ETL-SQL.Installer
ln -s "$FIXTURE/held-installer/linux" guarded/src/ETL-SQL.Installer/linux
if bash "$FIXTURE/guarded/scripts/build-linux-packages.sh" 0.22.0 "$FIXTURE/payload" > guarded-child.log 2>&1; then
    echo 'Linked Linux staging ancestor was accepted' >&2
    exit 1
fi
test -f held-installer/linux/build/sentinel
rm guarded/src/ETL-SQL.Installer/linux
mkdir guarded/src/ETL-SQL.Installer/linux
ln -s "$FIXTURE/held-installer/linux/build" guarded/src/ETL-SQL.Installer/linux/build
ln -s "$FIXTURE/held-installer/mac" guarded/src/ETL-SQL.Installer/mac
for builder in build-linux-packages.sh build-mac-dmg.sh; do
    if bash "$FIXTURE/guarded/scripts/$builder" 0.22.0 "$FIXTURE/payload" > guarded-output.log 2>&1; then
        echo 'Linked staging output was accepted' >&2
        exit 1
    fi
done
test -f held-installer/linux/build/sentinel
test -f held-installer/mac/sentinel
echo 'Native package modes: actual DEB and macOS bundle construction strip non-owner writes; read-only inputs preserved. DMG creation mocked; nothing installed.'
