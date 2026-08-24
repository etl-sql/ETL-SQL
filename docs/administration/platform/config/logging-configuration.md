# Logging Configuration

> **Applies to:** Solo · Team · Enterprise · SaaS

Configure log levels, output directories, file size limits, and retention for application, script, and test log streams.

ETL-SQL settings can be configured via `appsettings.json`, environment variables, or command-line parameters. When using environment variables, replace colons (`:`) with double underscores (`__`). For example, `Logging:AppLog:Directory` maps to `Logging__AppLog__Directory`.

---

## Log Level Thresholds

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Logging:LogLevel:Default` | string | `Information` | Default log level applied to all categories not matched below. |
| `Logging:LogLevel:Microsoft` | string | `Warning` | Log level threshold for Microsoft libraries. |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning` | Log level threshold for ASP.NET Core framework components. |

Valid values (ascending severity): `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`.

---

## Application Log (`AppLog`)

Captures service startup, shutdown, health, and infrastructure-level events.

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Logging:AppLog:Directory` | string | `logs/app` | Directory where application log files are written. |
| `Logging:AppLog:RetentionDays` | integer | `30` | Days to retain application log files before recycling. |
| `Logging:AppLog:FileSizeLimitMb` | integer | `10` | Maximum size in MB before the log file rolls over. |

---

## Script Execution Log (`ScriptLog`)

Captures per-run output from ETL-SQL script executions.

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Logging:ScriptLog:Directory` | string | `logs/scripts` | Target folder where per-run script logs are saved. |
| `Logging:ScriptLog:DefaultRetentionDays` | integer | `30` | Days to retain script execution log files. |
| `Logging:ScriptLog:FileSizeLimitMb` | integer | `10` | Maximum size in MB of a script log file. |

---

## Test Log (`TestLog`)

Captures output from smoke tests and deployment certification runs.

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Logging:TestLog:Directory` | string | `logs/tests` | Directory where test/smoke log files are archived. |
| `Logging:TestLog:RetentionDays` | integer | `30` | Retention window (days) for test execution logs. |
| `Logging:TestLog:FileSizeLimitMb` | integer | `50` | Maximum size in MB of test log files before rolling over. |

---

## Example: `appsettings.json` Snippet

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
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
  }
}
```

---

## Related

- [Configuration Settings Reference](../appsettings-reference.md) — full config hub
- [Security Configuration](security-configuration.md) — sandbox limits and egress fence
- [Platform Administration](../README.md)
- [Backup, Monitoring, and Health](../backup-and-monitoring.md)
