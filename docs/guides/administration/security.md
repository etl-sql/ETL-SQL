# Security and Secret Management

## 4. Security & Secret Management

ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate passwords, and connection strings. Encrypted values use the `ENC:` prefix.

### 4.1 Encrypting Secrets

Encrypt a value with an explicit master password:

```bash
ETL-SQL encrypt "my-secret-password" --pass "YourMasterKey"
```

The CLI also supports machine-bound encryption when no password is supplied. That is convenient for local services, but the encrypted value will not be portable if the machine key changes or the configuration is moved to another host.

### 4.2 Portal JWT Secret

The Report Portal requires a strong JWT secret. Generate one during deployment:

```bash
ETL-SQL config setup-jwt --update
```

> [!CAUTION]
> Record the plaintext secret in a password manager or deployment vault. If it is stored only as an encrypted value and the machine key is lost, the plaintext cannot be recovered.

For a non-disruptive rotation, place the replacement in `Portal__Jwt__Secret` and retain the old
value temporarily as `Portal__Jwt__PreviousSecrets__0`. The portal signs only with the current secret
and validates against both. Remove the previous value after the maximum access-token lifetime has
elapsed. Removing it sooner intentionally invalidates access tokens signed by that key.

### 4.3 Orchestrator API Key

A shared API key protects every Orchestrator route that submits, cancels, inspects, schedules, or manages jobs — including the ad-hoc execution routes `POST /jobs`, `DELETE /jobs/{id}`, and `GET /jobs/{id}`. Only the unauthenticated probes `GET /health`, `GET /metrics`, and `GET /metrics/prometheus` are exempt. The portal sends the key in the `X-Orchestrator-Key` request header.

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "C:\\ETL-SQL\\scripts"
  }
}
```

The installers (MSI custom action and Linux `postinst`) generate a random `Orchestrator:ApiKey` on first install and mirror it to `Portal:Orchestrator:ApiKey` so the two halves match out of the box.

Rotate without downtime by first adding the new key to `Orchestrator__PreviousApiKeys__0`, restarting
the Orchestrator, switching `Portal__Orchestrator__ApiKey` to the new key, then making the new key
current on the Orchestrator while retaining the old key temporarily in `PreviousApiKeys`. Remove the
old key after every caller has moved. The service compares fixed-length key digests in constant time.

> [!IMPORTANT]
> **The Orchestrator refuses to start unauthenticated on a network-reachable address.** If `Orchestrator:ApiKey` is empty *and* the service binds to a non-loopback address (for example `http://*:5001` or `http://0.0.0.0:5001`), startup fails fast with an actionable error. Configure a key, or bind the service to loopback only (`http://127.0.0.1:5001`). An empty key is permitted **only** for loopback-only bindings, which is development/isolated-host behavior.

### 4.4 Governance Core

Governance Core centralizes three production controls:

- **Plaintext secrets policy enforcement** — the central linter detects and blocks plaintext secret persistence when forbidden by policy.
- **Named secret references** — connector passwords and sensitive connection-string fields can use `SECRET:name` instead of raw secret values.
- **Durable audit forwarding** — Portal security and mutation audit rows are staged in a transactional outbox and can be forwarded to an HTTPS collector, with optional fail-closed behavior.

#### Named secret providers

Configure the secret provider in `appsettings.json` or with environment variables under `Governance:Secrets:*`.
The older `Secrets:*` prefix remains accepted as a compatibility fallback, but new deployments should use
`Governance:Secrets:*`.

```json
{
  "Governance": {
    "Secrets": {
      "Provider": "Environment",
      "EnvironmentPrefix": "ETLSQL_SECRET_"
    }
  }
}
```

Supported providers:

| Provider | Required settings | Operational notes |
| :--- | :--- | :--- |
| `Environment` | Optional `EnvironmentPrefix` | Secret names are uppercased; `.` and `-` become `_`. With the prefix above, `SECRET:sales_db_password` resolves from `ETLSQL_SECRET_SALES_DB_PASSWORD`. |
| `OsSecretStore` | `OsStoreRoot` | Stores protected values under a fully qualified local directory. Values are encrypted machine-scoped (DPAPI `LocalMachine` on Windows, machine-id-derived AES-256-GCM elsewhere), so an administrator can write secrets that a differently privileged service account reads back; restrict the directory with filesystem ACLs since any account on the host that can read the files can decrypt them. On Unix, secret files are written owner-read/write only. Values written by releases before machine scoping are user-scoped and stay readable by the account that wrote them; rotating a secret upgrades it to machine scope. The store is never read as plaintext — unrecognized file contents fail closed. |
| `HttpsVault` | `VaultEndpoint`; optional `VaultBearerToken` | The endpoint must be HTTPS. The provider requests `<VaultEndpoint>/<secret-name>` and accepts either a raw response body or JSON `{ "value": "secret" }`. |
| `PortalStore` | none (Portal host only) | Stores secrets encrypted in the Portal database using the cluster-wide Data Protection key ring — the supported multi-node path without an external vault. Managed through `api/admin/secrets` (set, list metadata, verify, verify-all, disable, enable, delete; values are never returned after write, and every mutation is audited). The `secret-store-keyring` health check under `GET /health` decrypt-probes every stored secret so an HA node with a wrong `Portal:Storage:KeyRingPath` fails fast; run `POST api/admin/secrets/verify-all` after a backup/restore to prove the restored key ring can decrypt every value without printing them. Not available to standalone CLI/Orchestrator deployments. |

Environment-variable examples:

```text
Governance__Secrets__Provider=HttpsVault
Governance__Secrets__VaultEndpoint=https://vault.example.com/etl-sql/secrets
Governance__Secrets__VaultBearerToken=ENC:ENCRYPTED_TOKEN
```

#### Managing OS secret store secrets from the CLI

With `Governance:Secrets:Provider=OsSecretStore` configured, administrators manage named secrets
without touching secret files directly:

