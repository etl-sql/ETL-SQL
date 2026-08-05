# User Management

Creating and managing Portal accounts, and connecting them to an enterprise identity provider.

## By deployment profile

| Profile | What applies |
| :--- | :--- |
| **Solo / Workstation** | **N/A.** There is one operator and no account system; the OS login is the identity. |
| **Team / SME** | Local Portal accounts and groups, created here. Change the first-run `admin` password and remove the bootstrap value from configuration. |
| **Enterprise / Corporate** | OIDC or LDAP as the identity source, with groups reconciled on every login. Keep at least one **local** administrator able to sign in when the provider is unreachable — `GET /api/admin/identity/diagnostics` reports whether one exists. |
| **SaaS / Departmental** | As Enterprise, **per environment**. A token minted in one environment is refused by another, so an operator who works across departments needs an account in each. |

Open **Admin → Users** to manage accounts.

The user catalog is server-paged. Use the search box and status filter to narrow large account lists, then select rows on the current page to enable or disable multiple users. Selection is page-local and is cleared when the filter or page changes.

## Enterprise Identity Path

The portal supports integration with enterprise identity providers via two primary paths: **OpenID Connect (OIDC)** and **LDAP / Active Directory (AD)**.

### OpenID Connect (OIDC)
Microsoft Entra ID is the reference provider (Keycloak, Okta, Auth0, and any compliant provider work the same way). Federated login uses the **authorization-code flow with PKCE**: the portal redirects the browser to the provider, validates the returned `id_token` against the provider's JWKS (issuer, audience, lifetime, and a per-login nonce), then **bridges the identity to the portal's own JWT/refresh-token session** — exactly like a password or LDAP login. Users are provisioned into the portal identity store on first login, and the configured group claims map to portal groups for ACL resolution. Local login keeps working alongside OIDC, so administrators retain a break-glass path.

**1. Register the portal as a confidential web application** with your identity provider and add the redirect URI:

```
https://<portal-host>/api/auth/oidc/callback
```

Record the client id and generate a client secret.

**2. Configure `appsettings.json`** (supply the secret via the `Portal__Identity__Oidc__ClientSecret` environment variable in production):
```json
{
  "Portal": {
    "Identity": {
      "Provider": "Oidc",
      "Oidc": {
        "Enabled": true,
        "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "ClientId": "<application-client-id>",
        "ClientSecret": "<client-secret>",
        "TenantId": "<tenant-id>",
        "Scopes": [ "openid", "profile", "email" ],
        "GroupClaimTypes": [ "groups", "roles" ]
      }
    }
  }
}
```

When `Enabled` is `true` the portal validates this configuration at startup and **refuses to start** if `Authority` is missing or not HTTPS, `ClientId`/`ClientSecret` are missing, or `openid` is absent from `Scopes` — failing closed rather than serving broken authentication.

**3. Map group claims to portal groups (optional).** Create portal groups with `Provider = "OIDC"` and set each group's `AdGroup` to the value the provider emits in the `groups`/`roles` claim (or leave `AdGroup` empty to match the group `Name`). Membership is reconciled deterministically on every login — claimed groups are added and unclaimed ones removed — so access follows the identity provider as claims change.

**4. Sign in.** The login page shows a **Sign in with SSO** button when OIDC is enabled. The portal exposes the effective posture (anonymous) at `GET /api/auth/providers` so the page renders the right options.

#### MFA and conditional access
ETL-SQL **delegates multi-factor authentication and conditional access entirely to the identity provider** — it does not implement its own MFA, device trust, or location/risk policies. Authentication strength is decided by the IdP during the authorization-code redirect; the portal only validates the resulting `id_token` and bridges it to a portal session. Practical implications:

- **Configure MFA, conditional access, device compliance, and risk policies in the IdP** (for example Entra ID Conditional Access, Okta sign-on policies, Keycloak authentication flows). They apply to ETL-SQL automatically because every federated login goes through the provider.
- **Session lifetime is governed at two layers.** The IdP controls how often a user must re-authenticate (and re-satisfy MFA/CA); the portal controls its own issued token lifetime via `Jwt.ExpiryMinutes` / `Jwt.RefreshExpiryDays`. Set the portal lifetimes no longer than your IdP session policy so re-evaluation happens on schedule.
- **Step-up / claim requirements.** Use `Identity.Oidc.RequiredClaims` to mandate claims the IdP only emits after a policy is satisfied (for example an `acr`/`amr` or tenant claim); logins missing them are refused.
- **Break-glass.** Local login remains available alongside OIDC, so a local administrator can still sign in if the IdP is unreachable. Keep at least one local admin account and protect it accordingly.

