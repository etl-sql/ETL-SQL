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

## 2. Configuration Reference (`appsettings.json`)

Core engine behavior is controlled via `appsettings.json` located in the same directory as the executable.

### 2.1 Security Settings
| Key | Default | Description |
| :--- | :--- | :--- |
| `Security:MaxRecursiveNestingDepth` | `5` | Maximum depth for `RUN SCRIPT` recursion or procedural nesting. |
| `Security:ApprovedSafeZones` | `[]` | List of directory paths (absolute) where runaway protection (e.g. file operation counts) can be overridden via `SET ALLOW_...` statements. |
| `Logging:Security:AuditLevel` | `Warning` | Minimum level for security-related logging (overrides, blocks). |

### 2.2 Orchestration & Performance
| Key | Default | Description |
| :--- | :--- | :--- |
| `Orchestration:MaxInMemoryBatches` | `10` | Number of data batches held in RAM before spilling `#temp` tables to disk. |
| `Orchestration:ForeachPageSize` | `10000` | Number of rows to fetch per pagination loop for remote `FOREACH` calls. |
| `Orchestration:JobThrottle:MaxConcurrentJobs` | `0` | Max simultaneous background jobs. `0` = auto (CPU count / 2). |

### 2.3 Engine Tuning & Scaling
| Key | Default | Description |
| :--- | :--- | :--- |
| `Engine:BatchSize` | `10000` | Size of row batches used during streaming (CFG-1). Lower for low-RAM containers. |
| `Engine:MaxRecursiveDepth` | `10000` | Maximum stack depth for nested procedure calls and RUN SCRIPT (CFG-3). |
| `Engine:JoinSpillThreshold` | `100000` | Number of rows in a join before spilling to disk (CFG-6). |
| `Engine:ExternalHashPartitions` | `32` | Number of partitions used for disk-spilling joins and aggregates (CFG-5). |
| `Engine:ExternalSort:ChunkSize` | `100000` | Number of rows per sort chunk in ExternalSortEngine (CFG-4). |
| `Session:StaleSessionRetentionDays` | `7` | How many days to keep inactive session state before reaping (CFG-10). |

### 2.4 Connector Resilience
| Key | Default | Description |
| :--- | :--- | :--- |
| `Connectors:Retry:MaxAttempts` | `3` | Max retry attempts for transient SQL errors (CFG-9). |
| `Connectors:Retry:BaseDelaySeconds` | `1.0` | Base delay for exponential backoff during retries. |

### 2.5 Reporting Dashboard

| Key | Default | Description |
| :--- | :--- | :--- |
| `ReportPlayer:Port` | `5200` | The port for the Report-SQL web dashboard. |

### 2.6 Logging & Retention
| Key | Default | Description |
| :--- | :--- | :--- |
| `Logging:AppLog:Directory` | `logs/system` | Location for internal engine diagnostic logs. |
| `Logging:AppLog:RetentionDays` | `30` | How many days to keep system logs. |
| `Logging:AppLog:FileSizeLimitMb` | `50` | Max size of a single system log file before rotation. |
| `Logging:Scheduler:MetricsIntervalSeconds` | `60` | Frequency of active/queued job status heartbeats in the log. |

---

## 3. Resource Governance

### 3.1 Memory Management
ETL-SQL uses an **aggregate streaming model**. While it can process terabytes of data, it only holds a "window" of records in memory (`MaxInMemoryBatches`). 
- **High Memory Usage**: If you see high RAM usage, reduce the `--batch-size` CLI flag or the `MaxInMemoryBatches` config.
- **Disk Pressure**: Large `#temp` tables spill to the local disk. Ensure your `TEMP` directory has sufficient IOPS and capacity.

### 3.2 Safety Guardrails
The engine enforces **Runaway Protection**. If a script exceeds the `MaxFileOperationsPerScript`, it will halt with a `SecurityException`. 
Users can bypass this by adding a `SET` statement to their script (e.g., `SET ALLOW_GREATER_THAN_100_FILE ON;`), but only if the script is running within an **Approved Safe Zone** (configured in `appsettings.json`).

Any bypass attempt within an approved safe zone is logged as an **Audit Warning** in the system logs (SEC-3). 

Administrators can view all active safe zones by running:
```sql
SHOW SAFE ZONES;
```

### 3.3 Performance Monitoring
Administrators can monitor the efficiency of their ETL pipelines using the job history metrics:
- **Scheduler Heartbeats**: Every 60 seconds (configurable), the scheduler logs `ActiveJobs` vs `QueuedJobs`.
- **Resource Audit**: Every job completion records `PeakRAM` and `CPUTime`.
- **Command**: Run `SHOW JOB HISTORY;` in the TUI to see an audit of which scripts are consuming the most memory and processing time.

---

## 4. Troubleshooting

### 4.1 Log Locations
- **Script Logs**: `logs/scripts/` (one file per script name).
- **Scheduler Logs**: `logs/system/` (diagnostics for job firing and persistence).

### 4.2 Error Codes
- **CS0103 / CS1061**: Typically indicates a missing dependency or configuration key in `appsettings.json`.
- **SecurityException**: A script attempted to access a path outside a safe zone or exceeded a safety threshold.