```powershell
etl-sql admin set-secret --name sales_db_password      # prompts (masked) and confirms
etl-sql admin verify-secret --name sales_db_password   # proves the secret resolves; never prints it
etl-sql admin rotate-secret --name sales_db_password   # replaces the value; fails if it does not exist
etl-sql admin disable-secret --name sales_db_password  # resolution fails until re-enabled
etl-sql admin enable-secret --name sales_db_password   # re-enables; the stored value resolves again
etl-sql admin delete-secret --name sales_db_password   # permanently removes the secret
```

`set-secret` and `rotate-secret` read the value from a masked interactive prompt (with
confirmation), or from stdin when input is piped (`Get-Content value.txt | etl-sql admin
set-secret --name x`). `--value` is supported for automation but can persist in shell history —
the CLI warns when it is used. Values are encrypted machine-scoped before they reach disk, so run
these commands on the machine that will resolve the secrets. `set-secret` on a disabled secret
re-enables it. Secret values are never echoed, logged, or included in error messages.

#### Shared connections (connection catalog)

An administrator can catalog a connection once — the SSRS shared data source model — so users
reference it without knowing the credentials. Configure the catalog with
`Governance:ConnectionCatalog:Provider=Local` and `Governance:ConnectionCatalog:LocalRoot=<dir>`
(machine-encrypted entries, same trust boundary as the OS secret store — restrict the directory
with filesystem ACLs), then manage entries from the CLI:

```powershell
etl-sql admin set-connection --alias sales_dw --type MSSQL --option SERVER=sql01 --option DATABASE=Sales --option USER=etl_worker --option PASSWORD=SECRET:sales_db_password
etl-sql admin set-connection --alias archive_s3 --type S3 --option BUCKET=archive-prod --option ACCESS_KEY=SECRET:archive_access_key --option SECRET_KEY=SECRET:archive_secret_key --sensitive BUCKET
etl-sql admin list-connections                       # aliases and Active/Disabled status
etl-sql admin verify-connection --alias sales_dw     # proves the entry and its SECRET: references resolve
etl-sql admin disable-connection --alias sales_dw    # SHARED:sales_dw fails until re-enabled
etl-sql admin enable-connection --alias sales_dw     # re-enables; the stored definition is retained
etl-sql admin delete-connection --alias sales_dw
```

Catalog entries hold `SECRET:name` references, never credential values — `set-connection` rejects
raw credentials and points at `set-secret`. Scripts use the entry through the declared connector
type, which must match the catalog entry:

```sql
CREATE CONNECTION dw AS MSSQL('SHARED:sales_dw');
```

At execution the alias expands to the cataloged definition, `SECRET:` references resolve through
the configured secret provider, and script-local options may add to but never override cataloged
credential fields or catalog-owned sensitive fields. An unknown alias, a disabled entry, a connector type mismatch, or an
unconfigured catalog fails connection creation with a clear error.

For multi-node deployments, set `Governance:ConnectionCatalog:Provider=Portal` on the Portal so
entries live in the Portal database (endpoints and options are additionally encrypted at rest with
the cluster keys) and are managed through the audited `api/admin/connections` API: list, masked
detail, set (raw credential values rejected), verify (proves the entry and its `SECRET:`
references resolve, and stamps last-verified), disable, delete, and metadata-only export/import
for promoting entries between environments. Entries record owner, environment scope, and
last-used/last-verified timestamps for governance review. Pair it with
`Governance:Secrets:Provider=PortalStore` so both the catalog and the secrets it references are
cluster-wide.

Portal-cataloged entries can additionally carry **use grants** (Admin → Connections → Detail →
Access, or `api/admin/connections/{alias}/acl`): an entry with no grants is usable by any caller
(the default), while an entry with grants can only be expanded by administrators, its owner, or
members of a granted group. The executing user's identity is checked at `SHARED:alias` expansion
time, denials are audited (`SHARED_CONNECTION_USE_DENIED`) without resolving any secret, and
executions without an injected identity are denied for restricted entries. Grants are group-based,
matching the folder/dataset permission model.

Before disabling or deleting, check **impact** (the Impact button in either admin tab, or
`GET api/admin/connections/{alias}/impact` / `GET api/admin/secrets/{name}/impact`): it lists
published reports, subscription job scripts, and orchestrator scheduled jobs whose scripts
reference the alias or secret name, catalog entries that reference a secret, and — for shared
connections — the recorded per-consumer usage (which user resolved the entry, when, and how many
times), captured automatically at `SHARED:alias` resolution.

### Native admin services

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

#### Enterprise machine enrollment

Enterprise policy is opt-in. When no machine enrollment exists, ETL-SQL remains in standalone mode:
it uses local configuration, requires no policy-server connection, and applies only its built-in safety
controls. Enterprise enrollment is deliberately stored outside `appsettings.json`, environment variables,
and command-line configuration so those lower-authority sources cannot disable it.

Generate or obtain the organization's RSA policy-signing key pair and place only the public PEM file on
the machine being enrolled. Run enrollment from an elevated Administrator or root shell:

```powershell
etl-sql enterprise enroll `
  --tenant corp-production `
  --policy-endpoint https://policy.example.com/etl-sql/policy `
  --signing-key C:\Install\etl-sql-policy-public.pem `
  --client-certificate-thumbprint 0123456789ABCDEF0123456789ABCDEF01234567 `
  --service-identity "NT SERVICE\ETL-SQL" `
  --max-offline-hours 24
```

The policy endpoint must be HTTPS without embedded credentials. The signing key must be RSA PEM with at
least 2048 bits. The optional certificate thumbprint identifies the machine credential presented to the
policy endpoint. `--service-identity` grants that Windows service identity read access to enrollment and
write access only to the separate protected policy-cache directory;
omit it when ETL-SQL runs as Local System. On Unix, install as root and arrange the service identity or
service manager so it can read the root-owned bootstrap without making it group- or world-writable.

Enrollment is stored at:

