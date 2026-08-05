# Authoritative Organization Policy

Signed, centrally published organization policy for enrolled machines — the Enterprise counterpart to the source-controlled workspace policy.

## By deployment profile

| Profile | What applies |
| :--- | :--- |
| **Solo / Workstation** | **Not this document.** Your policy is the source-controlled workspace policy `etlsql-policy.json` — checked in, versioned with the scripts it governs, and enforced by the CLI. See the [one-person quality loop](../../guides/one-person-quality-loop.md). |
| **Team / SME** | Workspace policy still applies and is usually enough. A signed organization authority is available but is not where most teams should start. |
| **Enterprise / Corporate** | Everything here: signed policy envelopes, a private key that never leaves the OS certificate store, machine enrollment, canary rollout and rollback. Check `GET /api/admin/policy-authority/impact` before activating — it answers what happens when you press the button. |
| **SaaS / Departmental** | As Enterprise, with policy authority **scoped per tenant or environment**. Tenant-specific policy authority and platform separation are **not certified**; do not infer them from the Enterprise path. |

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

## Policy authority deployment and operator runbook

Deploy the policy authority as part of the Portal control plane. In single-node deployments,
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
security-event delivery, execution ceilings, and report/dataset metadata requirements across enrolled machines; require peer review outside
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

## Canary (progressive) policy rollout

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

Organization policy can also make catalog metadata a hard report-publishing boundary. Configure the
Portal authority scope explicitly on every node with `Portal:PolicyAuthority:Tenant` and
`Portal:PolicyAuthority:Environment` (both default to `default`). Required tags support `REPORT`,
`DATASET`, and `COLUMN` scopes:

```json
{
  "schemaVersion": "1.0",
  "metadata": {
    "requiredTags": [
      { "tag": "@classification", "scopes": ["DATASET", "COLUMN"] },
      { "tag": "@owner", "scopes": ["REPORT"] }
    ]
  }
}
```

Before `POST /api/reports` or a script-replacing `PUT /api/reports/{id}` writes catalog state, the
Portal verifies the active envelope with the configured policy-authority public key, parses lineage
from the `.rptsql` file, and checks every declared dataset output. Missing report or dataset-column
tags return `400 organization_metadata_policy`. An invalid, expired, wrong-tenant, or unverifiable
active envelope also fails closed. This applies equally to local and federated publishers; the
Enterprise certification lane specifically exercises the boundary with an OIDC-authenticated
Publisher and confirms that a rejected request leaves no report row.

Verified policy values are added after JSON, environment variables, command-line configuration, and
test/deployment overrides, giving the authoritative enterprise policy final configuration precedence.
Operation-boundary checks additionally prevent scripts from weakening governed ceilings. Portal report
execution timeouts and paged result limits remain host availability controls rather than organization-policy
keys: scripts cannot raise them, and each host enforces its configured timeout or page limit independently.

Run `etl-sql enterprise status` to retrieve and verify policy and report `Live`, `Cached`, or `Unavailable`, the
policy version, source, issuance, expiry, governed key names, and any live-retrieval warning. Trust keys,
certificate thumbprints, signatures, and policy payload values are not printed.

## Enterprise upgrade ordering and schema compatibility

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
4. Upgrade Orchestrator, Portal workers, Report Player, CLI, TUI, language-server, and CI
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

## Enterprise outage runbooks

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

## Enterprise cache and outbox recovery rules

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

## Related

- [Enterprise machine enrollment](enterprise-enrollment.md)
- [Central security events and SIEM delivery](security-events.md)
- [Durable audit outbox and remote collectors](audit-outbox.md)
- [Platform administration](README.md)
