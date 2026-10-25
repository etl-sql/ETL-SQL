# Packaging stages complete attempts before replacing the accepted release directory.
function Assert-ReleaseOutputPath {
    param([string]$RepoRoot, [string]$Path)
    $relative = [IO.Path]::GetRelativePath($RepoRoot, [IO.Path]::GetFullPath($Path))
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)")) {
        throw 'Release output must remain inside the repository.'
    }
    $current = $RepoRoot
    foreach ($segment in $relative.Split([IO.Path]::DirectorySeparatorChar)) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Release output paths must not contain links.'
        }
    }
}

function Write-ReleaseOutputState {
    param($Transaction)
    [ordered]@{
        schemaVersion = 1
        status = $Transaction.Status
        accepted = $Transaction.Accepted
        output = $Transaction.Output
        previous = $Transaction.Previous
        nativeOutputs = @($Transaction.NativeOutputs)
        updatedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Transaction.Attempt 'transaction.json') -Encoding utf8
}

function New-ReleaseOutputTransaction {
    param([string]$RepoRoot)
    $RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
    $accepted = Join-Path $RepoRoot 'release'
    $parent = Join-Path $RepoRoot 'artifacts/portable-release'
    Assert-ReleaseOutputPath $RepoRoot $accepted
    Assert-ReleaseOutputPath $RepoRoot $parent
    if ((Test-Path -LiteralPath $accepted) -and -not (Test-Path -LiteralPath $accepted -PathType Container)) {
        throw 'Accepted release output must be a directory.'
    }
    $null = [IO.Directory]::CreateDirectory($parent)
    $lockPath = Join-Path $parent 'publish.lock'
    # CreateNew prevents concurrent attempts and does not remove another process's lock.
    $lock = [IO.FileStream]::new($lockPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        foreach ($attempt in Get-ChildItem -LiteralPath $parent -Directory -Force) {
            Assert-ReleaseOutputPath $RepoRoot $attempt.FullName
            Assert-ReleaseOutputPath $RepoRoot (Join-Path $attempt.FullName 'previous')
            $hasPrevious = Test-Path -LiteralPath (Join-Path $attempt.FullName 'previous')
            $recordPath = Join-Path $attempt.FullName 'transaction.json'
            if ($hasPrevious -or (Test-Path -LiteralPath $recordPath)) {
                Assert-ReleaseOutputPath $RepoRoot $recordPath
                $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
                $hasNativeBackup = $false
                foreach ($native in @($record.nativeOutputs)) {
                    if ($native) {
                        Assert-ReleaseOutputPath $RepoRoot $native.Path
                        Assert-ReleaseOutputPath $RepoRoot $native.Backup
                        if (Test-Path -LiteralPath $native.Backup) { $hasNativeBackup = $true }
                    }
                }
                if ($record.status -eq 'RecoveryRequired' -or (($hasPrevious -or $hasNativeBackup) -and $record.status -ne 'Promoted')) {
                    throw "Unresolved release recovery: $($attempt.FullName)"
                }
            }
        }
        $attemptPath = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
        $transaction = [pscustomobject]@{
            RepoRoot = $RepoRoot; Accepted = $accepted; Attempt = $attemptPath
            Output = Join-Path $attemptPath 'output'; Previous = Join-Path $attemptPath 'previous'
            LockPath = $lockPath; Lock = $lock; Status = 'Building'
            NativeOutputs = [Collections.Generic.List[object]]::new()
        }
        $null = [IO.Directory]::CreateDirectory($transaction.Output)
        Write-ReleaseOutputState $transaction
        return $transaction
    } catch {
        $lock.Dispose()
        Remove-Item -LiteralPath $lockPath -Force
        throw
    }
}

function Assert-ActiveReleaseOutputTransaction {
    param([string]$RepoRoot, $Transaction)
    $parent = Join-Path ([IO.Path]::GetFullPath($RepoRoot)) 'artifacts/portable-release'
    if ($Transaction.RepoRoot -cne [IO.Path]::GetFullPath($RepoRoot) -or
        $Transaction.Status -ne 'Building' -or $Transaction.Lock -isnot [IO.FileStream] -or -not $Transaction.Lock.CanWrite -or
        [IO.Path]::GetDirectoryName($Transaction.Attempt) -cne $parent -or
        [IO.Path]::GetFileName($Transaction.Attempt) -cnotmatch '^[0-9a-f]{32}$' -or
        $Transaction.Accepted -cne (Join-Path $RepoRoot 'release') -or
        $Transaction.Output -cne (Join-Path $Transaction.Attempt 'output') -or
        $Transaction.Previous -cne (Join-Path $Transaction.Attempt 'previous') -or
        $Transaction.LockPath -cne (Join-Path $parent 'publish.lock') -or
        $Transaction.Lock.Name -cne $Transaction.LockPath) {
        throw 'Portable staging requires the caller-owned active release transaction.'
    }
    foreach ($path in @($Transaction.Output, $Transaction.Accepted, $Transaction.LockPath)) { Assert-ReleaseOutputPath $RepoRoot $path }
}