- Windows: `%ProgramData%\ETL-SQL\Enterprise\enrollment.json`
- Linux/macOS: `/etc/etl-sql/enterprise/enrollment.json`

Windows grants control only to Local System and Administrators, plus read access to the optional service
identity. Unix writes the directory as `0700` and the file as `0600`. Every ETL-SQL executable checks this
fixed location before loading ordinary application configuration. If enrollment exists but is malformed,
uses unsafe permissions, has an unsupported schema, or contains an invalid endpoint or trust key, normal
startup fails closed.

Inspect status without exposing the key or certificate value:

```text
etl-sql enterprise status
```

Remove enrollment only from an elevated shell with explicit confirmation:

```text
etl-sql enterprise unenroll --yes
```

The unenrollment command can remove a malformed but still OS-protected bootstrap for disaster recovery.
If file permissions themselves are unsafe, repair ownership and permissions first; the command will not
trust or delete a broadly writable bootstrap. Removing enrollment returns the installation to standalone
mode. Organizations should monitor and restrict this administrative operation through endpoint management.

Enrollment protects the trusted ETL-SQL installation. It cannot stop a user from downloading, compiling,
or running unrelated software. Environments requiring mandatory enforcement must also restrict executable
launch through Windows Defender Application Control/AppLocker, managed software deployment, container
admission policy, or equivalent operating-system controls.

Filesystem approved-root enforcement canonicalizes paths and resolves symbolic links and junctions,
but a path cannot reliably identify every hard-link alias to the same underlying file. Treat hard-link
creation as an operating-system privilege boundary: deny it to ETL-SQL service accounts and protect
approved roots with ACLs or equivalent mount permissions. ETL-SQL does not claim hard-link containment
against a local administrator.

Local file and directory delete, move, rename, copy, archive extraction, and archive overwrite paths
use the filesystem policy authorizer immediately before mutation. On Windows and Linux, ETL-SQL
re-checks the final path reported by the opened file handle before destructive file mutation; on
platforms that cannot report a handle final path, enforcement remains best-effort by canonical path.
Remote filesystem connectors (`IRemoteFileSystem`, including SFTP, FTP, S3, Azure Blob, and
SharePoint-style object stores) are outside that OS-handle guarantee. ETL-SQL still applies connector
and path policy before dispatch, but remote delete, move, and rename semantics are governed by the
provider. Use provider IAM, scoped credentials, bucket/container policies, object versioning or object
lock where available, remote audit logs, and least-privilege service identities to contain provider-side
mutation risk.

#### Authoritative organization policy

The Portal policy authority signs published envelopes with an RSA certificate whose private key remains
in the operating-system certificate store. Configure only its thumbprint; never export the private key
into Portal JSON, environment variables, backups, configuration exports, logs, or support bundles:

```json
{
  "Portal": {
    "PolicyAuthority": {
      "SigningCertThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567"
    }
  }
}
```

Install the certificate in `LocalMachine/My` where possible; `CurrentUser/My` is the fallback. Grant the
Portal service identity permission to use its private key. An unset thumbprint disables publication with
a deterministic configuration error. Install and grant a replacement certificate before changing the
thumbprint, and retain the former public key until enrolled clients trust the replacement.

Portal administrators manage the authority from **Admin -> Policy Authority**. The tab validates
policy JSON, publishes active or staged versions, activates staged versions, republishes emergency
rollback versions, registers enrolled machines, revokes machine identities, and shows signing-key
status. The same operations are available through `api/admin/policy-authority/*`; the UI and API
never receive or return private-key material.

##### Policy authority deployment and operator runbook

Deploy the policy authority as part of the Report Portal control plane. In single-node deployments,
the same Portal instance may host user administration, catalog administration, and policy authority
operations. In HA deployments, every Portal node must use the same PostgreSQL Portal database, the
same Data Protection key ring, and the same policy-signing certificate identity; otherwise one node
may publish or serve a policy envelope that another node cannot verify operationally. Load balancers
should continue to probe `GET /healthz`; use `GET /health` or fleet monitoring to inspect the
`policy-authority` health check and catch missing or inaccessible signing keys before a publication
window.

Restrict policy-authority administration to the smallest administrator group that can approve
organization policy. Treat that role separately from routine report, subscription, and connection
catalog administration. Policy publication can change filesystem roots, connector destinations,
security-event delivery, and execution ceilings across enrolled machines; require peer review outside
the product workflow if your organization has four-eyes controls. Every policy-authority mutation is
audited through the Portal audit trail, including publish, activate, rollback, canary, machine
registration, and machine revocation actions.

Signing-key custody belongs to the operating-system certificate store or an equivalent managed
certificate deployment process. The Portal service identity needs private-key use permission, but
operators should not export the private key to configuration files, release archives, support
bundles, database backups, or screenshots. Keep the public key PEM used for machine enrollment in a
versioned deployment record so enrolled machines can be re-provisioned consistently. For rotation:

1. Generate or import the replacement RSA signing certificate.
2. Grant the Portal service identity private-key use permission.
3. Publish and validate a staged policy while the old active policy remains in service.
4. Update `Portal:PolicyAuthority:SigningCertThumbprint` to the replacement thumbprint and restart
   each Portal node under normal change control.
5. Publish a new policy version and verify the audit entry records `SigningKeyRotated=true`.
6. Re-enroll or re-provision machines with the replacement public key before retiring trust in the
   former key.

Do not remove the old public key from endpoint-management baselines until every enrolled machine that
must continue receiving policy has been re-enrolled. Machines pin the public signing key at
enrollment; a machine that still trusts only the old public key will reject envelopes signed by the
replacement key. If immediate revocation of a compromised signing key is required, revoke affected
machine identities first, rotate the Portal signing certificate, re-enroll machines from known-good
media, and accept that old enrollments will fail closed until repaired.

