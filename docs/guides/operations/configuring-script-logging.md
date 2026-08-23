# Configuring Script and Engine Logging

ETL-SQL provides structured execution logging for command-line runs, automated schedules, and background worker services. Logs record statement execution timings, row counts, warning aggregates, and error traces while automatically redacting sensitive credentials and secrets.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Enabling Logs via the CLI

Use the `--log` flag with `etl-sql run`:

```bash
# 1. Log to the default directory (logs/scripts/script_YYYYMMDD.log)
etl-sql run nightly_etl.etlsql --log

# 2. Log to a custom directory
etl-sql run nightly_etl.etlsql --log C:\ETL\Logs\

# 3. Log to an explicit file path
etl-sql run nightly_etl.etlsql --log /var/log/etl/nightly-custom.log
```

---

## Log Retention Configuration (`appsettings.json`)

Configure global log rotation, maximum file size limits, and retention periods in `appsettings.json`:

```json
{
  "Logging": {
    "ScriptLog": {
      "Directory": "logs/scripts",
      "DefaultRetentionDays": 30,
      "FileSizeLimitMb": 10,
      "LogLevel": "Information"
    }
  }
}
```

- **`Directory`**: The default filesystem root where log files are stored.
- **`DefaultRetentionDays`**: Automatically purges log files older than the specified age.
- **`FileSizeLimitMb`**: Splits log files when size exceeds the threshold.

---

## Automatic Credential Redaction

The logging subsystem enforces zero-trust redaction rules across all output targets:
- Password parameters and API tokens are never written to log files.
- `SECRET:name` references and encrypted strings (`ENC:...`) remain redacted.
- Diagnostic data-quality sample values for `@pii`-tagged columns are automatically masked.

---

## Related Topics

- [Tuning Pipeline Performance](tuning-pipeline-performance.md) — Profiling and memory management.
- [Automating Quality Gates](../data-quality/automating-quality-gates.md) — CI/CD execution and evidence.
- [Platform Administration](../../administration/platform/README.md) — Enterprise service configuration.
