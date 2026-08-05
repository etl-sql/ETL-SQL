# PLATFORM Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Configuration Settings Reference](appsettings-reference.md) | This document is the canonical reference for all configuration options available in `appsettings.json`. |
| [Durable Audit Outbox and Remote Collectors](audit-outbox.md) | Portal audit rows are written with a durable outbox row in the same database transaction. Configure remote |
| [Backup, Monitoring, and Health](backup-and-monitoring.md) | Backing up ETL-SQL state, proving the backup restores, and wiring health and failure signals into your own monitoring. |
| [Configuration Files](config-file-locations.md) | The published services read `appsettings.json`, environment variables, and encrypted configuration values. Production templates live beside the ser... |
| [Deployment-profile certification](deployment-profile-certification.md) | Deployment-profile certification composes focused test suites into operator-readable proof for the |
| [Deployment promotion](deployment-promotion.md) | Deployment promotion starts with a read-only inventory. The preflight separates portable scripts |
| [Enterprise Machine Enrollment](enterprise-enrollment.md) | Enterprise policy is opt-in. When no machine enrollment exists, ETL-SQL remains in standalone mode: |
| [Governance Core](governance.md) | Governance Core centralizes three production controls: |
| [Installation and Deployment](installation.md) | Installing ETL-SQL as workstation tooling, as managed services, or as a multi-node cluster. |
| [Native Admin Services](native-admin-services.md) | The `samples/admin_operations` scheduler scripts have managed, first-class replacements: three |
| [HTTPS and Network Configuration](networking.md) | HTTPS, reverse proxies, ports, and the network boundaries each service expects. |
| [Operator CLI Commands](operator-cli.md) | These commands replace manual operator runbooks with supported, repeatable CLI workflows. |
| [Authoritative Organization Policy](organization-policy.md) | Signed, centrally published organization policy for enrolled machines — the Enterprise counterpart to the source-controlled workspace policy. |
| [Deployment profile transitions](profile-transitions.md) | ETL-SQL promotions preserve source-controlled pipeline and report logic while changing target |
| [Resource Controls](resources.md) | Use resource settings to keep one report or job from consuming the whole host. |
| [Row-Level Security](row-level-security.md) | Folder and dataset permissions control **which reports a user can open** — the coarse-grained gate. |
| [Secrets and Keys](secrets.md) | ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate |
| [Central Security Events and SIEM Delivery](security-events.md) | Security events are separate from diagnostic logs and governance audit records: a dedicated versioned contract with a durable local outbox and opti... |
| [Portal State, Data Roots, and High Availability](state-and-ha.md) | Where the Portal keeps its state, which directories it is allowed to touch, and what a multi-node high-availability deployment requires. |
