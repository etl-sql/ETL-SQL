# Policy Signing and Verification

> **Applies to:** Enterprise · SaaS

Configure the RSA signing certificate, manage machine registration and key rotation, and use canary rollout for progressive policy deployment.

> [!TIP]
> See [Authoritative Organization Policy](organization-policy.md) for the hub, [Policy Schema Specification](policy-schema-specification.md) for envelope format and payload schema, and [Policy Enforcement Gates](policy-enforcement-gates.md) for deployment runbooks and outage recovery.

---

## Signing Certificate Configuration

The Portal policy authority signs published envelopes with an RSA certificate whose private key remains in the operating-system certificate store. Configure only its thumbprint — never export the private key into Portal JSON, environment variables, backups, configuration exports, logs, or support bundles:

```json
{
  "Portal": {
    "PolicyAuthority": {
      "SigningCertThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567"
    }
  }
}
```

Install the certificate in `LocalMachine/My` where possible; `CurrentUser/My` is the fallback. Grant the Portal service identity permission to use its private key. An unset thumbprint disables publication with a deterministic configuration error.

---

## Signing Key Rotation

Install and grant a replacement certificate before changing the thumbprint, and retain the former public key until enrolled clients trust the replacement.

**Rotation procedure:**

1. Generate or import the replacement RSA signing certificate.
2. Grant the Portal service identity private-key use permission.
3. Publish and validate a staged policy while the old active policy remains in service.
4. Update `Portal:PolicyAuthority:SigningCertThumbprint` to the replacement thumbprint and restart each Portal node under normal change control.
5. Publish a new policy version and verify the audit entry records `SigningKeyRotated=true`.
6. Re-enroll or re-provision machines with the replacement public key before retiring trust in the former key.

> [!WARNING]
> Do not remove the old public key from endpoint-management baselines until every enrolled machine has been re-enrolled. Machines pin the public signing key at enrollment; a machine that still trusts only the old key will reject envelopes signed by the replacement.

---

## Machine Registration

Register each enrolled machine in **Admin → Policy Authority → Machine enrollment** before or immediately after running `etl-sql enterprise enroll` on that host.

The registered tenant, environment, machine ID, enrollment ID, optional client-certificate thumbprint, and optional canary group are authoritative. The distribution endpoint ignores caller-supplied environment values and serves policy based on the registered record.

**Revoking a machine identity** makes policy retrieval fail immediately for that identity. This is the correct response to host retirement, cloned images, credential exposure, or suspected bootstrap compromise.

To reassign a host to another tenant or environment: revoke the old machine record, remove enrollment on the host, and enroll/register it as a new identity.

---

## Service Identity Least Privilege

| Service | Required permissions |
| :--- | :--- |
| **Portal service identity** | Read configuration, use policy-signing certificate private key, access Portal database, write Portal logs, access shared Portal artifact/key-ring roots. |
| **Orchestrator service identity** | Read enrollment bootstrap and protected policy cache, read scripts from approved roots, write job/session/log state, access only source systems and artifact roots required by scheduled jobs. |
| **Workstation/CLI identity** | Read its own enrollment bootstrap when enrolled. Should not receive Portal signing-key access or server-side mutation permissions. |

---

## Canary (Progressive) Policy Rollout

Before a policy change goes fleet-wide, validate it on a subset of enrolled machines. A **canary** version is published alongside — not over — the active version: only machines in its cohort receive it.

A cohort targets machines one of two ways (exactly one per canary):

- **Percentage of fleet** (1–100) — machines are selected by a stable, deterministic hash of their machine identity. Ramping up the percentage only *adds* machines (a node in the cohort at 10% stays in at 25%).
- **Named machine group** — machines labelled with that group at registration (the optional **Canary group** field on *Register machine*).

From **Admin → Policy Authority → Publish canary**, set the canary version and cohort. Each canary row offers:

- **Promote** — makes the canary the fleet-wide active version, superseding the previous active.
- **Halt** — rolls the canary back and reverts its machines. Because clients reject an envelope issued *before* the one they hold, halting re-issues the current active document as a fresh active version (a later issuance), which the cohort machines accept on their next five-minute refresh.

Only one canary can be in progress per tenant/environment at a time. Canaries are signed, versioned, and rollback-protected exactly like fleet-wide versions, and every publish/promote/halt is recorded in the mutation audit trail.

---

## Managed Dedicated Authorization Grants

Provisioning operations require short-lived signed grants inside `policyPayload`. Disable or remove each grant after the operation so a completed change does not leave reusable provisioning authority.

### Onboarding Authorization

```json
{
  "schemaVersion": "1.0",
  "saasOnboarding": {
    "enabled": true,
    "tenantId": "tenant-alpha",
    "operatorPrincipal": "provisioner@platform.example",
    "authorizationReference": "change-2026-0810",
    "reason": "Create the approved Managed Dedicated boundary",
    "expiresUtc": "2026-08-11T00:00:00Z"
  }
}
```

### Upgrade Authorization

```json
{
  "schemaVersion": "1.0",
  "saasUpgrade": {
    "enabled": true,
    "tenantId": "tenant-alpha",
    "operatorPrincipal": "release-operator@platform.example",
    "authorizationReference": "change-2026-0813-upgrade",
    "reason": "Upgrade the approved Managed Dedicated stack",
    "targetRelease": "0.18.0+build.abc123",
    "maxConcurrentJobs": 6,
    "maxStorageMb": 20480,
    "maxReportSessions": 30,
    "expiresUtc": "2026-08-14T00:00:00Z"
  }
}
```

### Deletion Authorization

Deletion uses a different signed grant so provisioning authority can never imply erasure authority. The policy must name the retention boundary and affirm that legal holds were cleared:

```json
{
  "schemaVersion": "1.0",
  "saasDeletion": {
    "enabled": true,
    "tenantId": "tenant-alpha",
    "operatorPrincipal": "privacy-operator@platform.example",
    "authorizationReference": "privacy-2026-0813",
    "reason": "Approved tenant erasure",
    "retentionUntilUtc": "2026-08-13T00:00:00Z",
    "legalHoldCleared": true,
    "expiresUtc": "2026-08-14T00:00:00Z"
  }
}
```

---

## Related

- [Authoritative Organization Policy](organization-policy.md) — overview hub
- [Policy Schema Specification](policy-schema-specification.md) — envelope format and payload schema
- [Policy Enforcement Gates](policy-enforcement-gates.md) — deployment runbooks and outage recovery
- [Enterprise Machine Enrollment](enterprise-enrollment.md)
- [Platform Administration](README.md)
