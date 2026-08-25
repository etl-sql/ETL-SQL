# Verified Viewer Context for Gateway PostgreSQL Resources

**Status:** Accepted and implemented for PostgreSQL Gateway resources.

## Decision

ETL-SQL supports asserted application viewer context as a separate assurance tier from delegated
database authentication. The Portal signs a short-lived operation envelope. The Gateway verifies
that envelope and installs the accepted values into PostgreSQL transaction-local custom settings.
PostgreSQL authenticates the configured service credential, not the viewer.

This decision does not implement OAuth delegated or on-behalf-of authentication and does not
implement Kerberos constrained delegation.

## Assurance tiers

| Mechanism | Identity authenticated by the database | What ETL-SQL can claim |
| :--- | :--- | :--- |
| Asserted application context | Gateway-local service credential | The Portal authenticated the viewer and the Gateway verified a signed, operation-bound assertion. Database policy may consume that assertion as application data. |
| OAuth delegated/on-behalf-of | Delegated OAuth subject, when the database validates the delegated token | The database authenticated the token subject under the provider's audience, issuer, scope, and delegation rules. This requires a separate connector design and certification. |
| Kerberos constrained delegation | Delegated Kerberos principal, when the database validates the service ticket | The database authenticated the delegated principal under the configured KDC and constrained-delegation policy. This requires a separate connector design and certification. |

Audit records and user-facing messages must use these exact boundaries. An asserted viewer is never
described as the PostgreSQL login, database principal, delegated user, impersonated database role,
or end-to-end authenticated database identity.

## Threat model

The Portal is trusted to derive the viewer from its authenticated server-side session. Browser
parameters, report parameters, script text, saved state, client-supplied headers, OIDC role/group
strings, and connector options are untrusted context sources. The broker is an untrusted transport.
The Gateway trusts a viewer assertion only after cryptographic verification.

The signed version-1 envelope binds tenant ID, Gateway ID, resource ID, operation ID and class,
effective and real viewer, expected executing credential identity, issued-at and expiry timestamps, a
256-bit random single-use nonce, the complete sorted claim map, signing key ID, and version.

The first implementation uses HMAC-SHA-256 with at least 256 bits of key material. Portal and
Gateway receive the same Base64 key through secret configuration. `KeyId` permits controlled key
replacement; an unknown key ID fails closed. The key never appears in a frame, resource record, log,
audit event, or catalog projection.

## Verification and failure behavior

For a context-enabled resource, the Gateway resolves the locally approved resource, then verifies:

1. supported envelope version and configured key ID;
2. signature with a constant-time comparison;
3. exact tenant, Gateway node, resource, operation ID, and operation-class binding;
4. exact match with the resource's expected executing credential identity;
5. issued-at, expiry, and the resource lifetime, capped at 300 seconds;
6. viewer identifiers and claim shape;
7. the resource-specific allowlist and reserved-key prohibition;
8. first use of the tenant-partitioned nonce.

Any missing verifier or envelope, unavailable key, malformed field, invalid signature, expired
assertion, binding mismatch, unknown claim, reserved key, or repeated nonce denies execution. A
resource not configured for viewer context rejects an unexpected envelope. Only the verified object
reaches the connector executor.

The Portal binds the envelope to the selected authenticated Gateway node. Each node persists its
tenant-partitioned nonce cache in its protected local Gateway state, so restart does not reopen the
replay window and another node cannot accept the envelope. The operation outcome ledger separately
prevents a completed or ambiguous mutating operation from executing again.

## Claims and reserved keys

Each resource stores an explicit allowlist. Claim names use ASCII letters, digits, underscore, or
hyphen and are limited to 64 characters. Values are limited to 2,048 characters and cannot contain
control characters. Empty allowlists are valid.

The Portal currently emits only server-derived `viewer_groups`, `viewer_roles`, `viewer_scopes`, and
`is_admin` when the resource allowlists them. Membership collections use sorted JSON arrays. Unknown
allowlisted names carry no value; there is no client, report-parameter, or script path for supplying
them.

