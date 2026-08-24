# Policy Enforcement Gates

> **Applies to:** Enterprise · SaaS

Deploy and operate the policy authority, manage staged and emergency publications, perform enterprise upgrades in the correct order, and recover from policy authority outages.

> [!TIP]
> See [Authoritative Organization Policy](organization-policy.md) for the hub, [Policy Schema Specification](policy-schema-specification.md) for envelope format, and [Policy Signing and Verification](policy-signing-and-verification.md) for certificate setup and canary rollout.

---

## Policy Authority Deployment

Deploy the policy authority as part of the Portal control plane. In single-node deployments, the same Portal instance may host user administration, catalog administration, and policy authority operations.

In HA deployments, every Portal node must use the same PostgreSQL Portal database, the same Data Protection key ring, and the same policy-signing certificate identity; otherwise one node may publish or serve a policy envelope that another node cannot verify operationally.

Portal administrators manage the authority from **Admin → Policy Authority**. The tab validates policy JSON, publishes active or staged versions, activates staged versions, republishes emergency rollback versions, registers enrolled machines, revokes machine identities, and shows signing-key status. The same operations are available through `api/admin/policy-authority/*`.

---

## Staged and Emergency Publication

**Normal changes:** Use staged publication. Validate the policy JSON in the Portal, publish as staged, review the version hash and expiry, then activate it during the change window.

**Canary rollout:** Use canary rollout for changes that may affect path approvals, connector destinations, service-event delivery, or execution ceilings. Start with a named operations group or a low percentage, confirm policy refresh and job behavior, then promote or halt.

**Emergency publication:** For immediate containment (blocking a compromised connector destination, disabling a dangerous filesystem root, or tightening security-event fail-closed thresholds). Publish the emergency policy with a short expiry and a distinct version name, verify at least one enrolled node has refreshed it, and record the operational reason in the change record. After containment, publish a normal reviewed policy.

> [!CAUTION]
> If the emergency policy is wrong, use rollback or halt-canary rather than editing the underlying database. Direct database edits bypass signing, version history, and audit guarantees.

**Unenrollment** is a governance event, not a routine troubleshooting shortcut. Permit `etl-sql enterprise unenroll --yes` only during approved decommissioning, lab rebuilds, or recovery from a malformed bootstrap. For production hosts, revoke the machine identity in the Portal before or immediately after unenrollment, then remove or rotate service credentials. If a team needs a temporary policy bypass for incident response, prefer a signed emergency policy or a short-lived canary/rollback action.

---

## Enterprise Upgrade Ordering and Schema Compatibility

Enterprise enrollment, policy, and security-event delivery are versioned and fail closed when a host receives a schema it does not understand.

**Upgrade order:**

1. Back up the Portal and Orchestrator databases, Portal artifact roots, Data Protection key ring, signing-certificate deployment records, and policy-authority state. Confirm the rollback plan before changing schemas or publishing policy.
2. Upgrade the security-event collector and remote audit collector first. During a mixed-version window, collectors must accept both the current schema and the next schema, dedupe by event ID, and keep explicit acknowledgement behavior. Do not enable fail-closed thresholds for a new event schema until collector acceptance is proven.
3. Upgrade Portal/policy-authority nodes and apply database migrations before publishing envelopes or policies that use new schema versions or new policy keys. In HA deployments, let the normal migration/lease ownership path run once; do not start two incompatible Portal builds against the same database.
4. Upgrade Orchestrator, Portal workers, Report Player, CLI, TUI, language-server, and CI runner hosts that consume enterprise policy. Keep the active policy within the oldest supported schema until those hosts report healthy status.
5. Publish a staged or canary policy that still uses the shared supported schema. Verify `etl-sql enterprise status`, fleet health, policy version, refresh time, security-event delivery, and audit delivery on the canary cohort before promoting.
6. Only after the fleet and collectors are upgraded should you publish a policy or envelope that requires the newer schema.

**Compatibility rules:**
- Older enrolled clients reject unsupported bootstrap, envelope, policy, and security-event schemas rather than guessing.
- New binaries must continue to read supported existing enrollment and cache files for the documented compatibility window.
- Signed policy rollback protection is based on envelope issuance time. When reverting a bad canary or active policy, publish or halt through the policy authority so the replacement envelope has a later issuance time.
- Policy payload additions that old clients ignore are acceptable only when the default behavior is safe. Any mandatory enforcement change needs a schema or capability gate.

Before closing an upgrade window, run `etl-sql enterprise status` on representative enrolled hosts and check Portal `GET /health` for policy-authority and collector health.

---

## Enterprise Outage Runbooks

### Policy Authority Unavailable

Symptoms: `policy-authority` degraded in Portal `GET /health`, policy retrieval failures, or enrolled hosts falling back to `Cached` policy.

1. Confirm whether the Portal process, Portal database, signing certificate, load balancer, and TLS certificate chain are healthy.
2. If the active policy is still cached and unexpired, leave enrolled hosts running and restore the authority before cache freshness expires.
3. Do not unenroll production hosts to work around an authority outage.
4. If cache expiry is imminent, publish no new policies until the authority is stable; recover the Portal node or fail over to a node with the same Portal database, Data Protection key ring, and policy-signing certificate.

### Policy Signing Certificate Expired or Inaccessible

Symptoms: degraded `policy-authority` health check, publication failures, or enrolled clients rejecting newly served envelopes.

