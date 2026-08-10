# Service Accounts

Service accounts are non-interactive Portal identities for unattended API and CLI work. An
administrator provisions each account under an active portal user. The owner supplies the existing
resource permissions, while the account's stored role cap and explicit scopes can only reduce access.

## Security Contract

- Service accounts cannot use login, password, OIDC, or other human-only endpoints.
- Service accounts cannot receive the `Admin` role **except** together with the `admin.identity`
  scope, which confines them to the identity-administration routes listed below. Granting `Admin`
  without that scope is refused, because it would restore unbounded reach across every
  administration endpoint.
- No service account can create or promote an `Admin`, whatever its scopes. That one operation
  requires a signed-in human. Demotion is permitted, so revoking an administrator during an
  incident does not need a browser.
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
| `admin.identity` | Identity administration only — users, groups, group membership, sessions, service accounts under constrained delegation, and read-only introspection of one user's effective access |
| `admin.portability` | Read-only access to the reviewed configuration-export plan and its hash-acknowledged download |

Scopes do not grant a role or resource permission. A request must pass the scope check and every
existing authorization check.

### `admin.identity`

Exists so a provisioning runbook or CI pipeline can manage users and groups without a browser. It is
deliberately not a blanket `admin.*`: backup and restore, configuration export unless separately
granted by `admin.portability`, environment promotion, support bundles, audit collection and export,
operational metrics, branding and orchestrator settings, service restart and shutdown, and dataset
at-rest key rotation all remain unreachable.

The reachable routes are an **explicit allowlist**, not a prefix rule, so an administration endpoint
added later is unreachable until someone opts it in on purpose:

| Area | Routes under `/api/admin/` |
| :--- | :--- |
| Users | `users`, `users/catalog`, `users/{id}`, `users/bulk-status`, `users/{id}/reset-password`, `users/{id}/revoke-tokens`, `users/{id}/disconnect` |
| Sessions | `sessions` |
| Groups | `groups`, `groups/catalog`, `groups/{id}`, `groups/bulk-delete`, `groups/{id}/studio-capabilities` |
| Membership | `groups/{id}/members`, `groups/{id}/members/catalog`, `groups/{id}/members/bulk-add`, `groups/{id}/members/bulk-remove`, `groups/{id}/members/{userId}` |
| Service accounts | `service-accounts`, `service-accounts/{id}`, `service-accounts/{id}/rotate-secret`, `service-accounts/{id}/revoke` |
| Introspection (read-only) | `permissions/effective/user/{id}`, `access-simulator/user/{id}` |

An account using this scope must **also** hold the `Admin` role — the scope never substitutes for
the role. Because the role claim is stamped when the token is issued and a service JWT lives up to
15 minutes, the owner's `Admin` assignment is re-read from the store on every identity-route
request: demoting an administrator revokes their automation's access immediately rather than at the
end of the token's life.

Service-account administration is constrained beyond the route allowlist. A service identity may
manage only accounts under its own human owner, and the target's scopes, roles, and Studio
capabilities must all be subsets of the caller's current effective claims. The same rule applies
before secret rotation, preventing a narrow provisioning account from rotating a stronger sibling
and stealing its authority. Human tenant administrators retain the full lifecycle.

### `admin.portability`

This scope permits only `GET /api/admin/configuration/export/plan` and the acknowledged
`GET /api/admin/configuration/export?acknowledgedPlan=...` download used by
`etl-sql admin tenant export`.
The existing `Admin` role and tenant authority checks still apply. Because an `Admin` role on a
service account also requires `admin.identity`, an export account currently holds both narrow
scopes; neither scope grants the other route family. The allowlist excludes configuration import,
backup, audit, operational, and every other administration endpoint.

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

For terminal automation, `etl-sql admin service-account create|rotate-secret --secret-out <new-file>`
uses create-new file semantics and never prints the one-time secret. The first CLI credential must
still be created by a signed-in tenant administrator in the Portal; there is no token-dependent
bootstrap loop. See [Admin Identity CLI](admin-identity-cli.md).

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
- [Administrators Guide](../../administration/platform/README.md)
