param(
    [string]$CoverageBase = "",
    [switch]$SkipCoverageCheck,
    [switch]$CheckCoverageOnly
)

$ErrorActionPreference = "Stop"
if ($SkipCoverageCheck -and $CheckCoverageOnly) {
    throw 'SkipCoverageCheck and CheckCoverageOnly are mutually exclusive.'
}
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $ScriptRoot "..")).Path
$ChangelogDir = Join-Path $RepoRoot "changelog.d"
$ChangelogFile = Join-Path $RepoRoot "CHANGELOG.md"

function Get-ChangelogFragments {
    if (-not (Test-Path -LiteralPath $ChangelogDir)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $ChangelogDir -File -Filter "*.md" | Where-Object { $_.Name -ne "README.md" })
}

function Invoke-ChangelogGit {
    param([string[]]$Arguments, [switch]$AllowFailure)

    $git = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $start = [Diagnostics.ProcessStartInfo]::new($git)
    $start.WorkingDirectory = $RepoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
    $start.ArgumentList.Add('--no-pager')
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            throw 'Git inspection timed out; changelog coverage could not be verified.'
        }
        if (-not $output.Wait(5000) -or -not $errorOutput.Wait(5000)) {
            throw 'Git inspection output did not close; changelog coverage could not be verified.'
        }
        if ($process.ExitCode -ne 0 -and -not $AllowFailure) {
            throw "Git inspection failed (exit $($process.ExitCode)); changelog coverage could not be verified."
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output.GetAwaiter().GetResult() }
    }
    finally {
        $process.Dispose()
    }
}

function Resolve-ChangelogCoverageBase {
    if (-not [string]::IsNullOrWhiteSpace($CoverageBase)) {
        return $CoverageBase
    }

    foreach ($candidate in @("origin/main", "main")) {
        $base = Invoke-ChangelogGit -Arguments @('merge-base', 'HEAD', $candidate) -AllowFailure
        if ($base.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($base.Output)) {
            return $base.Output.Trim()
        }
    }

    $tag = Invoke-ChangelogGit -Arguments @('describe', '--tags', '--abbrev=0', 'HEAD') -AllowFailure
    if ($tag.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag.Output)) {
        return $tag.Output.Trim()
    }
    throw 'No changelog comparison base could be resolved. Supply CoverageBase with a valid comparison commit.'
}

function Test-RequiresChangelogCoverage {
    param([string]$Path)

    $normalized = $Path -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $false
    }

    if ($normalized -eq "CHANGELOG.md" -or $normalized.StartsWith("changelog.d/")) {
        return $false
    }

    if ($normalized -eq "TODO.md" -or $normalized -eq "ROADMAP.md") {
        return $false
    }

    if ($normalized.StartsWith("docs/releases/") -or $normalized.StartsWith("docs/architecture/")) {
        return $false
    }

    return $normalized.StartsWith("src/") -or
        $normalized.StartsWith("tests/") -or
        $normalized.StartsWith("samples/") -or
        $normalized.StartsWith("docs/") -or
        $normalized.StartsWith("scripts/") -or
        $normalized.StartsWith(".github/workflows/")
}

function Get-ChangedFilesForChangelogCoverage {
    $files = New-Object System.Collections.Generic.HashSet[string]
    $prefix = Invoke-ChangelogGit -Arguments @('rev-parse', '--show-prefix')
    if (-not [string]::IsNullOrWhiteSpace($prefix.Output)) {
        throw 'Changelog coverage must inspect the compiler checkout, not a parent repository.'
    }
    $base = Resolve-ChangelogCoverageBase
    $resolved = Invoke-ChangelogGit -Arguments @('rev-parse', '--verify', '--end-of-options', "$base^{commit}") -AllowFailure
    if ($resolved.ExitCode -ne 0 -or $resolved.Output.Trim() -cnotmatch '\A[0-9a-f]{40}([0-9a-f]{24})?\z') {
        throw 'Changelog comparison base is not a valid commit.'
    }
    $queries = @(
        @('diff', '--name-only', '-z', '--no-ext-diff', $resolved.Output.Trim(), 'HEAD', '--'),
        @('diff', '--name-only', '-z', '--no-ext-diff', '--'),
        @('diff', '--cached', '--name-only', '-z', '--no-ext-diff', '--'),
        @('ls-files', '--others', '--exclude-standard', '-z', '--')
    )
    foreach ($query in $queries) {
        $result = Invoke-ChangelogGit -Arguments $query
        foreach ($file in $result.Output.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) {
            [void]$files.Add($file)
        }
    }

    return @($files)
}

