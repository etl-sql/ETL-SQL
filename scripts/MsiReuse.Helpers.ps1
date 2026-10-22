# Pure policy for selecting an exact-commit MSI certification from another run.
function Test-MsiCandidateRun {
    param($Run, [string]$SourceSha, [string]$Repository, [datetimeoffset]$Now = [datetimeoffset]::UtcNow)
    $created = [datetimeoffset]::MinValue
    return $Run.head_sha -ceq $SourceSha -and
        (($Run.path -ceq '.github/workflows/msi-upgrade.yml' -and $Run.event -in @('push', 'workflow_dispatch')) -or
         ($Run.path -ceq '.github/workflows/nightly-release.yml' -and $Run.event -ceq 'workflow_dispatch')) -and
        $Run.head_repository.full_name -ieq $Repository -and
        $Run.head_branch -clike 'release/*' -and
        [datetimeoffset]::TryParse([string]$Run.created_at, [ref]$created) -and
        $created -le $Now -and $created -ge $Now.AddDays(-7)
}

function Test-MsiReusableRun {
    param($Run, [string]$SourceSha, [string]$Repository, [datetimeoffset]$Now = [datetimeoffset]::UtcNow)
    return (Test-MsiCandidateRun -Run $Run -SourceSha $SourceSha -Repository $Repository -Now $Now) -and
        $Run.status -eq 'completed' -and $Run.conclusion -eq 'success'
}

function Get-MsiReusableArtifact {
    param($Receipt, $Run, [object[]]$Artifacts, [string]$SourceSha, [string]$Repository, [string]$PreviousTag)
    if ($Receipt.schemaVersion -ne 1 -or $Receipt.sourceSha -cne $SourceSha -or
        $Receipt.repository -ine $Repository -or [string]$Receipt.runId -cne [string]$Run.id -or
        [string]$Receipt.runAttempt -cne [string]$Run.run_attempt -or
        $Receipt.previousTag -cne $PreviousTag -or
        $Receipt.workstation -cne 'success' -or $Receipt.portal -cne 'success') {
        throw 'MSI certification receipt does not match the candidate, predecessor or successful run.'
    }
    $matchingArtifacts = @($Artifacts | Where-Object { [string]$_.id -ceq [string]$Receipt.artifactId })
    if ($matchingArtifacts.Count -ne 1 -or $matchingArtifacts[0].expired -ne $false -or
        $matchingArtifacts[0].name -cnotmatch "^msi-inputs-$($Run.id)-[1-9][0-9]*$") {
        throw 'Certified MSI input artifact is missing, expired or belongs to another run.'
    }
    return [string]$matchingArtifacts[0].id
}
