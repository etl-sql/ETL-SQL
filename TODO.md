# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Enterprise Identity & Approvals

> Status: **active (v0.14.0).**
> Goal: certify enterprise OIDC authentication before adding service accounts and approval
> workflows.
>
> Priority convention: **P1** the supported identity path that must exist before the enterprise
> identity claim; **P2** certification, recovery, compatibility, and operational verification.

### Phase 1 - Certified OIDC Authentication

- [x] **P1.1 Reconcile OIDC configuration and runtime behavior** across `PortalConfig`,
  appsettings/environment variables, startup validation, and administrator documentation.
  *(done)* Expanded `OidcIdentityConfig` to the full surface (Enabled, Authority, ClientId,
  ClientSecret, Scopes, CallbackPath, PostLoginRedirectPath, username/email/group claim types,
  ClockSkew). Added `OidcConfigValidationService` (hosted) that fails the host closed when OIDC is
  enabled but misconfigured (non-HTTPS authority, missing client id/secret, missing `openid` scope,
  bad paths); its `Validate` is a pure, reusable check. Exposed the effective posture at
  `GET /api/auth/providers` so the login page renders SSO conditionally. Updated the administrators
  guide (config example, full key table, accurate OIDC setup with redirect-URI registration and the
  fail-closed startup note). Unit tests cover every validation branch.
- [x] **P1.2 Certify OIDC login, logout, and token refresh** with integration coverage for the
  authorization-code callback, session invalidation, refresh behavior, and local fallback behavior.
  *(done)* Implemented the federated bridge: `OidcAuthenticationService` (discovery via cached
  `ConfigurationManager`, PKCE+state+nonce authorization request, code exchange, id_token validation
  against JWKS with issuer/audience/lifetime/nonce) and `OidcController` (`/api/auth/oidc/login` +
  `/callback`) carrying per-flow secrets in an encrypted HttpOnly cookie. `OidcUserProvisioningService`
  provisions/syncs the user (Provider="OIDC") and group claims, then issues the portal's own
  JWT/refresh session; refresh and logout reuse the existing provider-agnostic endpoints. Local login
  still works with OIDC enabled, and OIDC-provider accounts are blocked from the local password path.
  Login page gained an SSO button + token-fragment hand-off. Integration tests certify
  login→callback→session, refresh, logout, invalid-state rejection, disabled-account fail-closed,
  group add/stale-remove sync, local fallback, and disabled-OIDC 404; service tests certify the token
  crypto/validation paths. No new third-party dependency (uses IdentityModel already present via
  JwtBearer).
- [x] **P1.3 Validate OIDC claims and issuer/audience policy** including required claims, token
  lifetime, clock skew, failed validation handling, and audit coverage for authentication failures.
  *(done)* id_token validation enforces issuer, audience (ClientId + AdditionalAudiences), lifetime
  with configurable `ClockSkewSeconds`, JWKS signature, and nonce binding; added a configurable
  `RequiredClaims` policy that fails closed when a mandated claim is absent. All failures throw a
  single `OidcAuthenticationException` the callback turns into a redirect, and every failure path is
  audited (`LOGIN_FAILED` with reason): provider error, state/CSRF mismatch, token/claim validation,
  provider-confusion refusal, and disabled account. Service tests cover wrong audience, expired token,
  bad signature, nonce mismatch, token-endpoint failure, and required-claim present/absent.
  Security fix folded in: federated logins are now bound to OIDC accounts only — an IdP identity whose
  username matches a Local/LDAP account is refused (prevents account takeover via provider confusion).