Register each enrolled machine in **Admin -> Policy Authority -> Machine enrollment** before or
immediately after running `etl-sql enterprise enroll` on that host. The registered tenant,
environment, machine ID, enrollment ID, optional client-certificate thumbprint, and optional canary
group are authoritative; the distribution endpoint ignores caller-supplied environment values and
serves policy based on the registered record. Revoking a machine identity makes policy retrieval fail
immediately for that identity and is the correct response to host retirement, cloned images,
credential exposure, or suspected bootstrap compromise. To reassign a host to another tenant or
environment, revoke the old machine record, remove enrollment on the host, and enroll/register it as
a new identity.

Service identities need only the permissions required for their role:

- **Portal service identity** — read its configuration, use the policy-signing certificate private
  key, access the Portal database, write Portal logs, and access shared Portal artifact/key-ring
  roots configured for that deployment.
- **Orchestrator service identity** — read its enrollment bootstrap and protected policy cache, read
  scripts from approved roots, write its job/session/log state, and access only the source systems
  and artifact roots required by scheduled jobs.
- **Workstation/CLI identity** — read its own enrollment bootstrap when the workstation is enrolled,
  but should not receive Portal signing-key access or server-side mutation permissions.

Use staged publication for normal policy changes. Validate the policy JSON in the Portal, publish it
as staged, review the version hash and expiry, then activate it during the change window. Use canary
rollout when a policy may affect path approvals, connector destinations, service-event delivery, or
execution ceilings: start with a named operations group or a low percentage, confirm policy refresh
and job behavior, then promote or halt. Avoid publishing a restrictive fleet-wide policy directly
unless the change is an emergency.

Emergency policy publication is for immediate containment, such as blocking a compromised connector
destination, disabling a dangerous filesystem root, or tightening security-event fail-closed
thresholds. Publish the emergency policy with a short expiry and a distinct version name, verify at
least one enrolled node has refreshed it, and record the operational reason in the change record.
After containment, publish a normal reviewed policy that either preserves the emergency restriction
or deliberately rolls it back. If the emergency policy is wrong, use rollback or halt-canary rather
than editing the underlying database; direct database edits bypass signing, version history, and
audit guarantees.

Unenrollment is a governance event, not a routine troubleshooting shortcut. It returns the
installation to standalone mode, where organization policy is no longer retrieved or enforced. Permit
`etl-sql enterprise unenroll --yes` only during approved decommissioning, lab rebuilds, or recovery
from a malformed but still protected bootstrap. For production hosts, revoke the machine identity in
the Portal before or immediately after unenrollment, remove or rotate service credentials that were
usable by the host, and preserve audit/security-event records according to retention policy. If a
team needs a temporary policy bypass for incident response, prefer a signed emergency policy or a
short-lived canary/rollback action so the fleet remains under the authority model.

##### Canary (progressive) policy rollout

Before a policy change goes fleet-wide, you can validate it on a subset of enrolled machines. A
**canary** version is published alongside — not over — the active version: only machines in its
cohort receive it, while the rest of the tenant/environment keeps running the active version
unchanged. Use it to confirm new filesystem-path or connection restrictions on a small pool before
committing the fleet.

A cohort targets machines one of two ways (exactly one per canary):

- **Percentage of fleet** (1–100) — machines are selected by a stable, deterministic hash of their
  machine identity. The assignment does not change between polls, and ramping the percentage up only
  *adds* machines (a node in the cohort at 10% stays in at 25%), so you can widen a canary gradually.
- **Named machine group** — machines you have labelled with that group at registration (the optional
  **Canary group** field on *Register machine*).

From **Admin -> Policy Authority -> Publish canary**, set the *Canary version* and cohort, then
publish (the canary reuses the *Policy JSON* and *Expires at* from the publish form above). The
canary appears in the version history with a **Canary** state and its cohort; each canary row offers:

- **Promote** — makes the canary the fleet-wide active version, superseding the previous active.
- **Halt** — rolls the canary back and reverts its machines. Because clients reject an envelope
  issued *before* the one they hold, halting re-issues the current active document as a fresh active
  version (a later issuance), which the cohort machines accept on their next five-minute refresh.

Only one canary can be in progress per tenant/environment at a time; promote or halt it before
starting another. Canaries are signed, versioned, and rollback-protected exactly like fleet-wide
versions, and every publish/promote/halt is recorded in the mutation audit trail
(`PUBLISH_CANARY_POLICY`, `PROMOTE_CANARY_POLICY`, `HALT_CANARY_POLICY`) — a canary cannot silently
move machines onto a different policy. Standalone (unenrolled) installations never contact the policy
authority and are unaffected by any canary.

On every normal process startup, an enrolled installation requests a signed policy envelope from the configured
HTTPS endpoint. The request carries `X-ETL-SQL-Tenant`, `X-ETL-SQL-Enrollment`, and `X-ETL-SQL-Machine` headers
and presents the enrolled client certificate when configured. The server must return JSON in this form:

```json
{
  "schemaVersion": "1.0",
  "tenant": "corp-production",
  "policyVersion": "2026-06-28.4",
  "issuedAtUtc": "2026-06-28T12:00:00Z",
  "expiresAtUtc": "2026-06-29T12:00:00Z",
  "policyPayload": "<base64 UTF-8 policy JSON>",
  "signature": "<base64 RSA-PSS SHA-256 signature>"
}
```

The signature input is the UTF-8 encoding of these six values separated by a single LF (`\n`), with timestamps
formatted as UTC round-trip (`O`) values:

```text
schemaVersion
tenant
policyVersion
issuedAtUtc
expiresAtUtc
policyPayload
```

ETL-SQL verifies the enrolled tenant, issuance and expiry, RSA-PSS SHA-256 signature, and embedded policy schema.
It rejects a live envelope issued before the currently cached envelope to prevent rollback. A verified live
envelope is atomically stored under the protected `Enterprise/cache` directory and is fully re-verified before
offline use. Cache use ends at the earlier of envelope expiry and `MaxOfflineHours` from caching. Missing,
tampered, expired, or unsafe cache state fails startup when enrollment is fail-closed. The enrollment-only
`--allow-offline-failure` option permits startup without policy and is intended only for explicitly accepted
non-production risk.

