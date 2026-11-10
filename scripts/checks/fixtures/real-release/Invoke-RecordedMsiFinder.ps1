# Runs Find-CertifiedMsi.ps1 against a recording of the GitHub responses it read for a real
# candidate. gh is replaced by a function that answers only from the recording.
param(
    [Parameter(Mandatory)] [string]$Recording,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [Parameter(Mandatory)] [datetimeoffset]$Now,
    [AllowEmptyString()] [string]$PreviousTag = ''
)
$ErrorActionPreference = 'Stop'
$global:RecordedMsiFinder = Get-Content -LiteralPath $Recording -Raw | ConvertFrom-Json -AsHashtable -DateKind String

function gh {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'api') {
        $endpoint = [string]$args[-1]
        if (-not $global:RecordedMsiFinder.responses.ContainsKey($endpoint)) { throw "Not in the recording: $endpoint" }
        return ConvertTo-Json -InputObject $global:RecordedMsiFinder.responses[$endpoint] -Depth 100
    }
    if ($args[0] -eq 'run' -and $args[1] -eq 'download') {
        $name = [string]$args[[array]::IndexOf($args, '--name') + 1]
        $directory = [string]$args[[array]::IndexOf($args, '--dir') + 1]
        if (-not $global:RecordedMsiFinder.receipts.ContainsKey($name)) { throw "Receipt not in the recording: $name" }
        ConvertTo-Json -InputObject $global:RecordedMsiFinder.receipts[$name] -Depth 20 | Set-Content -LiteralPath (Join-Path $directory 'certification.json')
        return
    }
    throw "Unexpected gh call: $args"
}

& (Join-Path $PSScriptRoot '../../../Find-CertifiedMsi.ps1') -SourceSha $global:RecordedMsiFinder.sha -Repository $global:RecordedMsiFinder.repository `
    -PreviousTag $PreviousTag -OutputDirectory $OutputDirectory -Now $Now
