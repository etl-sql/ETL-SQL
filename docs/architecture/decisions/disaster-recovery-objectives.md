# Disaster Recovery Objectives

This guide defines supported RPO/RTO targets, recovery-set contents, restore-drill expectations, and
clone-safety rules for ETL-SQL deployments. It complements the HA topology guide; HA keeps service
available during common node failures, while disaster recovery proves the environment can be rebuilt
from backup after data, site, credential, or operator failure.

## RPO and RTO Targets

| Topology | Supported RPO | Supported RTO | Recovery set |
| :--- | :--- | :--- | :--- |
| Standalone | Last successful `etl-sql admin backup` or host snapshot | 2 hours after backup artifacts and host are available | Portal SQLite DB, Orchestrator SQLite DB, scripts, snapshots, datasets, maps, Data Protection key ring, app config, dataset/JWT/SMTP/orchestrator secrets |
| Departmental | Last scheduled backup, normally 24 hours or less | 4 hours after server/storage access is restored | Same as standalone; include service account inventory, SMTP/connector credentials, audit/security outbox, and service supervisor configuration |
| High Availability | PostgreSQL PITR or managed snapshot point plus coordinated artifact snapshot | 4 hours for single-region restore; 8 hours for alternate-site restore unless infrastructure policy states otherwise | Portal PostgreSQL, Orchestrator PostgreSQL, shared artifact roots, Data Protection key ring, policy-authority certificate identity, Portal secrets, audit/security outboxes, load-balancer/DNS/certificate configuration |

The achieved RPO for a restore is the elapsed time between the backup's `createdUtc` and the recovery
report's `generatedUtc`. The achieved RTO is operator-measured unless the restore is executed inside a
timed automation harness; the recovery report leaves `achievedRtoSeconds` null when no timer is
available.

## State Included in Recovery

Every recovery plan must identify ownership for:

- Portal catalog state: users, roles, groups, reports, folders, datasets, subscriptions, service
  accounts, refresh tokens, audit logs, audit outbox, security-event outbox, policy authority, shared
  connections, and Portal secret metadata.
- Orchestrator state: job definitions, leases, bundles, lineage catalog, job history, node registry,
  cluster locks, and admin-service job-state markers.
- Artifacts: report scripts, compiled snapshots, dataset cache files, maps, Data Protection key ring,
  generated support evidence, and HA soak/certification evidence.
- Keys and credentials: dataset at-rest keys and previous keys, JWT current and previous secrets,
  Orchestrator API keys, SMTP credentials, Portal secret-store key ring, policy-authority signing
  certificate/private key in the OS store or HSM, machine-enrollment client certificates, and external
  connector credentials.
- External dependencies: PostgreSQL, load balancers, object/file storage, DNS, TLS certificates,
  identity providers, LDAP/OIDC endpoints, SMTP, audit/security collectors, secret vaults, and any
  source/target systems required by scheduled jobs.

## Scheduled Restore Drills

Run a restore drill at least quarterly for production and before major upgrades. The drill should:

1. Restore into a clean directory or isolated environment; never overwrite the only known-good
   production state during a drill.
2. Run `etl-sql admin restore --validate --from <data.zip> --keys <keys.zip> --report recovery-report.json`
   for split-custody archives, or the equivalent PostgreSQL/artifact validation for HA backups.
3. Start Portal and Orchestrator against the restored state only after environment-specific endpoints
   and credentials have been reviewed.
4. Verify `/healthz`, `/health`, `/metrics`, admin login, service-account token minting, dataset cache
   reads, policy-authority status, audit outbox delivery, security-event continuity, and Orchestrator
   scheduled-job recovery.
5. Run `POST api/admin/secrets/verify-all` when using the Portal secret store so the restored key ring
   proves it can decrypt every stored secret without printing values.
6. Record the generated recovery report, operator transcript, and any corrected actions in the change
   or incident record.

The restore drill is failed if a referenced artifact is missing, key versions cannot decrypt existing
data, policy enrollment is unavailable for enrolled hosts, audit/security delivery would point to the
wrong environment, or scheduled jobs cannot be explained after recovery.

## Machine-Readable Recovery Report

`etl-sql admin restore` supports `--report <path>` in both validation and restore mode. The JSON report
contains:

- `schemaVersion`, `generatedUtc`, `operation`, and `status`.
- `backupId`, `backupCreatedUtc`, `appVersion`, `catalogMigration`, and `atRestKeyVersion`.
- `targetDirectory` and `restored`.
- `achievedRpoSeconds`, `achievedRtoSeconds`, and `dataLossWindowSeconds`.
- `fileCount` and `fileBytes`.
- `missingDependencies`, containing validation problems such as mismatched archive pairs, missing
  files, checksum failures, or incompatible application versions.
- `operatorActions`, listing required post-restore checks and clone-safety actions.

Treat `status=Pass` as "the backup pair is structurally restorable." It is not a production go-live
decision until the operator actions are complete in the restored environment.

## Clone and Cross-Environment Safety

Do not silently reuse machine identity, client certificates, or environment-bound credentials after a
clone, replacement-host restore, or cross-environment restore.

- Same physical machine, same tenant/environment: restoring enrollment and policy cache from a
  host-level backup is acceptable when recovering the same host after disk loss.
- Replacement machine or cloned VM/image: do not restore `enrollment.json`, policy cache, trust keys,
  or client certificates. Revoke the retired machine identity, start unenrolled, then deliberately
  re-enroll and rotate client credentials.
- Cross-environment restore: remove or replace audit/security collector endpoints, SMTP credentials,
  Orchestrator API keys, Portal JWT secrets, policy-authority certificates, and external connector
  credentials before startup. Never allow a test restore to emit production audit/security events.
- Golden images: never bake `.portal-keys`, machine enrollment, policy cache, client certificates,
  `portal.db`, `etlsql.db`, or restored appsettings secrets into an image template.

## Regional and Offline Recovery

For regional/site failure:

- Keep PostgreSQL backups or managed snapshots in a separate failure domain from the primary database.
- Keep artifact snapshots and key-ring backups in the same recovery point family as the database
  backup they correspond to.
- Use immutable or offline backup storage for at least one retention tier so ransomware or accidental
  deletion cannot remove every recovery point.
- Maintain emergency access procedures for the backup vault, certificate/HSM authority, DNS/load
  balancer administration, and identity provider administration.
- Store split-custody data and keys archives with separate access control. No single operator should
  be able to recover protected data without the approved second custody path unless the organization
  has explicitly accepted that risk.

## Retention

Baseline retention:

- Daily backups for 14 days.
- Weekly backups for 8 weeks.
- Monthly backups for 12 months when policy or regulation requires historical recovery.
- Keep pre-upgrade backups until the upgraded deployment has passed restore validation, operational
  alerting, and the next scheduled backup cycle.

Adjust retention for legal hold, audit, privacy deletion obligations, and storage cost. Retention must
cover both database state and matching artifact/key material; retaining only one side is not a valid
recovery point.