Long-running Portal, Report Player, and Orchestrator hosts refresh policy every five minutes. A newly verified
policy reloads the enterprise configuration overlay. If live retrieval and verified cache recovery both fail
under fail-closed enrollment, the host logs a critical error and stops rather than continuing beyond policy
freshness. Supervise these processes with Windows Services, systemd, Kubernetes, or an equivalent service manager
so an unhealthy policy dependency is visible and restart behavior follows organizational policy.
Governance policy documents inside `policyPayload` use schema version `1.0`. Execution limits include
parallelism, file/recursion operations, spill volume, SMTP sends, and maximum materialized string bytes:

```json
{
  "schemaVersion": "1.0",
  "execution": {
    "maxStringResultSize": 104857600
  },
  "mutationGuardrails": {
    "requireRemoteAuditForMutations": true
  }
}
```

Verified policy values are added after JSON, environment variables, command-line configuration, and
test/deployment overrides, giving the authoritative enterprise policy final configuration precedence.
Operation-boundary checks additionally prevent scripts from weakening governed ceilings. Portal report
execution timeouts and paged result limits remain host availability controls rather than organization-policy
keys: scripts cannot raise them, and each host enforces its configured timeout or page limit independently.

Run `etl-sql enterprise status` to retrieve and verify policy and report `Live`, `Cached`, or `Unavailable`, the
policy version, source, issuance, expiry, governed key names, and any live-retrieval warning. Trust keys,
certificate thumbprints, signatures, and policy payload values are not printed.

##### Enterprise upgrade ordering and schema compatibility

Enterprise enrollment, policy, and security-event delivery are intentionally versioned and fail
closed when a host receives a schema it does not understand. In this release, the protected
bootstrap/enrollment document uses schema `1.0`, the signed policy envelope uses schema `1.0`, the
organization policy payload uses schema `1.0`, and the security-event transport uses schema `1`
(`X-ETL-SQL-Security-Event-Schema: 1` plus matching request and event bodies). Operators must
therefore upgrade services before publishing any policy, envelope, or collector contract that
requires a newer schema.

Use this order for rolling enterprise upgrades:

1. Back up the Portal and Orchestrator databases, Portal artifact roots, Data Protection key ring,
   signing-certificate deployment records, and policy-authority state. Confirm the rollback plan
   before changing schemas or publishing policy.
2. Upgrade the security-event collector and remote audit collector first. During a mixed-version
   window, collectors must accept the current schema and the next schema being introduced, dedupe by
   event ID, and keep explicit acknowledgement behavior. Do not enable fail-closed thresholds for a
   new event schema until collector acceptance has been proven.
3. Upgrade Portal/policy-authority nodes and apply database migrations before publishing envelopes
   or policies that use new schema versions or new policy keys. In HA deployments, let the normal
   migration/lease ownership path run once; do not start two incompatible Portal builds against the
   same database.
4. Upgrade Orchestrator, Report Portal workers, Report Player, CLI, TUI, language-server, and CI
   runner hosts that consume enterprise policy. Keep the active policy within the oldest supported
   bootstrap, envelope, and policy-payload schema until those hosts report healthy status.
5. Publish a staged or canary policy that still uses the shared supported schema. Verify
   `etl-sql enterprise status`, fleet health, policy version, refresh time, security-event delivery,
   and audit delivery on the canary cohort before promoting.
6. Only after the fleet and collectors are upgraded should you publish a policy or envelope that
   requires the newer schema. Keep collector support for the prior event schema until all retained
   local outboxes that may contain prior-schema events have drained or expired under retention
   policy.

Compatibility rules:

- Older enrolled clients reject unsupported bootstrap, envelope, policy, and security-event schemas
  rather than guessing. With fail-closed enrollment, that rejection stops startup or execution instead
  of silently running outside policy.
- New binaries must continue to read supported existing enrollment and cache files for the documented
  compatibility window. Do not edit `Enterprise/enrollment.json` in place to a new schema before the
  installed binary supports it.
- Signed policy rollback protection is based on envelope issuance time. When reverting a bad canary
  or active policy, publish or halt through the policy authority so the replacement envelope has a
  later issuance time; direct database edits can strand clients on the rejected version.
- Portal audit outbox rows are tied to Portal database migrations. Restore or migrate them with the
  Portal database, and make the remote collector deduplicate event IDs so retried rows remain safe.
- Policy payload additions that old clients ignore are acceptable only when the default behavior is
  safe. Any mandatory enforcement change needs a schema or capability gate and must be rolled out
  after the consuming binaries are upgraded.

Before closing an upgrade window, run `etl-sql enterprise status` on representative enrolled hosts,
check Portal `GET /health` for policy-authority and collector health, verify security-event backlog
age/counts are falling, and confirm no machine is still reporting an unsupported schema or stale
policy version.

##### Enterprise outage runbooks

Use the runbooks below for enterprise-control outages. They are written to preserve policy
authority, auditability, and fail-closed guarantees; avoid direct database edits unless support has
confirmed the signed authority path cannot be recovered.

**Policy authority unavailable**

Symptoms include `policy-authority` degraded in Portal `GET /health`, policy retrieval failures from
`etl-sql enterprise status`, or enrolled hosts falling back to `Cached` policy. First confirm whether
the Portal process, Portal database, signing certificate, load balancer, and TLS certificate chain are
healthy. If the active policy is still cached and unexpired, leave enrolled hosts running and restore
the authority before cache freshness expires. Do not unenroll production hosts to work around an
authority outage. If cache expiry is imminent, publish no new policies until the authority is stable;
recover the Portal node or fail over to a node with the same Portal database, Data Protection key
ring, and policy-signing certificate.

**Policy signing certificate expired or inaccessible**