function Save-NativeReleaseOutput {
    param($Transaction, [string]$Path)
    $Path = [IO.Path]::GetFullPath($Path)
    Assert-ReleaseOutputPath $Transaction.RepoRoot $Path
    if ((Test-Path -LiteralPath $Path) -and -not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Native package output must be a file.' }
    $backup = Join-Path $Transaction.Attempt ('native-previous/' + [IO.Path]::GetFileName($Path))
    $entry = [pscustomobject]@{ Path = $Path; Backup = $backup; Prepared = $false }
    $Transaction.NativeOutputs.Add($entry)
    Write-ReleaseOutputState $Transaction
    if (Test-Path -LiteralPath $Path) {
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
        [IO.File]::Move($Path, $backup)
    }
    $entry.Prepared = $true
    Write-ReleaseOutputState $Transaction
}

function Write-ReleaseChecksums {
    param([string]$OutputDirectory)
    $artifacts = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object {
        $_.Extension -in @('.zip', '.vsix', '.msi', '.deb', '.dmg') -or $_.Name -eq 'sbom.json'
    } | Sort-Object Name
    $checksums = foreach ($artifact in $artifacts) {
        if ($artifact.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Checksummed release artifacts must not be links.' }
        $hash = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($artifact.Name)"
    }
    $checksums | Out-File -LiteralPath (Join-Path $OutputDirectory 'sha256sums.txt') -Encoding ascii -Force
}

function Restore-NativeReleaseOutputs {
    param($Transaction)
    try {
        foreach ($entry in $Transaction.NativeOutputs) {
            if (-not $entry.Prepared) { continue }
            Assert-ReleaseOutputPath $Transaction.RepoRoot $entry.Path
            Assert-ReleaseOutputPath $Transaction.RepoRoot $entry.Backup
            if (Test-Path -LiteralPath $entry.Path) {
                $failed = Join-Path $Transaction.Attempt ('native-failed/' + [IO.Path]::GetFileName($entry.Path))
                $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($failed))
                [IO.File]::Move($entry.Path, $failed)
            }
            if (Test-Path -LiteralPath $entry.Backup) { [IO.File]::Move($entry.Backup, $entry.Path) }
        }
    } catch {
        $Transaction.Status = 'RecoveryRequired'
        Write-ReleaseOutputState $Transaction
        throw "Native package recovery requires review of $($Transaction.Attempt)."
    }
}

function Complete-ReleaseOutputTransaction {
    param($Transaction)
    foreach ($path in @($Transaction.Accepted, $Transaction.Output, $Transaction.Previous)) {
        Assert-ReleaseOutputPath $Transaction.RepoRoot $path
    }
    $Transaction.Status = 'Promoting'
    Write-ReleaseOutputState $Transaction
    if (Test-Path -LiteralPath $Transaction.Accepted) {
        [IO.Directory]::Move($Transaction.Accepted, $Transaction.Previous)
    }
    try {
        [IO.Directory]::Move($Transaction.Output, $Transaction.Accepted)
    } catch {
        $promotionError = $_
        try {
            if (Test-Path -LiteralPath $Transaction.Previous) {
                [IO.Directory]::Move($Transaction.Previous, $Transaction.Accepted)
            }
            $Transaction.Status = 'Failed'
        } catch {
            $Transaction.Status = 'RecoveryRequired'
            Write-ReleaseOutputState $Transaction
            throw "Release promotion and rollback failed. Previous release retained at $($Transaction.Previous)."
        }
        Write-ReleaseOutputState $Transaction
        throw $promotionError
    }
    $Transaction.Status = 'Promoted'
    Write-ReleaseOutputState $Transaction
}

function Close-ReleaseOutputTransaction {
    param($Transaction)
    try {
        if ($Transaction.Status -eq 'Building') {
            $Transaction.Status = 'Failed'
            Write-ReleaseOutputState $Transaction
        }
    } finally {
        $Transaction.Lock.Dispose()
        Remove-Item -LiteralPath $Transaction.LockPath -Force
    }
}
