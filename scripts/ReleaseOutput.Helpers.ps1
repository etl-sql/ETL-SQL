# Portable packaging stages complete attempts before replacing the accepted release directory.
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
        updatedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Transaction.Attempt 'transaction.json') -Encoding utf8
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
            if (Test-Path -LiteralPath (Join-Path $attempt.FullName 'previous')) {
                $recordPath = Join-Path $attempt.FullName 'transaction.json'
                Assert-ReleaseOutputPath $RepoRoot $recordPath
                $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
                if ($record.status -ne 'Promoted') { throw "Unresolved release recovery: $($attempt.FullName)" }
            }
        }
        $attemptPath = Join-Path $parent ([Guid]::NewGuid().ToString('N'))
        $transaction = [pscustomobject]@{
            RepoRoot = $RepoRoot; Accepted = $accepted; Attempt = $attemptPath
            Output = Join-Path $attemptPath 'output'; Previous = Join-Path $attemptPath 'previous'
            LockPath = $lockPath; Lock = $lock; Status = 'Building'
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