Symptoms include a degraded `policy-authority` health check, publication failures, or enrolled
clients rejecting newly served envelopes. Restore access to the configured
`Portal:PolicyAuthority:SigningCertThumbprint` certificate first: verify it is installed in the
expected store, has a private key, chains to the expected trust root, and grants private-key use to
the Portal service identity. If the certificate is expired or compromised, install the replacement,
grant access, update the thumbprint, restart Portal nodes, publish a staged policy, and verify a
canary refresh. Machines pin the enrollment public key, so re-enroll affected machines before
retiring the old public key.

**Invalid policy publication**

Symptoms include canary execution denials, `PolicyValidationFailure` security events, or hosts
reporting policy refresh errors after activation. If the problem is in a canary, halt it from
**Admin -> Policy Authority** so the authority re-issues the active policy with a later issuance
time. If the bad policy is active fleet-wide, use rollback or emergency publication through the
policy authority, then verify `etl-sql enterprise status` on representative hosts. Do not repair by
editing `PolicyVersions` rows or cache files: clients reject older envelope issuance times, and
manual edits bypass signature, audit, and rollback protection.

**SIEM or security-event collector outage**

Symptoms include collector reachability failures, increasing pending count or oldest pending age,
terminal delivery failures, and fail-closed denials when signed thresholds are exceeded. First
confirm the collector endpoint, DNS, TLS certificate, client-certificate trust, and firewall path.
If the collector is intentionally down and fail-closed thresholds are not yet breached, restore the
collector and let local outboxes drain; events are retried and deduplicated by `eventId`. If
thresholds are breached and production work is blocked, prefer restoring the collector or increasing
capacity at the collector. Only publish a temporary emergency policy that relaxes fail-closed
thresholds when the organization accepts the audit-delivery risk, and follow it with a normal
reviewed policy after recovery.

**Disk exhaustion or outbox full**

Symptoms include outbox write failures, `SecurityEventOutboxFullException`, Portal audit outbox
backpressure, growing `AuditOutboxMessages`, or host disk alerts. Free space on the affected volume,
move logs or non-authoritative artifacts first, and preserve `Enterprise/enrollment.json`,
`Enterprise/cache`, `security-events.db`, Portal database files, and Data Protection keys. Do not
delete pending outbox rows to clear a fail-closed condition unless the business decision is to lose
audit/security evidence; if that decision is made, record it outside ETL-SQL and prefer retaining a
forensic copy before removal. After space is restored, restart affected services and verify backlog
counts decrease.

**Fail-closed fleet recovery**

When many hosts stop because policy, audit, or security-event delivery is unhealthy, recover the
control plane before weakening policy. Check Portal `GET /health`, fleet status, policy version and
expiry, audit outbox pending/failed counts, security-event pending/failed counts, oldest pending age,
outbox bytes, and collector reachability. Recover in this order: Portal database and policy
authority, signing certificate access, collector endpoints, disk capacity, then enrolled hosts.
After the control plane is healthy, restart a small canary cohort, verify `Live` policy status and
draining event queues, and then restart the rest of the fleet. If emergency policy relaxation was
used, publish a reviewed policy restoring normal fail-closed thresholds before closing the incident.

##### Enterprise cache and outbox recovery rules

Treat machine enrollment state as host identity, not as application configuration. The protected
`Enterprise/enrollment.json` file contains the tenant, policy endpoint, pinned policy-signing public
key, enrollment ID, machine ID, optional client-certificate thumbprint, and offline/fail-closed
settings. The sibling `Enterprise/cache` directory contains the verified policy cache and, for
enrolled machines, the local security-event outbox database (`security-events.db`). Do not copy this
directory into a golden image, clone it to another host, or restore it into another tenant or
environment.

Backup and restore rules:

1. **Same physical machine, same tenant/environment** — You may restore `enrollment.json` and
   `Enterprise/cache` from a host-level backup when recovering the same machine after disk loss.
   Restore ownership and permissions first, then run `etl-sql enterprise status`. If status cannot
   verify the bootstrap or cache, repair by revoking the old machine record in the Portal and
   enrolling the host again rather than editing the JSON by hand.
2. **Replacement machine or cloned VM/image** — Do not restore `enrollment.json`,
   `Enterprise/cache`, or `security-events.db`. Register and enroll the replacement as a new machine
   identity. Revoke the retired machine identity in the Portal so any copied or stolen enrollment
   fails policy retrieval. If the replacement uses a client certificate, issue or bind a replacement
   certificate and register its thumbprint with the new machine record.
3. **Cross-environment restore** — Never reuse the original machine enrollment, policy cache,
   security-event outbox, service-account secrets, connector credentials, or client certificate in a
   different tenant or environment. Restore application data only after deciding which credentials
   are valid for the target environment, then rotate or re-create those credentials deliberately.
   Re-enroll the host against the target policy authority so the new tenant/environment binding is
   explicit and audited.
4. **Policy-cache recovery** — The cache is a fallback for the enrolled machine that wrote it. It is
   re-verified before offline use and rejected if expired, tampered with, issued for the wrong
   tenant, older than the currently trusted issuance, or outside `MaxOfflineHours`. A restored cache
   can help a same-machine recovery survive a short policy-authority outage; it is not a portable
   policy artifact.
5. **Security-event outbox recovery** — Preserve the enrolled machine's `security-events.db` only
   for same-machine recovery. Events are idempotent by event ID, so restored pending events may be
   retried safely by a deduplicating collector. Do not move an outbox to a different machine
   identity: the transport signs the batch with that machine's enrollment headers, and moving it
   corrupts fleet accountability.

Portal audit outbox state is different. Portal audit rows and `AuditOutboxMessages` live in the
Portal database and are part of the Portal backup/restore set. Restore them with the Portal database
so pending remote-audit delivery can resume after a same-environment disaster recovery. If a database
backup is restored into a non-production environment, change or remove `Portal:Audit:TransportEndpoint`
and collector credentials before starting the Portal, or the restored environment may forward old
production audit rows to the production collector. For production failover, keep the endpoint and
credentials only when the restored Portal is assuming the same production authority and retention
obligations.