- [x] **P1.4 Map OIDC group claims dynamically to Portal groups** with deterministic membership sync,
  stale membership removal, and no privilege retention after claim changes.
  *(done)* `OidcUserProvisioningService.SyncGroupsAsync` reconciles only Provider="OIDC" groups
  against the token's group claims (match by `AdGroup` else `Name`): idempotent (unchanged claims =
  no writes), adds newly-claimed groups, removes unclaimed ones, and never touches Local/LDAP
  memberships. On any change the user's session is invalidated (security stamp rotated + refresh
  tokens revoked) so privileges in already-issued tokens cannot persist; on a privilege reduction the
  user's anonymous share/embed links are revoked too. Integration tests certify add, deterministic
  stale removal, and that the membership change drives invalidation.
  Security fix folded in: the federated session is handed to the SPA via a server-rendered page with a
  JSON data-island read by a same-origin script — tokens (incl. the refresh token) are no longer
  placed in the URL fragment/browser history.
- [ ] **P1.5 Document MFA and conditional-access posture** so administrators know ETL-SQL delegates
  MFA and conditional access enforcement to the identity provider.
- [ ] **P2.1 Add operational diagnostics for OIDC** including a redacted configuration check, useful
  admin-facing failure messages, and audit events for login/claim failures.
- [ ] **P2.2 Certify OIDC recovery scenarios** covering unavailable identity providers, rotated
  signing keys/JWKS cache behavior, changed group claims, disabled local users, and logout/session
  revocation.

---

## Future Language Features & Engine Enhancements

> Status: **Proposed / Backlog**
> Goal: Address key user experience, performance, and scaling pain points identified in [etl_pain_points_analysis.md](file:///C:/Users/chuck/.gemini/antigravity-cli/brain/6f8a3c19-3374-4017-a650-1c74979746fa/etl_pain_points_analysis.md) and [etl_implementation_strategies.md](file:///C:/Users/chuck/.gemini/antigravity-cli/brain/6f8a3c19-3374-4017-a650-1c74979746fa/etl_implementation_strategies.md).

### Recommended Near-Term Engine Features

- [ ] **P1.1 Job-scoped state persistence / incremental watermarking** — Implement
  `GET_JOB_STATE()` and `SET_JOB_STATE()` primitives for scheduled and ad-hoc incremental loads.
  Persist state in the orchestrator store for scheduled jobs, supporting both SQLite and PostgreSQL
  HA deployments, and use a tightly scoped local `[script_name].etlstate` fallback only for CLI
  development runs. State updates should commit only after successful script completion so failed
  loads do not advance watermarks.
- [ ] **P1.2 Pushdown aggregation for staged extracts** — Allow eligible `SELECT ... INTO #temp`
  queries with `GROUP BY` and aggregate functions to execute on SQL connectors via `IDatabaseSource`
  and stream only grouped results back into the engine. This reduces source load, network transfer,
  and engine memory pressure for large SQL-backed extracts.
- [ ] **P1.3 Cross-connection semi-join pushdown** — For joins between a small local `#temp` table
  and a large remote SQL table, push a bounded, parameterized key filter into the remote query when
  the join is a simple single-column equijoin. Start with conservative limits, dialect-specific
  parameter handling, clear `EXPLAIN` visibility, and a guaranteed fallback to normal engine joins.
- [ ] **P1.4 JSON/spec-backed schema contract checks** — Extend the existing `EXPECT SCHEMA`
  capability to load expected columns/types from a reviewed JSON/spec contract when needed, rather
  than introducing a competing `ASSERT SCHEMA` syntax. Keep inline `EXPECT SCHEMA` as the canonical
  script-native form and preserve `ON DRIFT WARN` behavior.

### Lower-Priority Backlog

- [ ] **P2.1 Collection/list ergonomics review** — Do not add higher-order list functions unless a
  concrete workflow still has excessive boilerplate after using `FILE_LIST()`, `REMOTE_FILE_LIST()`,
  SQL filtering, `FOREACH`, `SORT_LIST`, `APPEND_TO_LIST`, and `REMOVE_FROM_LIST`. Prefer examples
  and table-shaped workflows first.
- [ ] **P2.2 Interactive debugging feasibility spike** — Investigate debugger hooks,
  `IDebuggerController`, DAP integration, and TUI controls as a separate IDE/runtime investment.
  Treat it as useful developer experience work, not as a prerequisite for solving incremental load,
  schema drift, or large-query scaling pain points.
