# Windows Service Deployment Template

This directory contains the Windows service installer for isolated ETL-SQL environments. Each environment gets separate Portal and Orchestrator services, a dedicated service account, ACL-locked storage, per-service configuration, and independently managed ports and keys.

## Files

| File | Purpose |
| :--- | :--- |
| `Install-Environment.ps1` | Registers `ETL-SQL-Portal-<env>` and `ETL-SQL-Orchestrator-<env>` with isolated configuration and storage. |

## Usage

Run PowerShell 7 as an administrator and install one environment at a time. Use unique ports, storage roots, service accounts, JWT secrets, dataset keys, and Orchestrator API keys for every environment.

```powershell
pwsh -File .\Install-Environment.ps1 `
  -Environment finance `
  -BinPath 'C:\Program Files\ETL-SQL\bin' `
  -ServiceAccount 'CORP\svc-etlsql-finance' `
  -PortBase 5010 `
  -JwtSecret (Read-Host 'JWT secret') `
  -DatasetKey (Read-Host 'Dataset key') `
  -OrchestratorApiKey (Read-Host 'Orchestrator API key')

Start-Service ETL-SQL-Portal-finance
Start-Service ETL-SQL-Orchestrator-finance
```

After installing more than one environment, verify isolation and ACL boundaries:

```powershell
pwsh -File ..\verify\Test-Isolation.ps1 C:\ETL-SQL\*\*.env -CheckAcls
```

See [`../README.md`](../README.md) for the platform overview and [`../../docs/architecture/decisions/Departmental_Isolation.md`](../../docs/architecture/decisions/departmental-isolation.md) for the full runbook.
