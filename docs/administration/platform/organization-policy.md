# Authoritative Organization Policy

> **Applies to:** Enterprise · SaaS

Signed, centrally published organization policy for enrolled machines — the Enterprise counterpart to the source-controlled workspace policy.

> [!NOTE]
> **Solo/Workstation users:** Your policy is the source-controlled workspace policy `etlsql-policy.json` — checked in, versioned with the scripts it governs, and enforced by the CLI. See the [one-person quality loop](../../guides/patterns/one-person-quality-loop.md). This document does not apply.

---

## By Deployment Profile

| Profile | What applies |
| :--- | :--- |
| **Solo / Workstation** | Not this document. Use the source-controlled workspace policy `etlsql-policy.json`. |
| **Team / SME** | Workspace policy still applies and is usually enough. A signed organization authority is available but is not where most teams should start. |
| **Enterprise / Corporate** | Everything here: signed policy envelopes, a private key that never leaves the OS certificate store, machine enrollment, canary rollout and rollback. Check `GET /api/admin/policy-authority/impact` before activating. |
| **SaaS / Departmental** | As Enterprise, with policy authority **scoped per tenant or environment**. Tenant-specific policy authority and platform separation are **not certified**; do not infer them from the Enterprise path. |

---

## Getting Started: Signing Certificate

The Portal policy authority signs published envelopes with an RSA certificate whose private key remains in the operating-system certificate store. Configure only its thumbprint — never export the private key into Portal JSON, environment variables, backups, logs, or support bundles:

```json
{
  "Portal": {
    "PolicyAuthority": {
      "SigningCertThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567"
    }
  }
}
```

Install the certificate in `LocalMachine/My` where possible; `CurrentUser/My` is the fallback. Grant the Portal service identity permission to use its private key.

---

## Organization Policy Guides

| Guide | What it covers |
| :--- | :--- |
| [Policy Schema Specification](policy-schema-specification.md) | Signed envelope JSON format, signature input format, verification rules, offline cache, policy payload schema v1.0, and metadata required-tag enforcement |
| [Policy Signing and Verification](policy-signing-and-verification.md) | Certificate configuration, signing key rotation, machine registration, service identity least privilege, canary rollout, and Managed Dedicated authorization grants |
| [Policy Enforcement Gates](policy-enforcement-gates.md) | Policy authority deployment runbook, staged/emergency publication, enterprise upgrade ordering and schema compatibility, outage runbooks (6), and cache/outbox recovery rules |

---

## Related

- [Enterprise Machine Enrollment](enterprise-enrollment.md)
- [Central Security Events and SIEM Delivery](security-events.md)
- [Durable Audit Outbox and Remote Collectors](audit-outbox.md)
- [Platform Administration](README.md)
