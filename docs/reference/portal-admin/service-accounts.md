# Service Accounts

Service accounts are non-interactive Portal identities for unattended API and CLI work. An
administrator provisions each account under an active portal user. The owner supplies the existing
resource permissions, while the account's stored role cap and explicit scopes can only reduce access.

## Security Contract

- Service accounts cannot receive the `Admin` role or use administration, login, password, OIDC, or
  other human-only endpoints.
- Disabling, expiring, revoking, or rotating an account invalidates already-issued service JWTs.
- Service JWTs last at most 15 minutes and have no refresh token.
- The client secret is shown only after creation or rotation. The database stores a salted password
  hash, so neither a database backup nor an administrator can recover the secret.
- Removing a role from the owner removes that role from subsequently issued service JWTs. Deactivating
  the owner blocks token issuance and invalidates existing service JWTs.
- Audit events record the service-account ID, effective scopes, owner user ID, and correlation ID.

## Scopes

| Scope | Permitted surface |
| :--- | :--- |
| `portal.read` | Authenticated read-only portal APIs, still subject to roles and resource ACLs |
| `reports.execute` | Report execution and dataset refresh operations |
| `orchestrator.execute` | Orchestrator API operations, still subject to the required portal role |

Scopes do not grant a role or resource permission. A request must pass the scope check and every
existing authorization check.

## Provisioning

Use an administrator JWT to create an account:

```http
POST /api/admin/service-accounts
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "name": "nightly-reports",
  "description": "Production reporting runner",
  "ownerUserId": 42,
  "scopes": ["portal.read", "reports.execute"],
  "roles": ["ReportRunner"],
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

Store the returned `clientId` and one-time `clientSecret` in the caller's secret manager. Do not put
them in scripts, command histories, logs, or source control. `GET /api/admin/service-accounts` lists
account metadata but never returns a secret or its hash.

## Authentication

Exchange credentials immediately before an unattended operation:

```http
POST /api/auth/service-token
Content-Type: application/json

{
  "clientId": "sa_...",
  "clientSecret": "sas_..."
}
```

Use the returned `accessToken` as `Authorization: Bearer <accessToken>`. Request a new token when it
expires; service accounts do not use the refresh-token endpoint.

## Rotation And Revocation

- `POST /api/admin/service-accounts/{id}/rotate-secret` invalidates the old secret and all issued JWTs.
  Update the caller's secret manager with the one-time replacement before restarting the workload.
- `PUT /api/admin/service-accounts/{id}` changes enabled state, expiry, or scopes and invalidates issued
  JWTs.
- `POST /api/admin/service-accounts/{id}/revoke` permanently disables the account. Revoked accounts
  cannot be rotated or restored; provision a replacement instead.

## Migration And Backup

Portal startup applies the provider-specific SQLite or PostgreSQL migration that creates the service
account table and audit actor fields. Include the portal database in normal backups. A restored backup
retains account IDs, policy, and secret hashes, but it cannot reveal client secrets. Rotate credentials
after restoring into another environment or whenever backup custody is uncertain.

## References
- [Administrators Guide](../../guides/administration.md)
