# Row-Level Security via Injected Identity — Design Draft

> **Status:** draft for review (2026-07-01). Derived from an administrator operational review.
> Tracked in `TODO.md` under *Administrator operational review — follow-on hardening*.
> Nothing here is implemented yet; the "Current state" section is verified against the code.

## Goal

Let report authors write row-filtering predicates keyed on **who is running the report** and **what
groups/roles they hold**, such that the identity cannot be forged by the requesting user and cannot
leak across viewers through snapshot caching.

## Current state (verified against code)

- **No identity system variable exists.** `SystemVariableProvider` exposes only `@@TRANCOUNT`,
  `@@VERSION`, `@@ROWCOUNT`, `@@ERROR`, and telemetry counters.
- **The host knows the caller but does not surface it.** The Portal passes
  `DatasetCallerContext = "UserId=…;IsAdmin=…"` into execution (`ExecutionJobService` →
  `DashboardService` → `Evaluator.DatasetCallerContext`), but it is consumed only for PUBLIC/PRIVATE
  dataset gating — never exposed to report SQL.
- **`@@` immutability is implicit, not enforced.** The lexer reads `@@X` and `@x` as the same
  `VARIABLE` token. Two accidental layers prevent a write today: `SetVariableStatementHandler`
  requires the target to be declared first, and `GetVariable` routes any `@@` read through
  `SystemVariableProvider` before consulting user variables. Nothing *rejects* `DECLARE @@X`, and the
  read protection is a resolution-order side effect that a future refactor could remove. For a
  security control this must become explicit.
- **Role model already distinguishes author from admin.** Seeded Portal roles are `Admin`,
  `Publisher`, `Viewer`, `OrchestratorManager`, `FleetReader`. **`Publisher` is the report-writer
  role.** This distinction drives the impersonation model below.

## Proposed surface

### System variables (read-only, host-injected)

| Variable | Type | Meaning |
| :--- | :--- | :--- |
| `@@CURRENT_USER` | string | Effective username the report runs as (the impersonated user under impersonation). Null when no identity. |
| `@@CURRENT_USER_ID` | int | Effective immutable user id. |
| `@@REAL_USER` | string | The actual actor, unchanged by impersonation. Equals `@@CURRENT_USER` when not impersonating. |
| `@@IS_ADMIN` | bool | Whether the **effective** identity is an administrator. |

RLS predicates use `@@CURRENT_USER` (the effective identity). Audit uses `@@REAL_USER`.

### Predicate functions (primary filtering primitives)

- `HAS_GROUP('name')` → bool; true if the effective identity is in that group.
- `HAS_ROLE('name')` → bool; Portal role membership.
- **Phase 2:** table-valued `USER_GROUPS()` returning one row per group, for
  `WHERE r.RegionCode IN (SELECT g FROM USER_GROUPS())`.

`HAS_GROUP()` is the headline primitive rather than a delimited `@@USER_GROUPS` string, because a
scalar string invites `LIKE '%region%'` substring bugs that silently over-match. A raw
`@@USER_GROUPS` may exist for display, but **filtering** goes through set-membership.

**Group/role matching is case-insensitive** (`OrdinalIgnoreCase`) — consistent with the rest of the
identity layer (LDAP/OIDC role mapping already compares case-insensitively).

### Group/role source

The effective principal's Portal role assignments **plus OIDC group claims**. OIDC group→group
mapping is a required enterprise input, not optional: enterprises manage membership in their IdP, and
RLS is only useful if those groups reach the predicate. Groups are materialized **once** at execution
start into an in-memory set; `HAS_GROUP` is an O(1) lookup, never a per-row re-fetch.

## Injection boundary (the trust anchor)

Identity flows through a **host-only** channel, parallel to `DatasetCallerContext`: a new
`ExecutionIdentity` value (effective user id, username, roles, groups, isAdmin, plus the real actor)
set by the Portal from the authenticated `ClaimsPrincipal` in `ExecutionJobService` /
`DashboardService`. `SystemVariableProvider` reads from it.

It is **never** populated from report parameters, `SetParametersAsync`, environment, or saved
sessions — the namespaces a user can influence. Parameters and identity are separate worlds; a
`?CURRENT_USER=` query parameter cannot reach `@@CURRENT_USER`.

## Immutability enforcement

- Reject `DECLARE`, `SET`, and `OUTPUT`-parameter targets beginning with `@@`, at the statement
  handler (ideally at parse) with a clear error.
- Keep `GetVariable` routing every `@@` read through the provider, and document it as a security
  invariant so it survives refactors.

## Admin bypass

**Administrators bypass RLS by default.** When the effective `@@IS_ADMIN` is true, RLS predicates that
follow the documented pattern short-circuit to "see all". This is a deployment policy toggle
(`Portal:Security:AdminBypassRowLevelSecurity`, default **on**) so security-sensitive shops can turn
it off and filter admins too. The bypass keys on the **effective** identity, so an admin impersonating
a non-admin user correctly sees only that user's rows.

## Impersonation / run-as

Two distinct capabilities, both host-enforced — a script can never self-impersonate.

### Admin real impersonation (support / reproduction)

An `Admin` may run a report as a **real named user** to reproduce "what does Jane see." Because admins
already bypass RLS and can see all rows, impersonation only **narrows** their view — it grants no new
data access. The effective identity becomes Jane's; `@@REAL_USER` stays the admin. Fully audited.

### Publisher preview-as (authoring / testing)

A `Publisher` (report writer) may preview a report they can edit using a **simulated identity** — a
chosen set of groups/roles ("preview as groups: Region:East, Level:Manager"), not necessarily a real
user. This tests the predicate without targeting a specific person. Constraints:

