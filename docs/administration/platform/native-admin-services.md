# Native Admin Services

The `samples/admin_operations` scheduler scripts have managed, first-class replacements: three
Portal background services configured under `Portal:AdminServices`, all disabled by default. Each
runs on its own interval with an HA cluster lease (exactly one node runs per interval; restarts do
not re-send), retries delivery up to `MaxAttempts` per run, records every run — sent, skipped, or
failed — in a durable history (pruned per `RunHistoryRetentionDays`, default 90), and audits each
run as `ADMIN_SERVICE_RUN`.

```json
{
  "Portal": {
    "AdminServices": {
      "FailureDigest": {
        "Enabled": true, "IntervalHours": 24, "LookbackHours": 25,
        "Recipients": "ops-team@example.com", "SmtpAlias": "mailer", "AlertOnly": true
      },
      "BackupReport": {
        "Enabled": true, "IntervalHours": 24, "MaxBackupAgeHours": 26,
        "Recipients": "ops-team@example.com", "SmtpAlias": "mailer", "AlertOnly": true
      },
      "CapacityReport": {
        "Enabled": true, "IntervalHours": 24, "LookbackHours": 24,
        "Recipients": "ops-team@example.com", "SmtpAlias": "mailer"
      }
    }
  }
}
```

Migration from the sample scripts:

| Sample script | Native replacement |
| :--- | :--- |
| `daily_failure_digest.etlsql` | `FailureDigest` — failed scheduled jobs (including `INTERRUPTED`), failed/cancelled portal executions, and failed/denied subscription deliveries in the lookback window. |
| `backup_and_report.etlsql` | `BackupReport` — `etl-sql admin backup` now records its outcome automatically (job-state `admin-backup`); the service alerts when the last backup failed, was never recorded, or is older than `MaxBackupAgeHours`. The two-step scheduler wiring is no longer needed. |
| `capacity_report.etlsql` | `CapacityReport` — worst-point per-node disk/memory/CPU from host metrics plus job run/failure counts; always sends when enabled. |

Notifications go through a stored SMTP connection selected by `SmtpAlias` (the credential is
decrypted per send and never leaves the portal). `GET api/admin/services` shows each service's
configuration and last run; `GET api/admin/services/{name}/history` returns the run ledger. The
sample scripts remain as examples for custom workflows, but the supported production path is this
configuration.

Use named references in connector definitions:

```sql
CREATE CONNECTION sales AS MSSQL(
  SERVER = 'sql01',
  DATABASE = 'Sales',
  USER = 'etl_worker',
  PASSWORD = 'SECRET:sales_db_password'
);

CREATE CONNECTION warehouse AS POSTGRES(
  HOST = 'pg01',
  DATABASE = 'dw',
  USER = 'etl',
  PASSWORD = 'SECRET:dw_password'
);
```

Only sensitive connector options and sensitive connection-string fields are expanded (`PASSWORD`, `TOKEN`,
`ACCESS_KEY`, `SECRET_KEY`, `CLIENT_SECRET`, and similar credential fields). A `SECRET:` reference on any
other field — for example `BUCKET` or `HOST` — is rejected with an error rather than passed to the connector
as literal text. Organizations that consider specific metadata sensitive can designate additional fields:

```json
{ "Governance": { "Secrets": { "SensitiveConnectionFields": "HOST, PATH, BUCKET" } } }
```

Use `TYPE:FIELD` to scope a designation to one connector type:

```json
{ "Governance": { "Secrets": { "SensitiveConnectionFields": "SFTP:HOST, S3:BUCKET" } } }
```

Designated fields become `SECRET:`-resolvable and are masked in `SHOW CONNECTION`, diagnostics, and
connection-string rendering — without being treated as secrets in every deployment: unlike credential
fields they may still hold plain values (in scripts or catalog entries), so designating `HOST` does not
force every hostname into the secret store. Shared connection entries can also classify fields per
entry with `--sensitive FIELD` or the Portal Connections admin form; those fields are masked in
catalog detail/export displays and may use `SECRET:name` for that entry. Missing or unreachable
secrets fail closed with an error; ETL-SQL does not silently replace a missing secret with an empty value.
Logs, diagnostics, audit rows, support bundles, result formatting, and portal/orchestrator error surfaces redact
raw secret values and `SECRET:` references before persistence or display.

## Enterprise governance

The enterprise machine-enrollment, policy authority, security-event, and audit-outbox controls that were
previously described here each have their own focused page:

- [Enterprise machine enrollment](enterprise-enrollment.md) — opt-in enrollment, bootstrap storage, and the
  OS-containment boundary (WDAC/AppLocker).
- [Authoritative organization policy](organization-policy.md) — the Portal policy authority, deployment/operator
  runbook, canary rollout, upgrade ordering, outage runbooks, and cache/outbox recovery.
- [Central security events and SIEM delivery](security-events.md) — the versioned security-event contract and
  collector delivery.
- [Durable audit outbox and remote collectors](audit-outbox.md) — `Portal:Audit:*` remote forwarding and
  fail-closed mutation behavior.

## Related

- [Platform administration](README.md)
- [Secrets and Keys](secrets.md)
- [Governance](governance.md)
