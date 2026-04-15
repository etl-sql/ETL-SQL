# ETL-SQL Administrator's Guide

This guide is for system administrators and DevOps engineers responsible for deploying, configuring, and monitoring the ETL-SQL engine in production environments.

---

## 1. Installation & Deployment

ETL-SQL is a portable .NET application. It requires **.NET 10 Runtime** installed on the host.

### 1.1 Windows Service (NSSM)
To run the background scheduler continuously on Windows, we recommend using [NSSM](https://nssm.cc/):

```powershell
# 1. Install the service
nssm install ETL-SQL-Scheduler "C:\Path\To\ETL-SQL.exe" "ui repl"

# 2. Configure logging and directory
nssm set ETL-SQL-Scheduler AppDirectory "C:\Path\To"
nssm set ETL-SQL-Scheduler AppStdout "C:\Logs\service.log"
nssm set ETL-SQL-Scheduler AppStderr "C:\Logs\service-error.log"

# 3. Start
nssm start ETL-SQL-Scheduler
```

### 1.2 Linux Daemon (systemd)
On Linux, use a systemd unit file:

```ini
# /etc/systemd/system/etlsql.service
[Unit]
Description=ETL-SQL Scheduler Service
After=network.target

[Service]
ExecStart=/opt/etlsql/etl-sql ui repl
WorkingDirectory=/opt/etlsql
Restart=always
User=etluser

[Install]
WantedBy=multi-user.target
```

---

## 2. Configuration & Unified Deployment

ETL-SQL uses a **unified configuration model**. In a standard installation, all host processes share a single `appsettings.json` file located in the application root directory.

| Host | Purpose |
| :--- | :--- |
| **ETL-SQL.App / TUI** | Interactive REPL, batch script runner, and embedded scheduler |
| **ETL-SQL.Orchestrator.Service** | Standalone REST-based job scheduler; used in production |
| **ETL-SQL.ReportPlayer** | Report-SQL dashboard web server |

All hosts support environment variable overrides (standard .NET `DOTNET_*` / section prefix pattern).

---

## 3. Configuration Reference

### 3.1 Security

Applies to: **All Hosts**

| Key | Default | Description |
| :--- | :--- | :--- |
| `Security:AllowedHosts` | `["*"]` | Whitelist of network hosts scripts may connect to. `["*"]` = unrestricted. Remove `*` and list explicit hosts to enable strict egress control (see [SECURITY.md](../SECURITY.md)). |
| `Security:ApprovedSafeZones` | `[]` | Absolute paths of directories where `SET ALLOW_...` script overrides are honored. Scripts outside these paths cannot bypass runaway guards. |
| `Security:MaxFileOperationsPerScript` | `100` | Maximum number of filesystem operations a single script may perform before the engine halts with a `SecurityException`. |
| `Security:MaxRecursiveNestingDepth` | `5` | Maximum `RUN SCRIPT` / procedural nesting depth before halting. |

> [!NOTE]
> `Security:AllowedEnvVars` (the allow-list for the `ENV()` function) is not currently wired to `appsettings.json`. It must be configured programmatically in the DI setup (`SecurityService.AllowedEnvVars`). By default the set is empty, which means all `ENV()` calls are blocked.

**Example — hardened production configuration:**
```json
{
  "Security": {
    "AllowedHosts": ["sql-prod.internal.corp", "*.azure.com"],
    "ApprovedSafeZones": ["D:\\ETL\\scripts\\approved"],
    "MaxFileOperationsPerScript": 500,
    "MaxRecursiveNestingDepth": 10
  }
}
```

---

### 3.2 Engine Tuning

Applies to: **All Hosts**

| Key | Default | Description |
| :--- | :--- | :--- |
| `Engine:BatchSize` | `10000` | Rows per batch during streaming evaluation. Lower this on memory-constrained hosts. |
| `Engine:MaxRecursiveDepth` | `10000` | Maximum call stack depth for nested procedures and `RUN SCRIPT`. |
| `Engine:JoinSpillThreshold` | `100000` | Row count at which a join operation spills to disk instead of holding all data in RAM. |
| `Engine:WindowSpillThreshold` | `100000` | Row count at which window function processing spills to disk. |
| `Engine:ExternalHashPartitions` | `32` | Number of disk partitions used for spilled joins and aggregates. Increase if spill files become very large. |
| `Engine:ExternalSort:ChunkSize` | `100000` | Rows per chunk in the external sort engine. |
| `Engine:MaxMessages` | `1000` | Maximum number of print/log messages held in a session's message buffer. |

---

### 3.3 Orchestration & Concurrency

Applies to: **All Hosts**

| Key | Default | Description |
| :--- | :--- | :--- |
| `Orchestration:MaxInMemoryBatches` | `100` | Maximum number of data batches held in RAM before `#temp` tables begin spilling to disk. |
| `Orchestration:ForeachPageSize` | `10000` | Rows fetched per page in remote `FOREACH` pagination loops. |
| `Orchestration:JobThrottle:MaxConcurrentJobs` | `0` | Maximum simultaneous background jobs. `0` = auto (`ProcessorCount / 2`, minimum 1). |

---

### 3.4 Orchestrator Service (Standalone)

Applies to: **ETL-SQL.Orchestrator.Service only**

The standalone Orchestrator runs as an independent HTTP service. Its settings live under a `Jobs` section (not `Orchestration`).

| Key | Default | Description |
| :--- | :--- | :--- |
| `Urls` | `http://localhost:5100` | The address the Orchestrator REST API listens on. Change to bind to a specific interface or port in production. |
| `Jobs:UseProcessSpawning` | `false` | `false` = run jobs in-process (simpler, dev/test). `true` = spawn `ETL-SQL.exe run` as isolated child processes (recommended for production — memory isolation, killable per-job). |
| `Jobs:ExecutablePath` | `""` | Path to `ETL-SQL.exe`. Required when `UseProcessSpawning` is `true` and the executable is not on `PATH`. Auto-detected when empty. |
| `Jobs:TimeoutSeconds` | `3600` | Wall-clock timeout for a single spawned job. The child process is killed if it exceeds this. No effect in in-process mode. |
| `Jobs:MaxConcurrentJobs` | `0` | Maximum simultaneous jobs. `0` = auto (`ProcessorCount / 2`, minimum 1). |

**Orchestrator metrics** are logged every 60 seconds (hardcoded) to the app log: `ActiveJobs`, `QueuedJobs`, `AvailableSlots`. This is not configurable.

**Example — production process-spawning config:**
```json
{
  "Urls": "http://0.0.0.0:5100",
  "Jobs": {
    "UseProcessSpawning": true,
    "ExecutablePath": "/opt/etlsql/ETL-SQL",
    "TimeoutSeconds": 7200,
    "MaxConcurrentJobs": 4
  }
}
```

---

### 3.5 Reporting Dashboard

Applies to: **ETL-SQL.ReportPlayer only**

| Key | Default | Description |
| :--- | :--- | :--- |
| `ReportPlayer:Port` | `5200` | Port the Report-SQL web dashboard listens on. |

---

### 3.6 Connector Defaults

Applies to: **App / TUI**

These settings provide default credentials for connectors that don't receive inline credentials from a script. In production, replace the placeholder values and use `ENC:` encrypted strings where possible.

| Key | Default | Description |
| :--- | :--- | :--- |
| `Connectors:Retry:MaxAttempts` | `3` | Maximum retry attempts for transient SQL/network errors. |
| `Connectors:Retry:BaseDelaySeconds` | `1.0` | Base delay in seconds for exponential backoff between retries. |
| `Connectors:Ftp:Host` | `localhost` | Default FTP server hostname. |
| `Connectors:Ftp:Username` | `anonymous` | Default FTP username. |
| `Connectors:Ftp:Password` | `""` | Default FTP password. |
| `Connectors:Sftp:Host` | `localhost` | Default SFTP server hostname. |
| `Connectors:Sftp:Username` | `user` | Default SFTP username. |
| `Connectors:Sftp:Password` | `pass` | Default SFTP password. **Change this in production.** |
| `Connectors:AzureBlob:ConnectionString` | `UseDevelopmentStorage=true` | Azure Blob Storage connection string. `UseDevelopmentStorage=true` targets the local Azurite emulator. |
| `Connectors:AzureBlob:Container` | `test` | Default Azure Blob container name. |

---

### 3.7 Logging & Retention

Applies to: **App / TUI** (the Orchestrator.Service uses the same keys under `Logging:AppLog`)

| Key | Default | Description |
| :--- | :--- | :--- |
| `Logging:AppLog:Directory` | `logs/app` | Directory for internal engine diagnostic logs (relative to the executable). |
| `Logging:AppLog:RetentionDays` | `30` | Days to retain app log files before auto-deletion. |
| `Logging:AppLog:FileSizeLimitMb` | `10` | Maximum size of a single app log file before rotation (MB). |
| `Logging:ScriptLog:Directory` | `logs/scripts` | Directory for per-script execution logs. One log file is created per script name. |
| `Logging:ScriptLog:DefaultRetentionDays` | `30` | Days to retain script log files. |
| `Logging:ScriptLog:FileSizeLimitMb` | `10` | Maximum size of a single script log file before rotation (MB). |
| `Logging:LogLevel:Default` | `Information` | Minimum severity level for the app log. Valid values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`. |
| `Logging:LogLevel:Microsoft` | `Warning` | Minimum level for Microsoft framework noise. |

---

### 3.8 Session Management

Applies to: **App / TUI**

| Key | Default | Description |
| :--- | :--- | :--- |
| `Session:StaleSessionRetentionDays` | `7` | Days to keep inactive session state (`.etlsession` files) before the engine reaps them on next startup. |

## 4. Complete Master `appsettings.json`
 
 Use this as a reference when you need to reset to defaults or create a new installation. In a unified deployment, this single file controls all components.
 
 ```json
 {
   "Urls": "http://localhost:5100",
   "AllowedHosts": "*",
   "Logging": {
     "LogLevel": {
       "Default": "Information",
       "Microsoft": "Warning",
       "Microsoft.Hosting.Lifetime": "Information",
       "Microsoft.AspNetCore": "Warning"
     },
     "AppLog": {
       "Directory": "logs/app",
       "RetentionDays": 30,
       "FileSizeLimitMb": 10
     },
     "ScriptLog": {
       "Directory": "logs/scripts",
       "DefaultRetentionDays": 30,
       "FileSizeLimitMb": 10
     },
     "TestLog": {
       "Directory": "logs/tests",
       "RetentionDays": 30,
       "FileSizeLimitMb": 50
     }
   },
   "Security": {
     "AllowedHosts": ["*"],
     "ApprovedSafeZones": [],
     "MaxFileOperationsPerScript": 100,
     "MaxRecursiveNestingDepth": 5
   },
   "Engine": {
     "BatchSize": 10000,
     "MaxRecursiveDepth": 10000,
     "JoinSpillThreshold": 100000,
     "WindowSpillThreshold": 100000,
     "ExternalHashPartitions": 32,
     "ExternalSort": {
       "ChunkSize": 100000
     },
     "MaxMessages": 1000
   },
   "Orchestration": {
     "MaxInMemoryBatches": 100,
     "ForeachPageSize": 10000,
     "JobThrottle": {
       "MaxConcurrentJobs": 0
     }
   },
   "Scheduler": {
     "MetricsIntervalSeconds": 60,
     "SleepIntervalSeconds": 30
   },
   "Jobs": {
     "UseProcessSpawning": false,
     "ExecutablePath": "",
     "TimeoutSeconds": 3600,
     "MaxConcurrentJobs": 0
   },
   "ReportPlayer": {
     "Port": 5200
   },
   "Session": {
     "StaleSessionRetentionDays": 7
   },
   "Connectors": {
     "Retry": {
       "MaxAttempts": 3,
       "BaseDelaySeconds": 1.0
     },
     "Ftp": { "Host": "localhost", "Username": "anonymous", "Password": "" },
     "Sftp": { "Host": "localhost", "Username": "user", "Password": "pass" },
     "AzureBlob": { "ConnectionString": "UseDevelopmentStorage=true", "Container": "test" }
   }
 }
 ```

---

## 5. Resource Governance

### 5.1 Memory Management
ETL-SQL uses an **aggregate streaming model**. While it can process terabytes of data, it only holds a "window" of records in memory (`MaxInMemoryBatches`).
- **High Memory Usage**: Reduce `Engine:BatchSize` or `Orchestration:MaxInMemoryBatches`. Lower values increase disk I/O but reduce peak RAM.
- **Disk Pressure**: Large `#temp` tables spill to the local temp directory. Ensure your `TEMP` directory has sufficient IOPS and free space.

### 5.2 Concurrency Tuning
The default job concurrency (`MaxConcurrentJobs: 0`) auto-selects `ProcessorCount / 2`. For I/O-heavy ETL workloads that spend most of their time waiting on databases or APIs, you can safely set this to 2–4× the CPU count.

### 5.3 Security Guardrails
The engine enforces **Runaway Protection**. If a script exceeds `MaxFileOperationsPerScript`, it halts with a `SecurityException`.

Users can bypass this by adding a `SET ALLOW_GREATER_THAN_n_FILE ON;` statement, but **only if the script is executing within an Approved Safe Zone** configured in `appsettings.json`.

Any authorized bypass is logged as an **Audit Warning**. Administrators can view active safe zones at runtime:
```sql
SHOW SAFE ZONES;
```

### 5.4 Performance Monitoring
- **Orchestrator Heartbeats**: Every 60 seconds, the scheduler logs `ActiveJobs`, `QueuedJobs`, `AvailableSlots`, and `MaxConcurrent` to the app log.
- **Per-Job Metrics**: Every job completion records `PeakRAM` and `CPUTime`.
- **Historical Audit**: Run `SHOW JOB HISTORY;` in the TUI to see resource consumption across past executions.

### 5.5 Dynamic Overrides & Hierarchy

ETL-SQL supports a three-tier configuration hierarchy, allowing administrators to set global boundaries while giving script authors the flexibility to optimize for specific workloads.

| Level | Mechanism | Scope | Change Frequency |
| :--- | :--- | :--- | :--- |
| **Global** | `appsettings.json` | System-wide (all scripts) | Deployment-time only |
| **Environment** | `CREATE SETS` / `USE SETS` | Cross-script (Project/Env) | Weekly / Environmental |
| **Session** | `SET <KNOB> = <Value>` | Current Script Session | Per-execution |

#### When to Override Globally vs Sessionally
- **Globally**: Lower `BatchSize` if your server typically runs many concurrent small jobs and you want to prevent overall RAM exhaustion.
- **Sessionally**: Increase `JOIN_SPILL_THRESHOLD` or `EXTERNAL_HASH_PARTITIONS` in a specific script that you know processes a massive outlier dataset (petabytes) where the default spilling strategy might be too eager or create too few partitions.

```sql
-- Example: Overriding global defaults for a session-critical massive join
SET BATCHSIZE = 25000;
SET JOIN_SPILL_THRESHOLD = 500000;
SET EXTERNAL_HASH_PARTITIONS = 128;
```

---

## 6. Troubleshooting

### 6.1 Log Locations (Defaults)

| Host | Log Directory |
| :--- | :--- |
| App / TUI — engine diagnostics | `logs/app/` |
| App / TUI — per-script execution | `logs/scripts/` |
| Orchestrator.Service | `logs/orchestrator/` |

### 6.2 Common Errors

| Error | Likely Cause | Fix |
| :--- | :--- | :--- |
| `SecurityException: Unauthorized access to protected system directory` | Script is reading a blocked path | Review script paths; add an Approved Safe Zone if the path is legitimate |
| `SecurityException: File operation count exceeds safety limit of N` | Script is processing too many files | Add `SET ALLOW_GREATER_THAN_N_FILE ON;` to the script **and** register an Approved Safe Zone |
| `SecurityException: Connection to host 'X' is denied` | `AllowedHosts` is in strict mode and host is not listed | Add the host to `Security:AllowedHosts` |
| `SecurityException: Access to environment variable 'X' is denied` | `AllowedEnvVars` does not include that variable | Add the variable name to `SecurityService.AllowedEnvVars` in DI setup |
| `Could not locate ETL-SQL executable` | `Jobs:UseProcessSpawning` is `true` but `Jobs:ExecutablePath` is not set | Set `Jobs:ExecutablePath` to the absolute path of `ETL-SQL.exe` |
| High memory / OOM | `MaxInMemoryBatches` or `BatchSize` too high for available RAM | Reduce `Orchestration:MaxInMemoryBatches` and/or `Engine:BatchSize` |