function Test-ChangelogCoverage {
    param([array]$Fragments)

    $changedFiles = @(Get-ChangedFilesForChangelogCoverage)
    $coveredFiles = @($changedFiles | Where-Object { Test-RequiresChangelogCoverage $_ })
    if ($coveredFiles.Count -eq 0) {
        Write-Output "No changelog-covered feature surface changes detected."
        return
    }

    $hasFragment = $Fragments.Count -gt 0
    $hasChangelogChange = @($changedFiles | Where-Object { ($_ -replace '\\', '/') -eq "CHANGELOG.md" }).Count -gt 0
    if ($hasFragment -or $hasChangelogChange) {
        Write-Output "Changelog coverage present for $($coveredFiles.Count) changed feature-surface file(s)."
        return
    }

    $examples = ($coveredFiles | Select-Object -First 10) -join ", "
    throw "Feature-surface changes require changelog coverage. Add a changelog.d/<feature>.md fragment or update CHANGELOG.md. Changed files include: $examples"
}

$fragments = @(Get-ChangelogFragments)

if (-not $SkipCoverageCheck) {
    Test-ChangelogCoverage -Fragments $fragments
    if ($CheckCoverageOnly) {
        exit 0
    }
}

if (-not (Test-Path -LiteralPath $ChangelogDir)) {
    [IO.Directory]::CreateDirectory($ChangelogDir) | Out-Null
}

if ($fragments.Count -eq 0) {
    Write-Output "No changelog fragments found in changelog.d/."
    exit 0
}

Write-Output "Found $($fragments.Count) changelog fragment(s). Compiling..."

$combinedContent = New-Object System.Collections.Generic.List[string]
foreach ($file in $fragments) {
    Write-Output "Processing fragment: $($file.Name)"
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if (-not [string]::IsNullOrWhiteSpace($content)) {
        $combinedContent.Add($content.Trim())
        $combinedContent.Add("") # Space between fragments
    }
}

if ($combinedContent.Count -eq 0) {
    Write-Output "All fragments were empty. Deleting them."
    foreach ($file in $fragments) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    exit 0
}

$changelogLines = Get-Content -LiteralPath $ChangelogFile
$newLines = New-Object System.Collections.Generic.List[string]

$inserted = $false
foreach ($line in $changelogLines) {
    $newLines.Add($line)
    if ($line.Trim() -eq "## [Unreleased]") {
        $newLines.Add("")
        foreach ($cLine in $combinedContent) {
            # Handle multi-line strings split by newlines
            foreach ($subLine in ($cLine -split "`r?\n")) {
                $newLines.Add($subLine)
            }
        }
        $inserted = $true
    }
}

if (-not $inserted) {
    throw "Could not find '## [Unreleased]' section in CHANGELOG.md."
}

# Write the updated CHANGELOG.md
$newLines | Set-Content -LiteralPath $ChangelogFile -Encoding UTF8

# Delete the processed fragments
foreach ($file in $fragments) {
    Remove-Item -LiteralPath $file.FullName -Force
    Write-Output "Deleted fragment: $($file.Name)"
}

Write-Output "Changelog fragments successfully compiled into CHANGELOG.md."