#### Central security events and SIEM delivery

Security events are separate from diagnostic logs and transactional governance audit records. They carry the
same correlation, job, script, policy, tenant, and node identifiers when those values are available, but use a
dedicated versioned contract and durable local outbox. A policy denial is decided and enforced before reporting;
an unavailable event sink cannot turn a denial into an allow. Remote delivery affects execution only when an
enrolled organization's signed policy explicitly configures a fail-closed threshold.

Configure delivery in the signed organization policy. The collector endpoint must use HTTPS, must not contain
embedded credentials, and requires the enrolled machine's client certificate:

```json
{
  "schemaVersion": "1.0",
  "securityEvents": {
    "collectorEndpoint": "https://siem.example.com/etl-sql/security-events",
    "batchSize": 100,
    "intervalSeconds": 30,
    "leaseSeconds": 120,
    "minimumForwardedSeverity": "warning",
    "failClosedMaxTerminalFailures": 5,
    "failClosedMaxOldestEventSeconds": 900,
    "failClosedMaxPendingEvents": 1000,
    "failClosedMaxOutboxBytes": 104857600
  }
}
```

Omit all `failClosed*` values for local durability with best-effort remote forwarding. Standalone installations
write only to their local outbox and make no enterprise network calls. A severity filter removes lower-severity
rows from forwarding without changing local enforcement. Because the filter is authoritative policy, changing it
requires publishing a newly signed policy.

Standalone hosts use the OS local-application-data directory by default. Containers and certification harnesses
may set `ETLSQL_SECURITY_EVENT_OUTBOX_PATH` to an absolute path on a persistent writable volume. This override is
ignored for enrolled machines; their outbox remains beside the protected enrollment state.

Each request contains enrollment headers, an `Idempotency-Key` for the batch, schema header
`X-ETL-SQL-Security-Event-Schema: 1`, and this JSON envelope:

```json
{
  "schemaVersion": 1,
  "batchId": "<sha256-of-sorted-event-ids>",
  "events": [
    {
      "schemaVersion": 1,
      "eventId": "4f7578f2-46d4-40cb-8cba-cc08d186f409",
      "severity": "error",
      "type": "operationDenied",
      "timestampUtc": "2026-07-13T18:30:00Z",
      "actorIdentity": "user:42",
      "effectiveIdentity": "service:runner",
      "hostName": "etl-node-03",
      "nodeId": "machine-id",
      "tenantId": "production",
      "scriptHash": "<sha256>",
      "jobId": "job-9",
      "correlationId": "corr-17",
      "policyVersion": "v4",
      "policyHash": "<sha256>",
      "sanitizedTarget": "https://api.example.com",
      "decision": "denied",
      "reason": "Destination is outside the approved host policy."
    }
  ]
}
```

The collector must authenticate the client certificate and enrollment headers, deduplicate on `eventId`, and
return only IDs it durably accepted:

```json
{
  "acknowledgedEventIds": [
    "4f7578f2-46d4-40cb-8cba-cc08d186f409"
  ]
}
```

A 2xx response without an explicit acknowledgement does not remove an event. Unacknowledged rows are retried;
collectors must therefore make `eventId` unique in their ingestion store. The ETL-SQL schema remains the source
record. Apply vendor normalization in the collector or ingestion pipeline so changing SIEM products never changes
the engine contract.

| ETL-SQL field | Splunk CIM example | Elastic ECS example | Microsoft Sentinel ASIM example |
| :--- | :--- | :--- | :--- |
| `timestampUtc` | `_time` | `@timestamp` | `TimeGenerated`, `EventStartTime`, `EventEndTime` |
| `eventId` | `event_id` | `event.id` | `EventOriginalUid` |
| `type` | `signature`, `eventtype` | `event.action` | `EventOriginalType` |
| `severity` | `severity` | `event.severity` using a documented local numeric map | `EventOriginalSeverity`; normalize to `EventSeverity` |
| `decision` | `action` (`blocked`, `allowed`, `failed`) | `event.type` (`denied`, `allowed`, `error`) and `event.outcome` | `EventResult` and `EventResultDetails` |
| `reason` | `message` | `event.reason` | `EventMessage` |
| `actorIdentity` | `user` | `user.name` | `ActorUsername` |
| `effectiveIdentity` | custom `effective_user` | `user.effective.name` | `TargetUsername` or `AdditionalFields` |
| `hostName`, `nodeId` | `host`, custom `device_id` | `host.name`, `host.id` | `DvcHostname`, `DvcId` |
| `tenantId` | custom `tenant_id` | `organization.id` | `ActorScopeId` or `AdditionalFields` |
| `sanitizedTarget` | `dest` or `object` | `resource.name` or a domain-specific destination field | schema-specific target field or `AdditionalFields` |
| `correlationId`, `jobId`, `scriptHash`, `policyVersion`, `policyHash` | retain as custom fields | retain under `labels` or namespaced custom fields | retain in `AdditionalFields` |

