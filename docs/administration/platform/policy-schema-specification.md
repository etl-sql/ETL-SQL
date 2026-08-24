# Policy Schema Specification

> **Applies to:** Enterprise · SaaS

Reference for the signed policy envelope format, signature verification rules, policy payload schema, execution limits, and metadata required-tag enforcement.

> [!TIP]
> See [Authoritative Organization Policy](organization-policy.md) for the hub, [Policy Signing and Verification](policy-signing-and-verification.md) for certificate setup and canary rollout, and [Policy Enforcement Gates](policy-enforcement-gates.md) for deployment runbooks and outage recovery.

---

## Policy Envelope Format

On every process startup, an enrolled ETL-SQL installation requests a signed policy envelope from the configured HTTPS endpoint. The request carries `X-ETL-SQL-Tenant`, `X-ETL-SQL-Enrollment`, and `X-ETL-SQL-Machine` headers and presents the enrolled client certificate when configured.

The server must return JSON in this form:

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

---

## Signature Format

The signature input is the UTF-8 encoding of these six values separated by a single LF (`\n`), with timestamps formatted as UTC round-trip (`O`) values:

```text
schemaVersion
tenant
policyVersion
issuedAtUtc
expiresAtUtc
policyPayload
```

---

## Envelope Verification Rules

ETL-SQL verifies:
- Enrolled tenant matches the `tenant` field.
- `issuedAtUtc` is not before the currently cached envelope's issuance time (rollback prevention).
- `expiresAtUtc` is in the future.
- RSA-PSS SHA-256 signature is valid against the enrolled public key.
- Embedded policy payload parses as a valid policy schema.

A verified live envelope is atomically stored under the protected `Enterprise/cache` directory and is fully re-verified before offline use. Cache use ends at the earlier of envelope expiry and `MaxOfflineHours` from caching.

Long-running Portal, Report Player, and Orchestrator hosts refresh policy every five minutes. Missing, tampered, expired, or unsafe cache state fails startup when enrollment is fail-closed.

---

## Policy Payload Schema

Policy documents inside `policyPayload` use schema version `1.0`. Execution limits govern parallelism, file/recursion operations, spill volume, SMTP sends, and maximum materialized string bytes:

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

---

## Metadata Required Tags Enforcement

Organization policy can make catalog metadata a hard publishing boundary. Configure the Portal authority scope explicitly on every node with `Portal:PolicyAuthority:Tenant` and `Portal:PolicyAuthority:Environment` (both default to `default`). Required tags support `REPORT`, `DATASET`, and `COLUMN` scopes:

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

Before `POST /api/reports` or a script-replacing `PUT /api/reports/{id}` writes catalog state, the Portal:
1. Verifies the active envelope with the configured policy-authority public key.
2. Parses lineage from the `.rptsql` file.
3. Checks every declared dataset output against required tags.

Missing required tags return `400 organization_metadata_policy`. An invalid, expired, wrong-tenant, or unverifiable active envelope also fails closed. This applies equally to local and federated publishers.

Verified policy values are added after JSON, environment variables, command-line configuration, and test/deployment overrides, giving the authoritative enterprise policy final configuration precedence. Scripts cannot weaken governed ceilings.

---

## Checking Policy Status

```text
etl-sql enterprise status
```

Reports `Live`, `Cached`, or `Unavailable`, the policy version, source, issuance, expiry, governed key names, and any live-retrieval warning. Trust keys, certificate thumbprints, signatures, and policy payload values are not printed.

---

## Related

- [Authoritative Organization Policy](organization-policy.md) — overview hub
- [Policy Signing and Verification](policy-signing-and-verification.md) — certificate setup, machine registration, canary rollout
- [Policy Enforcement Gates](policy-enforcement-gates.md) — deployment runbooks, upgrade ordering, outage recovery
- [Enterprise Machine Enrollment](enterprise-enrollment.md)
- [Platform Administration](README.md)