#### Operational diagnostics
Administrators can verify federated login health at `GET /api/auth/oidc/diagnostics` (Admin role). It returns the effective OIDC configuration **with the client secret reduced to a `clientSecretConfigured` flag**, the startup validation errors (if any), and a live discovery probe (issuer, endpoints, and JWKS signing-key count, or a redacted error when the provider is unreachable). Authentication failures — provider errors, state/nonce mismatches, token/claim validation failures, and refusals — are recorded in the audit log as `LOGIN_FAILED` with a reason.

### LDAP / Active Directory (AD)
LDAP bind authentication enables directory verification for user logins, auto-provisioning of user metadata (email, display name), automatic role assignments based on security groups, and dynamic synchronization of portal group memberships.

To enable and configure LDAP, update `appsettings.json` under `"Identity"`:
```json
{
  "Portal": {
    "Identity": {
      "Provider": "Local",
      "Ldap": {
        "Enabled": true,
        "Server": "domaincontroller.corp.local",
        "Port": 389,
        "UseSsl": false,
        "AllowSelfSignedCertificates": false,
        "Domain": "corp.local",
        "BaseDn": "OU=Users,DC=corp,DC=local",
        "ServiceUser": "",
        "ServicePassword": "",
        "RoleMappings": {
          "CN=GG-Portal-Admins,OU=Groups,DC=corp,DC=local": "Admin",
          "GG-Portal-Publishers": "Publisher"
        }
      }
    }
  }
}
```

#### Key LDAP Integration Details:
1. **Login Bind & Username Formats**: When `Ldap.Enabled` is `true`, users can log in using either their simple username, a domain-qualified format (`CORP\username`), or a User Principal Name (UPN) format (`username@corp.local`). If the user does not exist in the database, the portal authenticates them against the directory, maps roles/groups, and auto-provisions a `PortalUser` record with their directory metadata (`displayName`, `mail`, `givenName`, `sn`).
2. **Local User Fallback**: Local portal accounts (users configured with `Provider == "Local"`, such as the default `admin` account) bypass LDAP authentication entirely and authenticate against local hashes. This ensures that administrators can always log in using a local emergency account even if active directory is down or unreachable.
3. **Password Changes**: Password change requests via the `/api/auth/change-password` endpoint are strictly blocked for accounts authenticated via LDAP. All password policy enforcement and resets must be handled on the directory level.
4. **Role Mappings**: Active Directory group memberships (retrieved via the standard `memberOf` user attribute) are matched against the configured `RoleMappings`. Users will automatically be assigned portal roles (`Admin`, `Publisher`, `Viewer`) corresponding to their active directory security groups.
5. **Group Synchronization**: Portal groups created with `Provider = "LDAP"` automatically synchronize their member lists against Active Directory security groups during login:
   - The user is added to matching LDAP portal groups they belong to in AD.
   - The user is removed from any LDAP portal groups they no longer belong to in AD.
   - **Safety Boundary**: Local portal groups (`Provider == "Local"`) are completely ignored during this synchronization, allowing manual group assignments to be preserved.
6. **Removed Directory Users**: Removing or disabling a user in the directory prevents their next LDAP login, but the Portal does not poll the directory for account lifecycle changes. Disable the corresponding Portal account in **Admin → Users** as part of the offboarding workflow. Disabling the Portal account revokes refresh tokens and causes already-issued access tokens to be rejected on their next request.
7. **Recovery Administration**: Keep at least one tested local Admin account. Local accounts bypass LDAP authentication, allowing an operator to disable stale LDAP accounts or correct mappings when the directory is unavailable.

#### Scripted LDAP Administration:
Administrators can script-manage LDAP users and groups inside `EXECUTE portal BEGIN...END` blocks:
```sql
-- Creating an AD / LDAP user (password is optional/ignored)
CREATE USER 'john' WITH (
  EMAIL    = 'john@corp.local',
  ROLE     = 'Publisher',
  PROVIDER = 'LDAP'
);

-- Creating a group mapped to a specific Active Directory security group
CREATE GROUP 'Finance Viewers' WITH (
  DESCRIPTION = 'Portal representation of AD Readers group',
  PROVIDER    = 'LDAP',
  AD_GROUP    = 'CN=GG-Finance-Readers,OU=Groups,DC=corp,DC=local'
);

-- Match by Name (Default when PROVIDER = 'LDAP' and AD_GROUP is omitted)
CREATE GROUP 'GG-Finance-Readers' WITH (
  DESCRIPTION = 'Finance Report Viewers AD Group',
  PROVIDER    = 'LDAP'
);
```

