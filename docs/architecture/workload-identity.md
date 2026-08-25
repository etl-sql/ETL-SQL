# Workload Identity and Machine-to-Machine Security

ETL-SQL exchanges a signed external workload assertion for a short-lived Portal service token. The
exchange is the preferred authentication path for CI and scheduled automation. Service-account
client secrets remain available for compatibility and bootstrap recovery.

## Trust boundaries

The external issuer authenticates a workload. It does not authorize Portal access. Portal policy
maps one exact issuer and subject to an existing service account, then applies every bound together:

- **Tenant** — The binding, service account, owner, replay row, and issued token use the same tenant.
  A Shared host derives this tenant from the verified binding; a Dedicated host also compares it to
  its host-fixed tenant.
- **Issuer** — The configured HTTPS issuer is matched before discovery. GitHub is fixed to
  `https://token.actions.githubusercontent.com`; Azure DevOps is restricted to
  `vstoken.dev.azure.com`; GitLab uses an operator-configured HTTPS issuer. Discovery cannot be
  redirected by an assertion claim.
- **Subject** — Exact ordinal match. Use a protected branch/environment subject, not a repository-wide
  subject, when the provider supports it.
- **Audience** — The assertion must contain the exact configured audience and the exchange request
  must repeat it. Portal service tokens have their separate `etl-sql-portal-api` audience.
- **Resource** — The binding names one exact Portal API path. The issued token carries that path and
  middleware rejects use on every other path.
- **Owner** — Federation maps to an existing service account. Roles and Studio capabilities are
  intersected with the current human owner's authority at issue time. Resource ACLs continue to use
  that owner, and disabling the owner invalidates issued tokens.
- **Operation** — The request selects one configured operation, expressed as an existing service
  account scope. The account must currently hold it and the issued token contains only that scope.
- **Approval** — A sensitive binding sets `RequireApproval`. A different administrator issues a
  signed five-minute approval bound to tenant, binding, resource, and all approved operations. The
  approval is one-use and the service-account owner cannot self-approve.
- **Lifetime** — External assertions must carry `iat`, `exp`, and `jti`; their lifetime is capped at
  ten minutes. Issued Portal tokens retain the existing maximum fifteen-minute service-token life.
- **Replay** — Assertion and approval `jti` values are SHA-256 hashed into a database unique ledger.
  This rejects replay across Portal nodes and restarts without storing bearer material.
- **Audit binding** — Successful exchange records tenant, service account, owner, external binding,
  provider, resource, operation, assertion ID, effective scope, correlation ID, and time. Denials
  record a stable reason code without recording the assertion or approval token.

## Threat model

| Threat | Required control | Failure evidence |
| :--- | :--- | :--- |
| Assertion from another tenant or service account | Binding tenant and client ID must select one account in the same tenant; host-fixed tenant must agree | `workload_tenant_denied` or `workload_account_authority_denied` |
| Forged issuer, subject, or signature | Exact issuer/subject policy, HTTPS discovery, issuer JWKS or configured public key, signed-token requirement | `workload_policy_denied` or `invalid_workload_assertion` |
| Token confused with another consumer | Exact workload audience on input; separate Portal API issuer/audience on output | `workload_policy_denied` |
| Use against another report, job, or endpoint | Exact resource-path claim checked on every request | `workload_resource_operation_denied` |
| Operation escalation | Binding operation must be a current account scope; output contains one scope; normal middleware remains default-deny | `workload_account_authority_denied` or scope denial |
| Owner demotion, disablement, or tenant move | Roles/capabilities are recapped at exchange; owner and account state are checked on every Portal-token request | `workload_account_authority_denied` or HTTP 401 |
| Approval bypass or self-approval | Sensitive binding requires signed one-use approval; issue endpoint forbids the owner | `workload_approval_required`, `invalid_workload_approval`, or HTTP 403 |
| Long-lived assertion | Maximum assertion lifetime is 600 seconds and may be configured lower | `invalid_workload_lifetime` |
| Assertion or approval replay | Durable unique ledger on tenant, binding, and hashed `jti` | `workload_replay_rejected` or `workload_approval_replay_rejected` |
| Stolen/retired signing key | Provider JWKS refresh or replacement configured public key; old signatures fail immediately after rotation | `invalid_workload_assertion` |
| Revoked federation | Disabled/removed binding cannot be selected; revoked service account invalidates issued tokens through its security stamp | `workload_policy_denied` or HTTP 401 |
| Credential-use anomaly | Every denial and success is audit/outbox evidence with binding and correlation; raw credentials are redacted and never persisted | `WORKLOAD_IDENTITY_EXCHANGE_DENIED` or `WORKLOAD_IDENTITY_TOKEN_ISSUED` |

The main residual risk is issuer account compromise. Provider-side protected environments, branch
rules, reviewer controls, and short assertion lifetimes remain necessary. An operator who broadens a
subject or assigns a broad service account broadens the resulting authority; the Portal deliberately
does not infer narrower semantics from provider-specific subject strings.

## Exchange sequence

1. The CI runner or scheduler obtains an OIDC assertion from GitHub, GitLab, or Azure DevOps. A
   scheduler without OIDC may create a `private_key_jwt` assertion using its certificate-backed key.
2. It sends the assertion plus audience, exact API path, operation, and any approval token to
   `POST /api/auth/workload-token`.
3. Portal matches one policy, validates signature and lifetime, consumes the assertion ID, validates
   and consumes approval when required, and rechecks the service account and owner.
4. Portal returns a short-lived resource-bound service token. The caller uses it only on the exact
   path in the exchange.

`private_key_jwt` is intended for managed schedulers that cannot obtain platform OIDC tokens. Store
the private key in the scheduler's certificate/key store and configure only the public PEM in Portal.
Prefer provider OIDC for hosted CI because it removes the long-lived private credential from the job.

## Rotation and revocation

- Rotate an OIDC issuer key through its JWKS. Cached discovery refresh supplies the new key; retired
  keys stop validating after the provider removes them.
- Rotate `private_key_jwt` by replacing `PublicKeyPem` and restarting/reloading Portal configuration.
  Assertions signed by the previous key then fail.
- Disable a binding to stop new exchanges. Disable or revoke the service account to also invalidate
  already-issued Portal tokens immediately.
- Keep service-account secret rotation in the compatibility runbook. Federation does not return or
  use that secret.

## Certification evidence

`WorkloadIdentityFederationTests` covers the four providers through one policy contract, hostile
subject/audience/resource/operation/approval attempts, lifetime, replay, binding revocation, and key
rotation. `ServiceAccountIntegrationTests.CiWorkloadExchange_IsSecretlessShortLivedResourceBoundAndAudited`
covers an end-to-end exchange, durable replay, resource denial, and attributed audit. Existing
`ServiceAccountIntegrationTests.RotateExpireAndRevoke_InvalidateCredentialsAndIssuedTokens` proves
account rotation and revocation invalidate live Portal tokens.

## References

- [Portal architecture](portal.md)
- [Service accounts](../reference/portal-commands/service-accounts.md)
- [SaaS tenant isolation](saas-tenant-isolation.md)
- [Deployment profiles](deployment-profiles.md)
