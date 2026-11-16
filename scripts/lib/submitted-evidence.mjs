// Safe extraction of a submitted evidence ZIP (bundle/ plus an optional selection.json), bounded by
// entry count and expanded size. The Windows payload collector uses it for retained package bundles.
import { spawnSync } from 'node:child_process';
import path from 'node:path';

function command(executable, args, options = {}) {
  const result = spawnSync(executable, args, { encoding: 'utf8', windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024, ...options });
  if (result.error || result.status !== 0) throw new Error(`Cannot complete submitted evidence command: ${executable}.`);
  return result.stdout;
}

export function extractSubmittedArchive(archive, directory, { maximumBytes = Number.MAX_SAFE_INTEGER, maximumEntries = Number.MAX_SAFE_INTEGER, allowSelection = false, timeoutMs = 60000 } = {}) {
  if (!Number.isSafeInteger(maximumBytes) || maximumBytes < 1 || !Number.isSafeInteger(maximumEntries) || maximumEntries < 1 ||
      !Number.isSafeInteger(timeoutMs) || timeoutMs < 1 || timeoutMs > 300000) throw new Error('Invalid evidence archive limits.');
  command('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `
$ErrorActionPreference='Stop'
$zip=[IO.Compression.ZipFile]::OpenRead($env:ETLSQL_EVIDENCE_ZIP)
try {
  if($zip.Entries.Count -gt [long]$env:ETLSQL_EVIDENCE_MAX_ENTRIES) { throw 'Evidence ZIP has too many entries.' }
  $totalBytes=0L
  foreach($entry in $zip.Entries) {
    if($entry.Length -gt [long]$env:ETLSQL_EVIDENCE_MAX_BYTES) { throw 'Evidence ZIP payload exceeds its limit.' }
    $totalBytes += $entry.Length
    if($totalBytes -gt [long]$env:ETLSQL_EVIDENCE_MAX_BYTES) { throw 'Evidence ZIP payload exceeds its limit.' }
    if(($entry.FullName -cnotmatch '^bundle/' -and -not ($env:ETLSQL_EVIDENCE_ALLOW_SELECTION -eq 'true' -and $entry.FullName -ceq 'selection.json')) -or $entry.FullName.Contains('\\') -or $entry.FullName.Contains(':') -or $entry.FullName.Contains([char]0) -or
       @($entry.FullName.Split('/') | Where-Object {$_ -eq '..' -or $_ -eq '.'}).Count -gt 0 -or
       (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe evidence ZIP entry.' }
  }
  $output=[IO.Path]::GetFullPath($env:ETLSQL_EVIDENCE_OUTPUT)
  if(Test-Path -LiteralPath $output) {
    $existing=Get-Item -LiteralPath $output -Force
    if(-not $existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
       @(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'Evidence extraction needs a new or empty ordinary directory.' }
  }
  $null=[IO.Directory]::CreateDirectory($output)
  $prefix=$output.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
  $comparison=if($IsWindows){[StringComparison]::OrdinalIgnoreCase}else{[StringComparison]::Ordinal}
  $actualBytes=0L
  foreach($entry in $zip.Entries) {
    $destination=[IO.Path]::GetFullPath([IO.Path]::Combine($output,$entry.FullName))
    if(-not $destination.StartsWith($prefix,$comparison)) { throw 'Evidence ZIP entry escapes its output.' }
    if($entry.FullName.EndsWith('/')) {
      if($entry.Length -ne 0) { throw 'Invalid evidence directory entry.' }
      $null=[IO.Directory]::CreateDirectory($destination)
      continue
    }
    $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
    $source=$entry.Open()
    try {
      $target=[IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
      try {
        $buffer=[byte[]]::new(65536)
        $entryBytes=0L
        while(($read=$source.Read($buffer,0,$buffer.Length)) -gt 0) {
          $entryBytes += $read
          $actualBytes += $read
          if($actualBytes -gt [long]$env:ETLSQL_EVIDENCE_MAX_BYTES -or $entryBytes -gt $entry.Length) { throw 'Expanded evidence exceeds its declared limit.' }
          $target.Write($buffer,0,$read)
        }
        if($entryBytes -ne $entry.Length) { throw 'Expanded evidence length differs from its ZIP entry.' }
      } finally {$target.Dispose()}
    } finally {$source.Dispose()}
  }
} finally {$zip.Dispose()}
`], { timeout: timeoutMs, env: { ...process.env, ETLSQL_EVIDENCE_ZIP: path.resolve(archive), ETLSQL_EVIDENCE_OUTPUT: path.resolve(directory),
    ETLSQL_EVIDENCE_MAX_BYTES: String(maximumBytes), ETLSQL_EVIDENCE_MAX_ENTRIES: String(maximumEntries), ETLSQL_EVIDENCE_ALLOW_SELECTION: String(allowSelection) } });
}
