# Security Model

For non-interactive API and CLI identities, see [Service Accounts](../../reference/portal-commands/service-accounts.md).

## By deployment profile

| Profile | Where the boundary is |
| :--- | :--- |
| **Solo / Workstation** | The **OS account and file permissions**. There is no Portal to authenticate against, and adding one would not make a single-operator workstation safer. |
| **Team / SME** | Portal authentication, roles and folder ACLs, over TLS. The model becomes two-axis here: a role decides the class of operation, an ACL decides which resources. |
| **Enterprise / Corporate** | Adds federated identity, service accounts capped by their owner's authority, Studio capabilities, row-level security, and a durable audit trail with optional fail-closed mutations. |
| **SaaS / Departmental** | Adds the environment boundary itself, which is **host-fixed rather than request-derived** — a caller cannot select their own tenant. Hard multi-tenant separation is not certified; see the [deployment profile review](../../releases/v0.18.0-deployment-profile-review.md). |

## Authentication

The portal uses **JWT Bearer tokens** with HMAC-SHA256 signing.

- Access tokens expire after `Jwt.ExpiryMinutes` (default 60 min).
- Every access token contains the user's Identity security stamp. Validation checks current account
  state (active flag + stamp) through a 30-second in-memory cache, so revocation takes effect
  immediately in-process and within 30 seconds across processes, without a database read per request.
- Refresh tokens expire after `Jwt.RefreshExpiryDays` (default 7 days), are stored only as SHA-256
  digests, and are single-use. Each successful refresh revokes the old token and returns a replacement.
- Replaying an already-rotated refresh token is treated as a theft signal: the request is rejected,
  every session and refresh token for that user is invalidated, and a `REFRESH_TOKEN_REUSE` audit
  event is written.
- Expired refresh-token rows are purged hourly. Revoked-but-unexpired rows are retained on purpose —
  they are the evidence reuse detection needs.
- Role, group, folder/dataset ACL, active-state, password, and LDAP mapping changes rotate the stamp and
  revoke outstanding refresh tokens for affected users.
- **Logout**, **Disconnect User**, and **Revoke Tokens** invalidate all current sessions for that user,
  including already-issued access tokens.
- Browser clients store access and refresh tokens in `sessionStorage`, not cookies. This avoids a
  cookie/CSRF authentication surface and keeps API clients on the same bearer-token model, but
  JavaScript running in the page can read the tokens. The portal therefore applies a nonce-based
  Content Security Policy, blocks inline event handlers, and does not permit arbitrary script origins.
  Do not weaken `script-src` or add `unsafe-inline`.

## Roles

Three roles are enforced at the controller level via `[Authorize(Roles = "...")]` attributes:

- **Admin** — full access
- **Publisher** — can create folders and publish reports
- **Viewer** — read and execute only

Folder-level **ACLs** provide finer control within those role boundaries.

## Managed Dedicated platform support approval

A platform operator does not receive a Portal role or tenant session. On a host-fixed Managed
Dedicated Portal, a tenant `Admin` can instead approve one redacted support-bundle disclosure for
one named platform actor and purpose for 1–60 minutes:

1. Call `GET /api/admin/support-bundle/review` and review the returned sections, exclusions, and
   full SHA-256 `contentHash`.
2. Call `POST /api/admin/support-access/approvals` with `platformActor`, `purpose`,
   `acknowledgedContent`, and `lifetimeMinutes`. Service-account tokens cannot approve access.
3. Deliver the returned capability to the named operator through an approved secret channel.
4. The operator calls `POST /api/platform/support-bundle` over TLS with the capability in the
   `X-ETL-SQL-Support-Capability` header.

The capability is signed under a key purpose and audience distinct from Portal user JWTs. It is
bound to the host-fixed tenant, exact disclosure hash, named actor, purpose, and expiry. It grants
only `support.bundle.read`; a changed disclosure, another tenant, an expired token, or a standard
Team/Enterprise/Shared host is refused. Approval, refusal, and successful download are durable audit
events; the capability value itself is never written to audit. Rotate or remove the corresponding
JWT validation key for emergency revocation. Do not place the capability in a URL, log, ticket, or
command history.

## MustChangePassword Enforcement

When a user has `MustChangePassword = true`, a middleware layer (`MustChangePasswordMiddleware`) intercepts all `POST /api/*` calls except `change-password`, `login`, `logout`, and `refresh`. Blocked requests return `403 Forbidden` with a `redirect` field pointing to the change-password page. This applies to all roles including Admin.

## Path Traversal Prevention

All script paths submitted to `POST /api/reports` are resolved to absolute paths and validated to remain within `ScriptRootPath`. A path like `../../etc/passwd` is rejected with `400 Bad Request`.

## Account Lockout

