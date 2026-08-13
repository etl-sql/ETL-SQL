# Deployment promotion

Deployment promotion starts with a read-only inventory. The preflight separates portable scripts
and policy from catalog state, target-specific bindings, protected material, operational evidence,
and disposable runtime state before any backup, import, or cutover is attempted.

```powershell
etl-sql admin promotion preflight `
  --source C:\work\customer-pipelines `
  --from-profile Solo `
  --to-profile Team `
  --output artifacts\promotion\solo-to-team-preflight.json
```

The JSON document uses schema `etl-sql.deployment-preflight/v1` and records SHA-256 hashes for
portable artifacts and evidence. It discovers `SECRET:name` and `SHARED:alias` references as target
bindings. Key files, `.env` files, secret-named configuration, and production settings are listed
only by relative path and kind: their contents, sizes, and hashes are deliberately excluded.

Preflight never follows reparse points, never mutates the source, and returns a non-zero exit code
for unsafe conditions such as raw credential literals, unreadable paths, unsupported traversal
depth, inventory overflow, or a backward profile transition. Warnings identify protected material
that must be provisioned out of band. The output path must not already exist, preventing an
inventory run from overwriting prior evidence. A successful inventory is an input to promotion planning; it
does not itself back up, import, fence schedulers, or perform a cutover.

Supported profile names are `Solo`, `Team`, `Enterprise`, and `SaaS`. SaaS remains a distinct tenant
trust boundary; a clean preflight does not substitute for tenant-isolation certification.

## Export, validate, and import Orchestrator state

The Orchestrator promotion package complements the Portal's `EXPORT PORTAL CONFIGURATION` bootstrap.
It carries catalog definitions and eligible operational history in a provider-neutral JSON contract:

```powershell
etl-sql admin promotion export --output artifacts\promotion\orchestrator.json

etl-sql admin promotion validate `
  --package artifacts\promotion\orchestrator.json `
  --bind 'SHARED:dev-mail=SHARED:prod-mail' `
  --output artifacts\promotion\validation.json

etl-sql admin promotion import `
  --package artifacts\promotion\orchestrator.json `
  --bind 'SHARED:dev-mail=SHARED:prod-mail'
```

Schema `etl-sql.orchestrator-promotion/v1` includes jobs, schedules, notifications, attachments,
ownership attribution, bounded completed quality history, normalized quality failures, lineage, and
tags. It retains `SECRET:name` references but never resolves them; provision those names separately
in the target secret provider. The export rejects credential-like raw literals.

Validation performs no mutation. It reports duplicate logical identities, dangling attachments,
raw credentials, unsupported package versions, and target objects whose logical name is already
used by different configuration. Import runs the same validation, fails before mutation on any
collision, clears transient job run pointers, preserves historical timestamps, and converges when
the same package is replayed. Binding maps change environment-specific connection or path values;
they do not rewrite portable pipeline logic.

Portal catalog state uses the existing secret-free bootstrap:

```sql
EXPORT PORTAL CONFIGURATION TO 'portal-bootstrap.etlsql';
```

That export covers governed connections, `SECRET:` references, identities, groups, folders and
catalog ownership, ACLs, reports, dataset metadata, subscriptions, and alerts. Copy the report
scripts named in its content manifest, rebind their target paths, then replay the bootstrap through
an administrator Portal connection. Source-controlled `.etlsql`, `.rptsql`, and
`etlsql-policy.json` files travel as the portable artifacts identified and hashed by preflight.
Resolved secrets, password hashes, sessions, tokens, caches, and private keys never travel in either
configuration package.

Direct Solo/Enterprise SaaS onboarding uses `admin promotion saas-onboard`; see
[Deployment profile transitions](profile-transitions.md) for the isolated-boundary contract and
activation checklist.

For Managed Dedicated, the signed organization-policy authorization identifies the platform
operator and tenant. The command can also bootstrap the tenant's single Enterprise OIDC provider:

```powershell
etl-sql admin promotion saas-onboard `
  --tenant tenant-alpha `
  --source-profile Enterprise `
  --source C:\work\tenant-alpha `
  --output-root D:\etl-sql-tenants `
  --oidc-authority https://login.customer.example/etl-sql `
  --oidc-client-id etl-sql-portal
```

`--tenant` is only an assertion against the signed policy. The OIDC options must be supplied
together, and the authority must be HTTPS without credentials, query, or fragment. Onboarding never
accepts or writes the OIDC client secret; inject it into that tenant's Portal process through
`Portal__Identity__Oidc__ClientSecret` before activation.

The staged boundary includes `queues/audit/platform-tenant-onboarding.json`. This receipt records
the platform actor, approval reference, reason, grant expiry, tenant, and time separately from the
tenant Portal's user audit. Its `tenantUserImpersonation` value is always false: platform onboarding
does not mint a tenant session or Portal role.

Managed Dedicated deletion is the paired deployment-plane lifecycle command and requires its own
signed `saasDeletion` policy section:

```powershell
# Preflight: validates signed authority, retention, legal hold, and boundary identity.
etl-sql admin promotion saas-delete `
  --tenant tenant-alpha `
  --tenant-root D:\etl-sql-tenants\tenant-alpha `
  --receipt-root D:\etl-sql-tenant-deletion-receipts

# Execute only after reviewing the preflight and taking the required backup/export.
etl-sql admin promotion saas-delete `
  --tenant tenant-alpha `
  --tenant-root D:\etl-sql-tenants\tenant-alpha `
  --receipt-root D:\etl-sql-tenant-deletion-receipts `
  --execute
```

The completion record deliberately lives outside the deleted boundary and contains attribution,
retention/legal-hold proof, file/byte counts, and a boundary digest—not tenant payloads. The command
refuses filesystem roots, reparse points, and a receipt path nested inside the boundary.

Before replaying that bootstrap, an administrator submits it and the target binding map to
`POST /api/admin/configuration/validate`. The endpoint applies bindings only to its in-memory copy,
uses reference-shaped sentinels for unresolved password placeholders, parses the real bootstrap,
and compares logical groups, users, folders/owners, governed connections, and reports against the
target catalog. Duplicate identities, parse failures, raw credentials, and same-name/different-state
collisions are errors; unused bindings are warnings. Validation is read-only, and bootstrap replay
remains idempotent once the report is green.

## References

- [Deployment Profile Standard](../../architecture/standards/Deployment_Profile_Standards.md)
- [Deployment Profile and Portability Strategy](../../architecture/roadmaps/Deployment_Profile_Strategy.md)
- [Operator CLI](operator-cli.md)
- [Deployment profile transitions](profile-transitions.md)
