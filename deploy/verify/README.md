# Isolation verifier

Proves a set of ETL-SQL environments are isolated — no two share a database target, artifact root,
Data Protection key ring, port, service account, or encryption key. Run it after adding an
environment, changing a service account, or before promoting to production. Full runbook:
[Departmental_Isolation.md §6](../../docs/architecture/decisions/Departmental_Isolation.md#6-isolation-verification-runbook).

| Script | Platform |
| :--- | :--- |
| `Test-Isolation.ps1` | Windows / PowerShell 7 (`-CheckAcls` adds a cross-account ACL probe; run elevated) |
| `verify-isolation.sh` | Linux / macOS (bash) |

```bash
# Linux
sudo ./verify-isolation.sh /srv/etl-sql/*/*.env

# Windows
pwsh -File Test-Isolation.ps1 C:\ETL-SQL\*\*.env -CheckAcls
```

It reads one descriptor file per environment (`KEY=VALUE`); the Windows and systemd installers emit
one as `<root>/<env>.env`, and Docker env files are accepted directly. Descriptors are grouped by
`ETLSQL_ENV`, so multiple HA-node descriptors for the *same* environment are not flagged against each
other. **Exit 0** = isolated, **1** = overlap(s) found (listed; secret values masked), **2** = usage
error. The verifier is certified by `IsolationVerifierTests` in `ETL-SQL.Tests`.