## Roles

| Role | What they can do |
| :--- | :--- |
| **Admin** | Everything — full user/group/folder management, SMTP configuration, audit log, Orchestrator management |
| **Publisher** | Create folders, publish reports, manage subscriptions |
| **Viewer** | Browse accessible folders, run and export reports, manage their own subscriptions |
| **OrchestratorManager** | Orchestrator tab only — create/edit/delete/trigger/kill scheduled jobs, view execution history. Cannot access the Admin panel. |
| **GovernanceViewer** | Read the governance dashboard — estate posture, scores and the rules behind them, badges, findings, and the decisions on them. No mutations. |
| **DataSteward** | Everything GovernanceViewer can do, plus the steward decisions: ignore a finding as a false positive, accept a risk, reopen, mark an asset reviewed, and assign badges. Also gates the data-quality quarantine queue. |
| **GovernanceManager** | Everything DataSteward can do, plus configuration: run scans, change the score threshold and enabled checks, manage glossary terms, and manage suppression categories. |

<a name="governance-roles"></a>
The three governance roles are separate authorities, not a convenience ladder:

- **Reading is deliberately wide.** A steward who cannot see other stewards' work cannot cover for
  them, and a governance lead needs the whole estate. `?scope=mine` narrows the queue to the caller,
  but it is a filter they choose, never a boundary imposed on them.
- **Deciding is steward judgement.** Ignoring a finding or accepting a risk requires a written reason
  and is recorded against the asset version it was decided on. When that asset changes, the
  suppression stops applying and the finding reopens — so a decision cannot quietly cover content
  nobody reviewed.
- **Configuring changes what "governed" means** for everyone. Whoever can lower the threshold should
  not be the same person working against it, which is why `DataSteward` cannot: a steward able to
  clear their own queue by moving the bar is not being held to one.

Every governance mutation is audited. Threshold changes record the value **before** as well as after,
because "who lowered the threshold" cannot be answered from the new value alone.

<a name="orchestrator-manager-role"></a>
Assign `OrchestratorManager` to operations staff who need to manage the ETL-SQL Orchestrator from the web UI without needing full admin rights. A user with only this role can see and use the Orchestrator tab but has no access to user management, groups, folders, audit logs, or report publishing.

## Creating a User

Click **New User** and fill in:

- **Username** — unique login name
- **Email** — used for subscription delivery
- **Password** — must be at least 8 characters with at least one digit
- **Role** — Admin, Publisher, or Viewer

New users created by an administrator always have `MustChangePassword = true`. They will be prompted to set their own password on first login.

## Editing a User

Click a user row to open their profile. You can change their name, email, role, and active status. **Deactivating a user** (`IsActive = false`) prevents login and blocks all API calls using their tokens.

## Resetting a Password

Use **Reset Password** on the user's profile to force a new temporary password and set `MustChangePassword = true`. The user will be prompted to change it on their next login.

## Revoking Sessions

**Revoke Tokens** immediately invalidates all refresh tokens for that user, ending all active sessions. Use this if an account is believed to be compromised.

## Deleting a User — Ownership Lifecycle

Deleting a user distinguishes durable shared resources from personal artifacts:

- **Durable resources must be reassigned first.** If the user owns folders, published reports, or
  datasets, the delete returns `409 Conflict` with a count of each. Retry with
  `DELETE /api/admin/users/{id}?reassignTo=<userId>` naming a different, active user; ownership of
  all three transfers in one operation and a `TRANSFER_OWNERSHIP` audit event records the counts
  and the target.
- **Personal artifacts die with the user.** Subscriptions (including their Orchestrator jobs and
  generated trigger scripts, which are removed immediately), alerts, saved views, favorites,
  share links, embed tokens, and refresh tokens are deleted — they are personal capabilities, not
  shared state. Active subscriptions still require the explicit `?cascade=true` acknowledgement.

## LDAP Account Lifecycle Boundary

LDAP synchronization happens **at login only** — there is no background directory sweep:

- A user removed from the directory simply can no longer authenticate (the LDAP bind fails). Their
  portal account, ownerships, and grants remain until an administrator deactivates or deletes the
  account using the lifecycle above. Synchronization never deactivates or deletes accounts.
- On each successful LDAP login the user's memberships in `Provider = 'LDAP'` groups converge to
  the directory's group list (additions and removals), and mapped roles are applied. Local groups
  are never touched by synchronization.
- An LDAP-mapped group removed from the directory keeps its portal row and ACLs; it loses members
  one at a time as they log in. Delete the group in the portal when it is no longer wanted.

---
