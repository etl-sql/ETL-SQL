# Logging and Performance Tuning

## 6. Logging

### 6.1 Enable log files

```bash
# Log to the default directory (logs/scripts/)
ETL-SQL run nightly_load.etlsql --log

# Log to a specific directory
ETL-SQL run nightly_load.etlsql --log C:\ETL\Logs\

# Log to a specific file
ETL-SQL run nightly_load.etlsql --log C:\ETL\Logs\nightly-$(date +%Y%m%d).log
```

### 6.2 Log configuration (`appsettings.json`)

Log retention and size limits are controlled in `appsettings.json` next to the executable:

```json
{
  "Logging": {
    "ScriptLog": {
      "Directory": "logs/scripts",
      "DefaultRetentionDays": 30,
      "FileSizeLimitMb": 10
    }
  }
}
```

Log files are named after the script file with a date suffix (e.g., `nightly_load_20260413.log`).

---

## 7. Performance Tuning

### 7.1 Batch size

The `--batch-size` option controls how many rows are buffered in memory at one time. The default of 10,000 is suitable for most workloads. Tune this based on available RAM and row width:

```bash
# Large, wide rows — reduce batch size to avoid memory pressure
ETL-SQL run big_transform.etlsql --batch-size 2000

# Narrow rows with fast I/O — increase for throughput
ETL-SQL run csv_import.etlsql --batch-size 50000
```

### 7.2 Performance metrics

Use `--perf` to get a post-execution breakdown:

```bash
ETL-SQL run nightly_load.etlsql --perf
```

Output includes:
- Lexer / Parser / Execution phase timings (ms)
- Total rows processed
- Throughput (rows/second)
- Approximate RAM peak (MB)
- Disk-spill volume if the aggregate engine overflowed

### 7.3 Per-statement profiling

Use `SET PROFILING ON` inside your script to capture timings at the individual statement level:

```sql
SET PROFILING ON;

SELECT * INTO #staging FROM src.Orders WHERE status = 'Open';
MERGE INTO dest.dbo.Orders AS T USING #staging AS S ON T.Id = S.Id ...;

SELECT * INTO #perf FROM eng.profile;
SELECT * FROM #perf ORDER BY DurationMs DESC;
```

---