For Elastic, a successfully enforced denial should normally use `event.type: denied` and
`event.outcome: success`; the policy action succeeded even though the requested operation did not. Use
`event.outcome: failure` for ETL-SQL `decision: failed`. For Sentinel, choose the specialized ASIM schema when
the target has clear file, network, process, or audit semantics; otherwise retain the source record and normalize
the common fields. Mapping references: [Splunk CIM fields](https://help.splunk.com/en?resourceId=CIM_User_CIMfields&version=cim-6_1),
[Elastic ECS event fields](https://www.elastic.co/docs/reference/ecs/ecs-event), and
[Microsoft Sentinel ASIM common fields](https://learn.microsoft.com/azure/sentinel/normalization-common-fields).

Monitor fleet-status security-event diagnostics for pending and terminal counts, oldest pending time, outbox
bytes, dropped events, collector reachability, and last attempt/success/failure. Test collector outage and recovery
before enabling fail-closed thresholds; a threshold breach intentionally blocks new script execution until the
outbox becomes healthy.

#### Durable audit outbox and remote collectors

Portal audit rows are written with a durable outbox row in the same database transaction. Configure remote
forwarding under `Portal:Audit:*`:

```json
{
  "Portal": {
    "Audit": {
      "TransportEndpoint": "https://siem.example.com/etl-sql/audit",
      "TransportBearerToken": "ENC:ENCRYPTED_COLLECTOR_TOKEN",
      "TransportBatchSize": 100,
      "TransportIntervalSeconds": 30,
      "TransportTimeoutSeconds": 10,
      "TransportMaxAttempts": 8,
      "TransportLockSeconds": 120,
      "OutboxBackpressureLimit": 10000,
      "OutboxMaxBytes": 104857600,
      "OutboxDeliveredRetentionMinutes": 1440,
      "RequireRemoteDelivery": true,
      "FailClosedMaxPendingBacklog": 1000,
      "FailClosedMaxBacklogSeconds": 900
    }
  }
}
```

The collector endpoint must be HTTPS. Each POST body has an `events` array. Every event includes a stable
`EventId`, audit metadata, and a redacted JSON payload; collectors should treat `EventId` as the deduplication key
because a row may be resent after a crash or lost delivery acknowledgement. Any 2xx response marks the batch
delivered. Non-2xx responses retry with exponential backoff until `TransportMaxAttempts`, then the row is marked
`Failed`.

`RequireRemoteDelivery` changes the Portal from best-effort forwarding to fail-closed mutation behavior. **Leaving it
unset is the recommended default**: fail-closed then turns on automatically for an **enrolled** deployment that has a
collector configured (`TransportEndpoint`), and stays off for standalone/unenrolled deployments and for any deployment
with no collector — so a compliance deployment gets fail-closed audit without having to remember to flip a switch,
while nothing is ever blocked where remote audit was not set up. Set an explicit `true`/`false` to override; an
explicit value always wins. When it is
enabled, security-sensitive mutations are blocked with HTTP 503 once remote audit delivery is judged unavailable:
any terminally failed outbox row, pending backlog over `FailClosedMaxPendingBacklog`, oldest pending row older than
`FailClosedMaxBacklogSeconds`, or queued payload over `OutboxMaxBytes`. Leave it disabled unless an HTTPS collector
is configured, monitored, and treated as mandatory infrastructure.

When `RequireRemoteDelivery` is disabled, the outbox transport may shed old delivered rows and then oldest queued
rows to keep local disk usage under `OutboxMaxBytes`; the durable local `AuditLog` rows remain. When
`RequireRemoteDelivery` is enabled, ETL-SQL never drops queued remote-audit rows to satisfy the cap; it blocks new
mutations until the collector drains the backlog.

Operational checks:

1. Configure the collector and verify it accepts HTTPS POSTs from every Portal node.
2. Trigger a harmless audited action and confirm the collector receives an event with a stable `EventId`.
3. Temporarily stop the collector and confirm pending outbox rows accumulate.
4. If `RequireRemoteDelivery` is enabled, confirm mutations fail with HTTP 503 after the configured backlog, age, or size threshold.
5. Restart the collector and confirm pending rows drain and mutations resume.

### 4.x Row-Level Security (report data filtering)

Folder and dataset permissions control **which reports a user can open** — the coarse-grained gate.
Row-level security (RLS) is the finer layer: it lets a report author filter the *rows* a viewer sees
based on the viewer's identity, so one report can serve every user their own slice of the data.

**How authors write it.** The engine exposes the authenticated viewer's identity to report SQL as
read-only system variables and predicate functions, populated by the Portal from the signed-in user:

| Primitive | Meaning |
| :--- | :--- |
| `@@CURRENT_USER` / `@@CURRENT_USER_ID` | The viewer's username / id. |
| `@@REAL_USER` | The actual actor — differs from `@@CURRENT_USER` only under admin impersonation. |
| `@@IS_ADMIN` | Whether the effective viewer is an administrator. |
| `HAS_GROUP('name')` | TRUE if the viewer belongs to the group (case-insensitive). |
| `HAS_ROLE('name')` | TRUE if the viewer holds the Portal role. |

Groups come from Portal group membership **and OIDC group claims** (synced at login). A typical
row-filtered report:

```sql
SELECT r.* FROM sales r
WHERE HAS_GROUP('Region:' + r.RegionCode);   -- membership test, not a substring match
```

**Security properties administrators should know:**

- **The identity is not forgeable.** These variables are injected by the Portal from the authenticated
  principal; a script cannot assign them (`SET @@CURRENT_USER = …` is rejected) and report parameters
  cannot populate them.
- **Admins bypass RLS by default.** `HAS_GROUP` / `HAS_ROLE` return TRUE for administrators so they see
  all rows. Set `Portal:Security:AdminBypassRowLevelSecurity` to `false` to filter admins by the same
  predicates as everyone else.
- **Fail-closed.** If no identity is present (e.g. a non-interactive run), `HAS_GROUP` returns FALSE and
  `@@CURRENT_USER` is null, so a well-formed predicate returns **no rows** rather than leaking all rows.
- **No shared snapshot.** A report that references any identity primitive is automatically treated as
  identity-sensitive: it is executed per viewer and its result is **never** cached as a shared snapshot,
  so one user's filtered rows can never be served to another. These reports run fresh on each view
  rather than from the snapshot cache.
- **Predicate integrity depends on report change control.** RLS lives in the report's SQL, so the
  existing publish-permission and published-hash checks are what prevent an author from removing the
  filter. Treat edit/publish rights on RLS reports accordingly.

**Admin impersonation.** An administrator can reproduce what a specific user sees via
`POST /api/reports/{id}/execute-as/{targetUserId}`. The run filters rows as the target user (including
the target's — not the admin's — bypass status), while the audit log records the real admin acting as
the target (`EXECUTE_REPORT_AS`). Impersonated runs are never cached.

> Full design and threat model: `docs/architecture/decisions/RowLevelSecurity.md`.

---