- Scoped to reports the Publisher can edit.
- Non-persistent and **never cached as a shared snapshot** (see below).
- Audited with the real actor and the simulated claim set.
- **Data-access note (open decision):** preview runs the real query filtered by the simulated
  identity, so a Publisher can see rows for groups they don't personally belong to. That is inherent
  to authoring RLS reports over sensitive data. If that exceeds a deployment's tolerance, preview-as
  must be gated by a separate grant (`Publisher.CanPreviewAsArbitraryGroups`) or restricted to a
  non-production data copy. Flagged for the security review, not silently allowed.

### Enforcement

- Impersonation is requested through the Portal API / preview surface, which sets `ExecutionIdentity`
  to the impersonated/simulated principal and records the **real** actor separately.
- Any in-script impersonation directive is honored **only** when the executing principal already holds
  impersonation authority (Admin, or Publisher within preview scope); a Viewer-run script that
  attempts impersonation is rejected at the host.
- Every impersonated execution logs both real and effective identity. Non-negotiable.

## Snapshot & subscription interaction

A report is **identity-sensitive** if a static scan of its script (same mechanism as `DmlDetector`)
finds any reference to `@@CURRENT_USER*`, `@@REAL_USER`, `@@IS_ADMIN`, `HAS_GROUP`, `HAS_ROLE`, or
`USER_GROUPS()`. Auto-detected so authors cannot forget to flag it. When flagged:

- **No shared snapshot** — results are never served from a cache computed under a different identity.
- **Subscriptions** either disabled or delivered per-recipient with per-recipient execution.
- **Scheduled/background refresh** disabled or run per-user — a scheduled refresh runs under a service
  identity, and caching that for humans is the classic RLS bypass.

## Non-interactive contexts

CLI, TUI, and orchestrator scheduled jobs have no interactive user. For them `@@CURRENT_USER` resolves
to the configured service/run-as identity (or a `SYSTEM` sentinel), and `HAS_GROUP` returns false for
everything unless explicitly granted. Combined with the snapshot rule, an RLS report cannot be
scheduled into a shared cached artifact.

## Fail-closed

If no identity was injected, `@@CURRENT_USER` is null and `HAS_GROUP` returns false — so a well-formed
predicate (`WHERE HAS_GROUP(r.OwnerGroup)`) returns **no rows**, not all rows. Stronger, and the
recommended default: identity-sensitive reports **refuse to execute** without an injected identity,
mirroring existing PRIVATE-dataset fail-closed behavior, with a per-report opt-out.

## Author pattern (example)

```sql
-- Regional sales, row-filtered to the viewer's region groups.
-- Admins bypass by default (Portal:Security:AdminBypassRowLevelSecurity).
SELECT r.*
FROM   sales r
WHERE  HAS_GROUP('Region:' + r.RegionCode);   -- membership, not substring
```

## Threat model

| Bypass attempt | Stopped by |
| :--- | :--- |
| `SET` / `DECLARE @@CURRENT_USER` | Explicit `@@`-target rejection + reads route to provider |
| `?CURRENT_USER=` report parameter | Identity and parameters are separate namespaces; host-only injection |
| Script calls impersonation to elevate | Honored only if executing principal holds impersonation authority; else rejected |
| Read another user's cached snapshot | Auto-flagged identity-sensitive → no shared snapshot |
| Schedule refresh under service identity, serve to users | Scheduled/shared refresh disabled for identity-sensitive reports |
| Author strips the predicate | Governed by report edit/publish permission + existing `PublishedScriptHash` check |

## Phasing

- **Phase 1 (first slice):** `@@CURRENT_USER`, `@@CURRENT_USER_ID`, `@@REAL_USER`, `@@IS_ADMIN`,
  `HAS_GROUP`, `HAS_ROLE`; host injection from the Portal principal; explicit `@@` immutability
  enforcement; admin bypass (default on); auto-flag identity-sensitive reports + no-shared-snapshot;
  fail-closed when identity absent; **admin real impersonation** with dual-identity audit.
- **Phase 2:** OIDC group-claim mapping into the group set; table-valued `USER_GROUPS()` /
  `USER_ROLES()`; **Publisher preview-as**; per-recipient subscription execution; CLI/orchestrator
  run-as identity semantics.
- **Phase 3:** RLS-decision audit detail; admin-bypass and preview-as policy toggles surfaced in
  admin UI; documentation and template reports.

## Open questions

1. **Publisher preview-as data access — RESOLVED (2026-07-02).** Not an escalation, and no separate
   grant is needed. A report author already has full access to the data their query reaches (they
   wrote the SQL); RLS is a filter they *apply to viewers*, not an access control against themselves.
   Preview-as therefore works exactly like admin run-as: the **effective** (simulated/target) identity
   drives only the author-written RLS predicates, while the **real actor's** own authority still gates
   dataset/connection access (`DatasetCallerContext` = real actor), so preview-as cannot reach data the
   previewer couldn't already reach. True isolation of data from its own author is a database-layer
   responsibility (per-author credentials / DB-native RLS), explicitly out of ETL-SQL's scope.
   Implementation: extend `execute-as` to Publishers for reports they can edit; unconditionally
   never-cached; dual-identity audit.
2. **Group name namespace** — are OIDC groups domain-qualified, and do we normalize a canonical form
   before matching? (Matching is case-insensitive regardless.)
3. **Admin bypass granularity** — global toggle (as designed) vs per-report opt-in to still-filter
   admins for specific sensitive reports.