After **5 consecutive failed login attempts** an account is locked for **15 minutes** (ASP.NET Identity defaults). Lockout applies to all roles. Admins can unlock accounts by resetting the password or waiting for the lockout window to expire.

## HTTPS in Production

When `ASPNETCORE_ENVIRONMENT` is `Production`, the portal enables `UseHttpsRedirection()` and HSTS. **Always run behind a TLS-terminating reverse proxy in production.**

## Browser Security Headers and Embedding

The portal sends `Content-Security-Policy`, `X-Content-Type-Options: nosniff`,
`Referrer-Policy: no-referrer`, and a restrictive `Permissions-Policy` on every response. Portal HTML
uses a fresh script nonce per response. Same-origin framing is allowed by default; external framing is
denied.

To allow a trusted application to frame portal content, list each exact origin. Paths, wildcards, user
information, and non-HTTP schemes are rejected:

```json
"Portal": {
  "Security": {
    "FrameAncestors": [
      "https://analytics.example.com",
      "https://intranet.example.com:8443"
    ]
  }
}
```

When no external origin is configured, the portal also sends `X-Frame-Options: SAMEORIGIN`. When
external origins are configured, CSP `frame-ancestors` is authoritative and the legacy header is
omitted because it cannot express an allowlist.

## Unauthenticated Request Rate Limits

The portal applies fixed-window limits by remote IP address and endpoint path. Requests over the limit
are rejected immediately with `429 Too Many Requests` and `Retry-After: 60`; excess requests are not
queued.

```json
"Portal": {
  "RateLimit": {
    "AuthPermitLimit": 20,
    "AuthWindowSeconds": 60,
    "AnonymousTokenPermitLimit": 60,
    "AnonymousTokenWindowSeconds": 60,
    "DesignerPermitLimit": 120,
    "DesignerWindowSeconds": 60,
    "MetricsPermitLimit": 12,
    "MetricsWindowSeconds": 60
  }
}
```

The auth policy covers every `/api/auth/*` action. The anonymous-token policy covers share-link and
embed-token resolution. Designer operations use the designer policy. Prometheus scrapes use the
metrics policy and share a single-flight snapshot cache with a 15-second refresh interval. When the
portal runs behind a reverse proxy, configure ASP.NET Core forwarded
headers at the host boundary so `RemoteIpAddress` is the trusted client address; do not accept forwarded
addresses from arbitrary direct clients.

## Runtime Secret Provisioning and Rotation

Provision `Portal:Jwt:Secret`, `Portal:Orchestrator:ApiKey`, and `Orchestrator:ApiKey` through
environment variables or the deployment secret provider. The shared `AddSecureConfiguration` layer
also accepts machine-bound `ENC:` values. Do not commit plaintext production values to
`appsettings.json`.

The portal persists its ASP.NET Data Protection key ring in `Portal:Storage:KeyRingPath`, defaulting
to `.portal-keys` beside the single-node portal database.
Admin-entered Orchestrator API keys in `portal-orchestrator.json` are protected by that ring. Back up
`.portal-keys` with the portal database; losing it makes protected SMTP and Orchestrator values
unreadable. Legacy sidecars containing plaintext `ApiKey` are automatically rewritten with
`ProtectedApiKey` when first loaded.

### Rotate the JWT signing secret

1. Generate a new 256-bit-or-stronger secret.
2. Set the new value as `Portal__Jwt__Secret`.
3. Put the old value in `Portal__Jwt__PreviousSecrets__0`.
4. Restart all portal instances together. New tokens use only the new key; existing access tokens
   signed by the old key remain valid.
5. After at least `Jwt.ExpiryMinutes` plus clock skew has elapsed, remove the old value from
   `PreviousSecrets` and restart all instances.

Removing the old key immediately is an emergency revocation procedure and invalidates access tokens
and support capabilities signed with it. Refresh tokens are not JWT-signed and can still obtain a new access token unless the
user sessions are separately revoked.

### Rotate the Orchestrator API key without downtime

1. Generate a new random key.
2. Add it to `Orchestrator__PreviousApiKeys__0` while leaving the old key in
   `Orchestrator__ApiKey`; restart the Orchestrator. It now accepts both.
3. Change the portal to send the new key through `Portal__Orchestrator__ApiKey` or Admin Settings and
   verify an authenticated management request.
4. Set the new key as `Orchestrator__ApiKey`, retain the old key temporarily in
   `Orchestrator__PreviousApiKeys__0`, and restart the Orchestrator.
5. After every caller has moved to the new key, remove the old key from `PreviousApiKeys` and restart.

Keep the overlap short and record the cutover. `PreviousSecrets` and `PreviousApiKeys` are validation
rings, not permanent secret archives.

---
