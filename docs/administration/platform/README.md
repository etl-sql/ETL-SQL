# PLATFORM Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Configuration Settings Reference](appsettings-reference.md) | This document is the canonical reference for all configuration options available in `appsettings.json`. |
| [Durable Audit Outbox and Remote Collectors](audit-outbox.md) | Portal audit rows are written with a durable outbox row in the same database transaction. Configure remote |
| [Backup, Monitoring, and Health](backup-and-monitoring.md) | ## 8. Backup & Maintenance |
| [Configuration Files](config-file-locations.md) | ## 3. Configuration Files |
| [Enterprise Machine Enrollment](enterprise-enrollment.md) | Enterprise policy is opt-in. When no machine enrollment exists, ETL-SQL remains in standalone mode: |
| [Governance Core](governance.md) | ### 4.4 Governance Core |
| [Installation and Deployment](installation.md) | ## 1. Deployment Components |
| [Native Admin Services](native-admin-services.md) | The `samples/admin_operations` scheduler scripts have managed, first-class replacements: three |
| [HTTPS and Network Configuration](networking.md) | ## 5. HTTPS & Network Configuration |
| [Operator CLI Commands](operator-cli.md) | ## 11. Operator CLI Commands |
| [Authoritative Organization Policy](organization-policy.md) | The Portal policy authority signs published envelopes with an RSA certificate whose private key remains |
| [Resource Controls](resources.md) | ## 7. Resource Controls |
| [Row-Level Security](row-level-security.md) | ### 4.x Row-Level Security (report data filtering) |
| [Secrets and Keys](secrets.md) | ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate passwords, and connection strings. Encrypted values use t... |
| [Central Security Events and SIEM Delivery](security-events.md) | Security events are separate from diagnostic logs and transactional governance audit records. They carry the |
| [Portal State, Data Roots, and High Availability](state-and-ha.md) | ## 6. Portal State and Data Roots |

## Stewardship Catalog Posture

ETL-SQL treats stewardship metadata as script-first lineage metadata. Administrators should ask
publishers to define durable ownership and classification in `.etlsql` / `.rptsql` tags, then use
the durable lineage catalog to find gaps:

```sql
SHOW LINEAGE HISTORY FOR MISSING TAGS AT prod_orch LIMIT 500 INTO #missing_stewardship;
```

The required Phase 1 stewardship set is `@owner`, `@steward`, `@contact`, `@classification`, and
`@quality`. The governed catalog also type-checks privacy and quality tags (`@pii`, `@phi`, `@pci`,
`@sensitive`, `@classification`, `@quality`, `@freshness`, and related source tags). Organization
extensions should use `org_`, `x_`, or `custom_` prefixes so they remain clearly local and do not
collide with future standard tags.

The Portal Lineage view also has a Stewardship mode for operational review. It exposes searchable
lineage/tag inventory, sensitive or restricted assets, missing stewardship metadata, stale lineage,
and per-steward queues. Stale lineage uses `@freshness` metadata when present; otherwise the Portal
query applies a configurable stale-after-days window.
