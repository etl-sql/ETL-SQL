# Platform Administration

Install, configure, secure, scale, back up, and monitor the ETL-SQL platform and services.

## Pages

- [Backup, Monitoring, and Health](backup-and-monitoring.md) - | Database | Typical path | Backup guidance |
- [Configuration Files](configuration.md) - The published services read `appsettings.json`, environment variables, and encrypted configuration values. Production templates live beside the service projects:
- [Governance Core](governance.md) - Governance Core centralizes three production controls:
- [Installation and Deployment](installation.md) - ETL-SQL can be deployed as workstation tooling, server services, or both.
- [Native Admin Services](native-admin-services.md) - The `samples/admin_operations` scheduler scripts have managed, first-class replacements: three
- [HTTPS and Network Configuration](networking.md) - Both the Orchestrator and Portal use Kestrel. The production templates define these defaults:
- [Operator CLI Commands](operator-cli.md) - These commands replace manual operator runbooks with supported, repeatable CLI workflows.
- [Resource Controls](resources.md) - Use resource settings to keep one report or job from consuming the whole host.
- [Row-Level Security](row-level-security.md) - Folder and dataset permissions control **which reports a user can open** — the coarse-grained gate.
- [Secrets and Keys](secrets.md) - ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate passwords, and connection strings. Encrypted values use the `ENC:` prefix.
- [Portal State, Data Roots, and High Availability](state-and-ha.md) - The Portal constrains filesystem access to configured roots. Set these to service-owned directories rather than broad user folders:
- [Configuration Settings Reference](settings.md) - Canonical reference for all `appsettings.json` keys, environment variable mappings, and ad-hoc `SET` overrides.

## See Also

- [Administration](../README.md) - the full admin area.
- [Platform Administration](../platform/README.md) · [Portal Administration](../portal/README.md) · [Orchestration](../orchestration/README.md)
- [CLI Reference](../../reference/cli/README.md) - every `etl-sql` command.