1. Restore access to the configured `Portal:PolicyAuthority:SigningCertThumbprint` certificate: verify it is installed in the expected store, has a private key, chains to the expected trust root, and grants private-key use to the Portal service identity.
2. If the certificate is expired or compromised, install the replacement, grant access, update the thumbprint, restart Portal nodes, publish a staged policy, and verify a canary refresh.
3. Machines pin the enrollment public key, so re-enroll affected machines before retiring the old public key.

### Invalid Policy Publication

Symptoms: canary execution denials, `PolicyValidationFailure` security events, or hosts reporting policy refresh errors after activation.

1. If the problem is in a canary, halt it from **Admin → Policy Authority** so the authority re-issues the active policy with a later issuance time.
2. If the bad policy is active fleet-wide, use rollback or emergency publication through the policy authority, then verify `etl-sql enterprise status` on representative hosts.
3. Do not repair by editing `PolicyVersions` rows or cache files: clients reject older envelope issuance times, and manual edits bypass signature, audit, and rollback protection.

### SIEM or Security-Event Collector Outage

Symptoms: collector reachability failures, increasing pending count or oldest pending age, terminal delivery failures, and fail-closed denials when signed thresholds are exceeded.

1. Confirm the collector endpoint, DNS, TLS certificate, client-certificate trust, and firewall path.
2. If the collector is intentionally down and fail-closed thresholds are not yet breached, restore the collector and let local outboxes drain; events are retried and deduplicated by `eventId`.
3. If thresholds are breached and production work is blocked, prefer restoring the collector or increasing capacity. Only publish a temporary emergency policy that relaxes fail-closed thresholds when the organization accepts the audit-delivery risk, and follow it with a normal reviewed policy after recovery.

### Disk Exhaustion or Outbox Full

Symptoms: outbox write failures, `SecurityEventOutboxFullException`, Portal audit outbox backpressure, growing `AuditOutboxMessages`, or host disk alerts.

1. Free space on the affected volume. Move logs or non-authoritative artifacts first.
2. Preserve `Enterprise/enrollment.json`, `Enterprise/cache`, `security-events.db`, Portal database files, and Data Protection keys.
3. Do not delete pending outbox rows to clear a fail-closed condition unless the business decision is to lose audit/security evidence; if that decision is made, record it outside ETL-SQL and prefer retaining a forensic copy before removal.
4. After space is restored, restart affected services and verify backlog counts decrease.

### Fail-Closed Fleet Recovery

When many hosts stop because policy, audit, or security-event delivery is unhealthy, recover the control plane before weakening policy.

1. Check Portal `GET /health`, fleet status, policy version and expiry, audit outbox pending/failed counts, security-event pending/failed counts, oldest pending age, outbox bytes, and collector reachability.
2. Recover in this order: Portal database and policy authority, signing certificate access, collector endpoints, disk capacity, then enrolled hosts.
3. After the control plane is healthy, restart a small canary cohort, verify `Live` policy status and draining event queues, then restart the rest of the fleet.
4. If emergency policy relaxation was used, publish a reviewed policy restoring normal fail-closed thresholds before closing the incident.

---

## Enterprise Cache and Outbox Recovery Rules

Treat machine enrollment state as host identity, not application configuration. The protected `Enterprise/enrollment.json` file contains the tenant, policy endpoint, pinned policy-signing public key, enrollment ID, machine ID, optional client-certificate thumbprint, and offline/fail-closed settings.

**Backup and restore rules:**

1. **Same physical machine, same tenant/environment** — Restore `enrollment.json` and `Enterprise/cache` from a host-level backup when recovering the same machine after disk loss. Run `etl-sql enterprise status`. If status cannot verify the bootstrap or cache, repair by revoking the old machine record and re-enrolling.
2. **Replacement machine or cloned VM/image** — Do not restore `enrollment.json`, `Enterprise/cache`, or `security-events.db`. Register and enroll the replacement as a new machine identity. Revoke the retired machine identity in the Portal.
3. **Cross-environment restore** — Never reuse the original machine enrollment, policy cache, security-event outbox, service-account secrets, connector credentials, or client certificate in a different tenant or environment. Re-enroll the host against the target policy authority so the new tenant/environment binding is explicit and audited.
4. **Policy-cache recovery** — The cache is a fallback for the enrolled machine that wrote it. It is re-verified before offline use and rejected if expired, tampered with, issued for the wrong tenant, older than the currently trusted issuance, or outside `MaxOfflineHours`. A restored cache can help a same-machine recovery survive a short policy-authority outage; it is not a portable policy artifact.
5. **Security-event outbox recovery** — Preserve the enrolled machine's `security-events.db` only for same-machine recovery. Events are idempotent by event ID, so restored pending events may be retried safely by a deduplicating collector. Do not move an outbox to a different machine identity.

Portal audit outbox state is different. Portal audit rows and `AuditOutboxMessages` live in the Portal database and are part of the Portal backup/restore set. Restore them with the Portal database so pending remote-audit delivery can resume after a same-environment disaster recovery.

> [!CAUTION]
> If a database backup is restored into a non-production environment, change or remove `Portal:Audit:TransportEndpoint` and collector credentials before starting the Portal, or the restored environment may forward old production audit rows to the production collector.

---

## Related

- [Authoritative Organization Policy](organization-policy.md) — overview hub
- [Policy Schema Specification](policy-schema-specification.md) — envelope format and payload schema
- [Policy Signing and Verification](policy-signing-and-verification.md) — certificate setup, machine registration, canary rollout
- [Enterprise Machine Enrollment](enterprise-enrollment.md)
- [Central Security Events and SIEM Delivery](security-events.md)
- [Durable Audit Outbox and Remote Collectors](audit-outbox.md)
- [Platform Administration](README.md)
