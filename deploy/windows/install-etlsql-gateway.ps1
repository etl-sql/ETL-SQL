param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$ServiceName = 'ETLSQLGateway'
)

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not [System.IO.Path]::IsPathFullyQualified($resolvedExecutable)) {
    throw 'The Gateway executable path must be absolute.'
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName ('"{0}" gateway start' -f $resolvedExecutable) `
    -DisplayName 'ETL-SQL Secure Outbound Data Gateway' `
    -Description 'Maintains an authenticated outbound-only ETL-SQL Gateway session.' `
    -StartupType Automatic
Start-Service -Name $ServiceName
