[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$Hang,
    [switch]$Supervise,
    [string]$SupervisorEvidenceDirectory = 'release-validation/browser-watchdog/supervisor',
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
if ($Supervise) {
    $arguments = @('-AssemblyPath', $AssemblyPath, '-EvidenceDirectory', $EvidenceDirectory)
    if ($Hang) { $arguments += '-Hang' }
    & (Join-Path $PSScriptRoot '../Invoke-CertificationProcess.ps1') -ScriptPath $PSCommandPath `
        -ScriptArguments $arguments -EvidenceDirectory $SupervisorEvidenceDirectory `
        -TimeoutSeconds $TimeoutSeconds -HeartbeatSeconds 5
    exit $LASTEXITCODE
}
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$env:PLAYWRIGHT_DRIVER_SEARCH_PATH = Split-Path -Parent $assembly
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assembly)) | Out-Null
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$null = New-Item -ItemType Directory -Force -Path $evidence
$playwright = [Microsoft.Playwright.Playwright]::CreateAsync().GetAwaiter().GetResult()
try {
    $browser = $playwright.Chromium.LaunchAsync([Microsoft.Playwright.BrowserTypeLaunchOptions]@{ Headless = $true }).GetAwaiter().GetResult()
    try {
        $page = $browser.NewPageAsync().GetAwaiter().GetResult()
        $null = $page.GotoAsync('data:text/html,<title>rehearsal-ready</title><p>ready</p>').GetAwaiter().GetResult()
        $title = $page.TitleAsync().GetAwaiter().GetResult()
        if ($title -cne 'rehearsal-ready') { throw 'The real browser did not render the probe.' }
        $cdp = $browser.NewBrowserCDPSessionAsync().GetAwaiter().GetResult()
        $processInfo = $cdp.SendAsync('SystemInfo.getProcessInfo', $null).GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $evidence 'browser-processes.json'), $processInfo.GetRawText())
        [ordered]@{ startedUtc = [datetimeoffset]::UtcNow; probePid = $PID; title = $title; intentionalHang = [bool]$Hang } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'browser-ready.json')
        Write-Output 'Real browser ready; process evidence saved.'
        if ($Hang) {
            Write-Output 'Deliberate browser wait with its own timeout disabled; the outer watchdog must stop this child.'
            $null = $page.WaitForSelectorAsync('#deliberately-never-present', [Microsoft.Playwright.PageWaitForSelectorOptions]@{ Timeout = 0 }).GetAwaiter().GetResult()
            throw 'The deliberate browser wait unexpectedly completed.'
        }
        [ordered]@{ status = 'Passed'; completedUtc = [datetimeoffset]::UtcNow; title = $title } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'browser-completed.json')
    } finally {
        $null = $browser.CloseAsync().GetAwaiter().GetResult()
    }
} finally {
    $playwright.Dispose()
}
