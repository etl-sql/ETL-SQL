# PLATFORM Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Configuration Settings Reference](appsettings-reference.md) | Hub index for all `appsettings.json` options. Links to focused per-area config pages under `config/`. |
| &nbsp;&nbsp;↳ [Logging Configuration](config/logging-configuration.md) | `Logging:*` keys — log levels, AppLog, ScriptLog, TestLog settings. |
| &nbsp;&nbsp;↳ [Security Configuration](config/security-configuration.md) | `Security:*` keys — path protection, egress fence, execution limits, spill encryption. |
| &nbsp;&nbsp;↳ [Engine Configuration](config/engine-configuration.md) | `Engine:*` keys — batch size, memory governor, spill thresholds, resource ceilings. |
| &nbsp;&nbsp;↳ [Orchestrator Configuration](config/orchestrator-configuration.md) | `Orchestrator:*`, `Orchestration:*`, `Scheduler:*`, `Jobs:*` — job scheduling, concurrency, sandbox. |
| &nbsp;&nbsp;↳ [Portal Configuration](config/portal-configuration.md) | `Portal:*`, `ReportPlayer:*`, `Session:*`, `Connectors:*`, `Lineage:*` — Portal database, modules, JWT, identity providers. |
| [Durable Audit Outbox and Remote Collectors](audit-outbox.md) | Portal audit rows are written with a durable outbox row in the same database transaction. Configure remote |
| [Backup, Monitoring, and Health](backup-and-monitoring.md) | Backing up ETL-SQL state, proving the backup restores, and wiring health and failure signals into your own monitoring. |
| [Configuration Files](config-file-locations.md) | The published services read `appsettings.json`, environment variables, and encrypted configuration values. Production templates live beside the ser... |
| [Deployment-profile certification](deployment-profile-certification.md) | Deployment-profile certification composes focused test suites into operator-readable proof for the |
| [Production Canaries](production-canaries.md) | Hosted SLOs, synthetic isolation, journey coverage, alert attribution, credential rotation, and fault-drill evidence. |
| [Deployment promotion](deployment-promotion.md) | Deployment promotion starts with a read-only inventory. The preflight separates portable scripts |
| [Enterprise Machine Enrollment](enterprise-enrollment.md) | Enterprise policy is opt-in. When no machine enrollment exists, ETL-SQL remains in standalone mode: |
| [Governance Core](governance.md) | Governance Core centralizes three production controls: |
| [Installation and Deployment](installation.md) | Installing ETL-SQL as workstation tooling, as managed services, or as a multi-node cluster. |
| [Native Admin Services](native-admin-services.md) | The `samples/admin_operations` scheduler scripts have managed, first-class replacements: three |
| [HTTPS and Network Configuration](networking.md) | HTTPS, reverse proxies, ports, and the network boundaries each service expects. |
| [Operator CLI Commands](operator-cli.md) | These commands replace manual operator runbooks with supported, repeatable CLI workflows. |
| [Authoritative Organization Policy](organization-policy.md) | Hub for signed, centrally published organization policy for enrolled machines. |
| &nbsp;&nbsp;↳ [Policy Schema Specification](policy-schema-specification.md) | Policy envelope JSON format, signature input, verification rules, payload schema, and metadata required-tag enforcement. |
| &nbsp;&nbsp;↳ [Policy Signing and Verification](policy-signing-and-verification.md) | Certificate config, signing key rotation, machine registration, service identity least privilege, canary rollout. |
| &nbsp;&nbsp;↳ [Policy Enforcement Gates](policy-enforcement-gates.md) | Deployment runbook, staged/emergency publication, enterprise upgrade ordering, outage runbooks, cache/outbox recovery. |
| [Deployment profile transitions](profile-transitions.md) | ETL-SQL promotions preserve source-controlled pipeline and report logic while changing target |
| [Resource Controls](resources.md) | Use resource settings to keep one report or job from consuming the whole host. |
| [Row-Level Security](row-level-security.md) | Folder and dataset permissions control **which reports a user can open** — the coarse-grained gate. |
| [SaaS Operator Best Practices & FAQ](saas-operations-faq.md) | Practical operating patterns, tenant isolation blueprints, and FAQs for hosting multi-tenant SME fleets. |
| [Secrets and Keys](secrets.md) | ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate |
| [Secure Outbound Data Gateway](secure-outbound-gateway.md) | Reach private databases, file roots, and APIs without inbound firewall exceptions, through an outbound-connected tenant-attested policy enforcement point. |
| [Central Security Events and SIEM Delivery](security-events.md) | Security events are separate from diagnostic logs and governance audit records: a dedicated versioned contract with a durable local outbox and opti... |
| [Portal State, Data Roots, and High Availability](state-and-ha.md) | Where the Portal keeps its state, which directories it is allowed to touch, and what a multi-node high-availability deployment requires. |
| [Tenant Portability Signing Keys](tenant-portability-signing-keys.md) | Tenant portability manifests are signed by the exporting operator with an OpenPGP signing key. |
