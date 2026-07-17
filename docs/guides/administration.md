# ETL-SQL Administrator's Guide

This guide is for operators who install, configure, back up, and monitor ETL-SQL in production or
shared test environments. For day-to-day portal administration, see
[Report Portal Administration](report-portal-admin.md). For command-line job operations, see
[Job Orchestration](job-orchestration.md). For Report Portal and Orchestrator server sizing, see
[Capacity Planning](../architecture/decisions/Capacity_Planning.md).

## Topics

- [Installation and Deployment](administration/installation.md) - deployment components, production
  install (Windows, Linux, Docker), and the first-run checklist.
- [Configuration Files](administration/configuration.md) - `appsettings.json` and code-style
  configuration.
- [Security and Secret Management](administration/security.md) - encrypting secrets, the Portal JWT
  and Orchestrator API keys, Governance Core, native admin services, and row-level security.
- [HTTPS and Network Configuration](administration/networking.md) - TLS setup and same-host service
  startup.
- [Portal State, Data Roots, and High Availability](administration/state-and-ha.md) - state stores,
  data roots, and Practical High Availability (including containerized clustering).
- [Resource Controls](administration/resources.md) - lockbox bundles, portal and job execution
  limits, engine defaults, lineage/OpenLineage, and user snippet templates.
- [Backup, Monitoring, and Health](administration/backup-and-monitoring.md) - backups and restore
  drills, operational checks, external monitoring, and `etl-sql doctor`.
- [Operator CLI Commands](administration/operator-cli.md) - onboarding, support bundles,
  backup/restore, in-place upgrades, database migration, and HA soak operations.

## See Also

- [CLI Reference](../reference/cli/README.md) - every `etl-sql` command, generated from the command
  tree (the atomic reference for the operator commands narrated above).
- [Report Portal Administration](report-portal-admin.md) - user, permission, publishing, and
  subscription administration.
- [Job Orchestration](job-orchestration.md) - scheduling, DAGs, and command-line job operations.
