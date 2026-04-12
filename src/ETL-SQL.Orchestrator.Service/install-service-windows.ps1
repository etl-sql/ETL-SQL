<#
.SYNOPSIS
    Installs ETL-SQL-OrchestratorService as a Windows Service.

.DESCRIPTION
    Publishes the Orchestrator Service executable, then registers it with the
    Windows Service Control Manager using sc.exe. Requires Administrator privileges.

.PARAMETER InstallDir
    Directory to publish the service binary. Default: C:\ETL-SQL\OrchestratorService

.PARAMETER ServiceName
    Windows Service name. Default: ETL-SQL-OrchestratorService

.EXAMPLE
    # Run in an elevated PowerShell session:
    .\install-service-windows.ps1

    # Custom install path:
    .\install-service-windows.ps1 -InstallDir "D:\Services\ETL-SQL"
#>
param(
    [string]$InstallDir   = "C:\ETL-SQL\OrchestratorService",
    [string]$ServiceName  = "ETL-SQL-OrchestratorService",
    [string]$DisplayName  = "ETL-SQL Orchestrator Service",
    [string]$Description  = "Manages scheduled ETL-SQL jobs and exposes the job submission API."
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing Orchestrator Service to $InstallDir ..."
$projectPath = Join-Path $PSScriptRoot "ETL-SQL.Orchestrator.Service.csproj"
dotnet publish $projectPath -c Release -o $InstallDir --self-contained false

$exePath = Join-Path $InstallDir "ETL-SQL-OrchestratorService.exe"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping existing service ..."
    Stop-Service -Name $ServiceName -Force
    Write-Host "Removing existing service ..."
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Registering Windows Service: $ServiceName"
sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto DisplayName= $DisplayName
sc.exe description $ServiceName $Description
sc.exe start $ServiceName

Write-Host ""
Write-Host "Service installed and started. Use 'sc.exe query $ServiceName' to check status."
Write-Host "Logs will appear in: $InstallDir\logs\"