Reserved custom keys are `tenant`, `gateway`, `resource`, `operation`, `operation_class`, `viewer`,
`real_viewer`, `executing_credential`, `issued_at`, `expires_at`, `nonce`, `roles`, and `groups`.

OIDC roles and groups are deliberately reserved. The PostgreSQL connector never issues `SET ROLE`,
never maps an OIDC role or group to a PostgreSQL role, and never uses a claim as an identifier or SQL
fragment. Database role grants remain tied to the executing service credential.

## PostgreSQL installation and pool cleanup

The Gateway creates a fresh PostgreSQL transaction before installing context. Each value is bound as
an Npgsql parameter to `SELECT set_config(@name, @value, true)`. The `true` makes every setting
transaction-local. Fixed settings are `etlsql.viewer_id`, `etlsql.real_viewer_id`,
`etlsql.executing_credential`, `etlsql.tenant_id`, `etlsql.resource_id`, and
`etlsql.operation_id`. Accepted custom claims use `etlsql.claim_<allowlisted_name>`.

The operation runs on that same connection and transaction. Success commits; cancellation, provider
failure, and disposal roll back. Commit or rollback ends every setting before Npgsql returns the
connection to its pool. A context-enabled resource using another connector fails closed.

PostgreSQL row policies can read `current_setting('etlsql.viewer_id', true)` and must deny null or
empty values. Policies must not convert `etlsql` settings into `SET ROLE` or database grants.

## Audit contract

After verification, PostgreSQL identity matching, and successful execution, the Gateway emits
`ViewerContextAccepted`.
`ActorIdentity` is the verified viewer. `EffectiveIdentity` is the expected executing credential.
Tenant, resource ID, and correlation ID are recorded. Claims, raw credentials, targets, connection
strings, signatures, and signing keys are not recorded.

Before installing any setting, the PostgreSQL connector reads `session_user` on the new transaction
and requires an exact match with that expected identity. A mismatch rolls back and denies the
operation, so the configured audit identity cannot silently drift from the database login.

## Configuration

Portal and Gateway configure the same Base64-encoded 32-byte-or-longer secret as
`ETLSQL_VIEWER_CONTEXT_HMAC_KEY`. The optional `ETLSQL_VIEWER_CONTEXT_KEY_ID` on the Gateway and
`Portal:Gateway:ViewerContextKeyId` in Portal default to `portal-gateway-v1` and must agree.

```powershell
$env:ETLSQL_VIEWER_CONTEXT_HMAC_KEY = '<base64 secret from the deployment secret store>'
etlsql gateway resource propose --resource-id corp-pg-reports --connector POSTGRES `
  --target 'Host=db.corp.internal;Database=Reports;Password=${CREDENTIAL}' `
  --credential-ref ENV:ETLSQL_GATEWAY_REPORTS `
  --executing-credential-id svc_reporting --viewer-claims viewer_groups,viewer_roles `
  --viewer-context-ttl-seconds 60 --operations READ
etlsql gateway resource approve --resource-id corp-pg-reports
```

The credential ID is the expected PostgreSQL `session_user`. It is not the password, secret
reference, or connection string. PostgreSQL verifies it against the authenticated session before
context installation.

## Certification evidence

- `VerifiedViewerContextTests` covers forgery, replay, cross-boundary binding, reserved and unlisted
  claims, hostile SQL characters, expiry, fail-closed dispatch, and dual-identity audit output.
- `PostgresTests.VerifiedViewerContext_IsParameterizedTransactionLocalAndClearedBeforePoolReuse`
  uses PostgreSQL to prove parameterized values, unchanged `current_user`, transaction lifetime, and
  absence of viewer context after connection-pool reuse.

## References

- [Secure Outbound Gateway](../../administration/platform/secure-outbound-gateway.md)
- [PostgreSQL Connector](../../reference/connectors/databases/postgres.md)
- [SaaS Tenant Isolation](../saas-tenant-isolation.md)
