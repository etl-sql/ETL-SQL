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

- [ ] **P1.1 Reconcile OIDC configuration and runtime behavior** across `PortalConfig`,
  appsettings/environment variables, startup validation, and administrator documentation.
- [ ] **P1.2 Certify OIDC login, logout, and token refresh** with integration coverage for the
  authorization-code callback, session invalidation, refresh behavior, and local fallback behavior.
- [ ] **P1.3 Validate OIDC claims and issuer/audience policy** including required claims, token
  lifetime, clock skew, failed validation handling, and audit coverage for authentication failures.
- [ ] **P1.4 Map OIDC group claims dynamically to Portal groups** with deterministic membership sync,
  stale membership removal, and no privilege retention after claim changes.
- [ ] **P1.5 Document MFA and conditional-access posture** so administrators know ETL-SQL delegates
  MFA and conditional access enforcement to the identity provider.
- [ ] **P2.1 Add operational diagnostics for OIDC** including a redacted configuration check, useful
  admin-facing failure messages, and audit events for login/claim failures.
- [ ] **P2.2 Certify OIDC recovery scenarios** covering unavailable identity providers, rotated
  signing keys/JWKS cache behavior, changed group claims, disabled local users, and logout/session
  revocation.
