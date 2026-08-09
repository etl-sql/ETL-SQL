# Configuration Files

The published services read `appsettings.json`, environment variables, and encrypted configuration values. Production templates live beside the service projects:

| Service | Template |
| :--- | :--- |
| Orchestrator | `src/ETL-SQL.Orchestrator.Service/appsettings.Production.json.template` |
| Portal | `src/ETL-SQL.Portal/appsettings.Production.json.template` |

Common environment-variable overrides use .NET's double-underscore convention:

```text
Portal__DatabasePath=C:\ETL-SQL\data\portal.db
Portal__Database__Provider=Sqlite
Portal__ScriptRootPath=C:\ETL-SQL\scripts
Portal__SnapshotDirectory=C:\ETL-SQL\snapshots
Portal__Orchestrator__ApiUrl=https://orchestrator.example.com:5003
Portal__Orchestrator__ApiKey=your-shared-secret
Portal__Orchestrator__DatabasePath=C:\ETL-SQL\data\etlsql.db
Portal__Storage__Provider=Local
Portal__Storage__KeyRingPath=C:\ETL-SQL\data\.portal-keys
Orchestrator__ApiKey=your-shared-secret
Orchestrator__Database__Provider=Sqlite
Orchestrator__DatabasePath=C:\ETL-SQL\data\etlsql.db
Orchestrator__ScriptRoot=C:\ETL-SQL\scripts
Jobs__UseProcessSpawning=true
Jobs__ExecutablePath=C:\Program Files\ETL-SQL\bin\ETL-SQL.exe
```

Use environment variables or deployment-secret tooling for values that should not be written to disk in plaintext.

## Code Style & Formatting Configuration (`.etlsqlformat.json`)

To enforce consistent SQL formatting styles across user workstations, administrators can place a `.etlsqlformat.json` configuration file in the root of shared script repository directories or VCS workspaces. The ETL-SQL formatter (integrated into the CLI, TUI, and language server) will recursively look up parent directories from the target script file to locate and load this configuration automatically.

For the list of all formatting variables and configuration options (e.g. `keywordCasing`, `commaPlacement`, `formatMetadataTags`), see the query formatting configuration section in [Getting Started](../../guides/onboarding/getting-started.md).

---
