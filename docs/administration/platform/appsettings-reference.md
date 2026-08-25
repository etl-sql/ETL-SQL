# Configuration Settings Reference

> **Applies to:** Solo · Team · Enterprise · SaaS

The canonical index for all `appsettings.json` configuration options in ETL-SQL. This hub links to focused per-area reference pages.

ETL-SQL settings can be configured via `appsettings.json`, environment variables, or command-line parameters. When using environment variables, replace colons (`:`) with double underscores (`__`). For example, `Security:PathProtectionMode` maps to the environment variable `Security__PathProtectionMode`.

Many settings can also be overridden ad-hoc for a single script session using the SQL-style `SET` command.

---

## Configuration Reference Pages

| Reference Page | Keys Covered |
| :--- | :--- |
| [Logging Configuration](config/logging-configuration.md) | `Logging:LogLevel:*`, `Logging:AppLog:*`, `Logging:ScriptLog:*`, `Logging:TestLog:*` |
| [Security Configuration](config/security-configuration.md) | `Security:PathProtectionMode`, `Security:ApprovedSafeZones`, `Security:DeniedEgressRanges`, `Security:MaxFileOperationsPerScript`, `Security:SpillEncryption*`, and infrastructure egress fence |
| [Engine Configuration](config/engine-configuration.md) | `Engine:BatchSize`, `Engine:TotalMemoryGrantMB`, `Engine:MemoryGovernorPolicy`, spill thresholds, resource ceilings, execution policy controls, observability, and `Reporting:Default*` report formatting |
| [Orchestrator Configuration](config/orchestrator-configuration.md) | `Orchestrator:*`, `Orchestration:JobThrottle:*`, `Orchestration:SandboxAdmission:*`, `Orchestration:SandboxExecution:*`, `Orchestration:ResourceManagement:*`, `Scheduler:*`, `Jobs:*` |
| [Portal Configuration](config/portal-configuration.md) | `Portal:*`, `ReportPlayer:*`, `Session:*`, `Connectors:*`, `Lineage:*`, `Snippets:*`, `ConnectionStrings:*` — including database, storage, modules, Studio, JWT, rate limiting, dataset cryptography, and identity providers |

---

## Quick Reference: Ad-Hoc `SET` Commands

Scripts can override many engine settings for the duration of a single session. For example:

```sql
SET BATCHSIZE = 5000;
SET MAX_PARALLEL_DEGREE = 8;
SET SPILL_ENCRYPTION OFF;
SET LINEAGE = OFF;
```

The `SET` column in each configuration table shows the corresponding command for keys that support per-session overrides.

---

## Environment Variable Override Format

Replace every `:` in a key path with `__`:

| appsettings.json key | Environment variable |
| :--- | :--- |
| `Security:PathProtectionMode` | `Security__PathProtectionMode` |
| `Portal:Database:Provider` | `Portal__Database__Provider` |
| `Orchestrator:ApiKey` | `Orchestrator__ApiKey` |

---

## References

- [Platform Administration](README.md)
- [Portal Administration](../portal/README.md)
- [Orchestration](../orchestration/README.md)
- [SET Commands Reference](../../reference/set-commands/README.md)
