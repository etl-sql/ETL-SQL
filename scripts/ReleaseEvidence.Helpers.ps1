# Package only the deployment-profile bundles selected by a release-specific claims index.

function Resolve-ReleaseEvidenceFile {
    param([string]$Root, [string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Evidence path must be relative: '$RelativePath'."
    }
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $RelativePath))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "Evidence path escapes its bundle: '$RelativePath'."
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing evidence file: '$RelativePath'." }
    return $path
}

function Assert-ReleaseEvidenceTimestamp {
    param([string]$Value, [int]$MaxAgeDays, [string]$Description)

    $timestamp = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [ref]$timestamp)) { throw "Missing/invalid timestamp for $Description." }
    $now = [DateTimeOffset]::UtcNow
    if ($timestamp -lt $now.AddDays(-$MaxAgeDays) -or $timestamp -gt $now.AddMinutes(5)) {
        throw "Stale or future-dated evidence for $Description."
    }
    return $timestamp
}

function Get-ReleaseCertificationInputs {
    param(
        [Parameter(Mandatory)][string]$EvidenceRoot,
        [Parameter(Mandatory)][string]$ReleaseVersion,
        [Parameter(Mandatory)][string]$CandidateCommit,
        [ValidateRange(1, 30)][int]$MaxAgeDays = 7
    )

    $root = (Resolve-Path -LiteralPath $EvidenceRoot -ErrorAction Stop).Path
    $indexPath = Resolve-ReleaseEvidenceFile -Root $root -RelativePath 'claims-index.json'
    $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
    if ($index.schemaVersion -ne 'etl-sql.deployment-profile-release-claims/v1' -or
        $index.releaseVersion -ne $ReleaseVersion -or @($index.claims).Count -eq 0) {
        throw 'Claims index does not describe the requested release.'
    }
    $null = Assert-ReleaseEvidenceTimestamp -Value $index.generatedUtc -MaxAgeDays $MaxAgeDays -Description 'claims index'
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $null = $selected.Add($indexPath)
    # The Markdown index is a view; the JSON index carries the checked claims.
    $markdownIndex = Join-Path $root 'claims-index.md'
    if (Test-Path -LiteralPath $markdownIndex -PathType Leaf) { $null = $selected.Add($markdownIndex) }

    $bundles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($claim in $index.claims) {
        if ($claim.result -eq 'NotCertified' -and $claim.releaseEligible -eq $false) { continue }
        if ($claim.result -ne 'Passed' -or $claim.releaseEligible -ne $true -or $claim.commit -ne $CandidateCommit) {
            throw "Unusable or mixed-candidate release claim: '$($claim.lane)'."
        }
        $reportPath = Resolve-ReleaseEvidenceFile -Root $root -RelativePath $claim.evidence
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.schemaVersion -ne 'etl-sql.deployment-profile-certification/v1' -or
            $report.commit -ne $CandidateCommit -or $report.dirty -ne $false -or
            $report.releaseEligible -ne $true -or $report.result -ne 'Passed' -or
            $report.kind -ne $claim.kind -or $claim.lane -notin $report.lanes -or @($report.phases).Count -eq 0) {
            throw "Unusable or mixed-candidate certification: '$($claim.evidence)'."
        }
        $reportedClaims = @($report.topologyClaims | Where-Object { $_.lane -eq $claim.lane })
        if ($reportedClaims.Count -ne 1) { throw "Missing or ambiguous topology claim: '$($claim.lane)'." }
        $reportedClaim = $reportedClaims[0]
        foreach ($field in @('topology', 'claim', 'claimScope', 'sharedSaaS')) {
            $expected = $reportedClaim.$field
            if ($field -eq 'claimScope' -and $null -eq $expected) { $expected = 'ProfileAndTransitionContracts' }
            if ($field -eq 'sharedSaaS' -and $null -eq $expected) { $expected = 'N/A' }
            if ([string]::IsNullOrWhiteSpace($claim.$field) -or $claim.$field -cne $expected) {
                throw "Release claim differs from its executed evidence ($field): '$($claim.lane)'."
            }
        }
        $expectedGaps = if ($null -eq $reportedClaim.uncovered) { @() } else { @($reportedClaim.uncovered) }
        $claimGaps = if ($null -eq $claim.uncovered) { @() } else { @($claim.uncovered) }
        if ((ConvertTo-Json -InputObject @($claimGaps) -Compress) -cne
            (ConvertTo-Json -InputObject @($expectedGaps) -Compress)) {
            throw "Release claim omits or changes uncovered scope: '$($claim.lane)'."
        }
        $bundleRoot = Split-Path -Parent $reportPath
        if ($bundleRoot -eq $root) { throw 'Certification must be in a distinct run bundle.' }
        foreach ($phase in $report.phases) {
            if ($phase.status -ne 'Passed' -or $phase.exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($phase.command)) {
                throw "Certification phase did not execute successfully: '$($phase.phase)'."
            }
            $start = Assert-ReleaseEvidenceTimestamp -Value $phase.startedUtc -MaxAgeDays $MaxAgeDays -Description $phase.phase
            $end = Assert-ReleaseEvidenceTimestamp -Value $phase.completedUtc -MaxAgeDays $MaxAgeDays -Description $phase.phase
            if ($end -lt $start) { throw "Reversed phase timestamps: '$($phase.phase)'." }
            $log = Resolve-ReleaseEvidenceFile -Root $bundleRoot -RelativePath $phase.log
            if ((Get-Item -LiteralPath $log).Length -eq 0) { throw "Empty phase log: '$($phase.log)'." }
        }
        $null = $bundles.Add($bundleRoot)
    }
    if ($bundles.Count -eq 0) { throw 'Claims index contains no passed certification bundles.' }

    foreach ($bundle in $bundles) {
        # Refuse links before archiving. A bundle may include nested provider fault evidence.
        $items = @((Get-Item -LiteralPath $bundle -Force); (Get-ChildItem -LiteralPath $bundle -Recurse -Force))
        foreach ($item in $items) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Evidence contains a filesystem link: '$($item.FullName)'." }
            if (-not $item.PSIsContainer) {
                if ($item.Name -eq 'certification.json') {
                    $nested = Get-Content -LiteralPath $item.FullName -Raw | ConvertFrom-Json
                    if ($nested.commit -ne $CandidateCommit -or $nested.dirty -ne $false -or $nested.result -ne 'Passed') {
                        throw "Unusable or mixed-candidate nested certification: '$($item.FullName)'."
                    }
                }
                $null = $selected.Add($item.FullName)
            }
        }
    }
    foreach ($path in $selected) {
        $null = Resolve-ReleaseEvidenceFile -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $path))
        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Evidence contains a filesystem link: '$path'."
        }
        [pscustomobject]@{
            Path = $path
            RelativePath = [IO.Path]::GetRelativePath($root, $path).Replace('\', '/')
            Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

function New-ReleaseCertificationArchive {
    param(
        [Parameter(Mandatory)][string]$EvidenceRoot,
        [Parameter(Mandatory)][string]$ReleaseVersion,
        [Parameter(Mandatory)][string]$CandidateCommit,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $files = @(Get-ReleaseCertificationInputs -EvidenceRoot $EvidenceRoot -ReleaseVersion $ReleaseVersion -CandidateCommit $CandidateCommit)
    $manifest = [ordered]@{
        schemaVersion = 'etl-sql.release-certification-archive/v1'
        releaseVersion = $ReleaseVersion
        commit = $CandidateCommit
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        scope = 'Deployment-profile contracts and the selected certified transitions; uncovered claims remain in the index.'
        files = @($files | Sort-Object RelativePath | ForEach-Object { [ordered]@{ path = $_.RelativePath; sha256 = $_.Sha256 } })
    }
    $temporary = "$DestinationPath.$([guid]::NewGuid().ToString('N')).tmp.zip"
    try {
        $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files) {
                $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.Path, $file.RelativePath)
            }
            $entry = $archive.CreateEntry('archive-manifest.json')
            $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
            try { $writer.Write(($manifest | ConvertTo-Json -Depth 8)) } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }

        $archive = [IO.Compression.ZipFile]::OpenRead($temporary)
        try {
            foreach ($file in $files) {
                $stream = $archive.GetEntry($file.RelativePath).Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
                finally { $stream.Dispose() }
                if ($hash -ne $file.Sha256) { throw "Evidence changed while archiving: '$($file.RelativePath)'." }
            }
        } finally { $archive.Dispose() }
        Move-Item -LiteralPath $temporary -Destination $DestinationPath -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
