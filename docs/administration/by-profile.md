# Administration by Deployment Profile

The administration docs are organised by **task**, because a fact should live in exactly one place.
This page is the other axis: it gives each deployment profile an ordered path through those tasks,
so you can read only what applies to you.

Profiles are **cumulative operating profiles, not editions** — the same `.etlsql` and `.rptsql`
artifacts run under all four. A larger profile adds operational and trust boundaries; it never
changes what a script means. See
[Deployment Profile Standards](../architecture/standards/Deployment_Profile_Standards.md) for the
capability matrix and evidence contract.

---

## Solo / Workstation

One trusted operator on one machine. The OS account is the security boundary. **There is no Portal
and no Orchestrator service**, so most of the Portal administration section does not apply to you.

1. [Installation](platform/installation.md) — install the CLI; skip the service sections.
2. [Configuration file locations](platform/config-file-locations.md) — where settings live.
3. [Secrets and keys](platform/secrets.md) — `ETL-SQL encrypt` and `SECRET:name`. Stop before the
   JWT and Orchestrator API key sections; you have neither.
4. [Backup and monitoring](platform/backup-and-monitoring.md) — schedule `etl-sql admin backup`
   with the OS scheduler, and test the restore.
5. [Operator CLI](platform/operator-cli.md) — `doctor`, `support-bundle`, `backup`/`restore`.

Quality and governance work fully without a Portal: see the
[one-person quality loop](../guides/patterns/one-person-quality-loop.md) and
[data quality](../guides/feature-guides/data-quality.md#running-unattended-without-portal).

## Team / SME

A shared service for one organization, normally a single node. This is the smallest profile where
a **second person** exists — which is where permissions, revocation and review start to mean
something.

1. Everything in Solo, plus:
2. [Portal deployment and first-run setup](portal/deployment.md).
3. [Secrets and keys](platform/secrets.md) — now including the **JWT secret** and the
   **Orchestrator API key**, which must match on both halves.
4. [User management](portal/users.md) and [permissions](portal/permissions.md) — groups and folder
   ACLs. Note that ACLs bind to **groups**, not roles, and that Admins bypass folder ACLs entirely.
5. [Publishing reports](portal/publishing.md) and
   [connections and subscriptions](portal/connections-and-subscriptions.md).
6. [Job scheduling](orchestration/job-scheduling.md) — durable schedules instead of the OS scheduler.
7. [Production readiness checklist](portal/production-readiness.md) before go-live.

> [!IMPORTANT]
> If you run Team on PostgreSQL, set `Portal:Topology:ExpectedMode` explicitly. The `Auto` default
> infers `HighAvailability` from PostgreSQL alone and never infers `Departmental`, so the node is
> held out of load-balancer rotation until you tell it what it is. See
> [state and high availability](platform/state-and-ha.md).

## Enterprise / Corporate

Multiple teams, formal identity, and an availability contract. Everything in Team, plus the
boundaries that make it auditable.

1. [Security model](portal/security.md) and [permissions](portal/permissions.md) — the two-axis
   model: a **role** decides the class of operation, an **ACL** decides which resources.
2. [Organization policy](platform/organization-policy.md) and [governance](platform/governance.md).
3. [Row-level security](platform/row-level-security.md).
4. [State and high availability](platform/state-and-ha.md) — shared PostgreSQL, shared artifact
   roots, and an **identical key ring, JWT secret, dataset key and Orchestrator key on every node**.
5. [Monitoring and audit](portal/monitoring-and-audit.md) — including the remote audit outbox.
6. [Enterprise enrollment](platform/enterprise-enrollment.md) and
   [native admin services](platform/native-admin-services.md).
7. [Operations](portal/operations.md) — the posture and diagnostic surfaces.

Recovery custody stays on the host at every size: the Portal *reports* backup freshness and
restore-drill evidence, and never holds custody or performs the restore.

## SaaS / Multi-Organization and departmental deployments

Several mutually untrusted organisations, or several departments that must not see each other's
data. **Everything in Enterprise, applied separately to every environment.**

This is the profile where the failure mode is not "I forgot a setting" but "I shared one":

1. [Departmental isolation](../architecture/decisions/Departmental_Isolation.md) — the contract for
   what must never be shared. Read it before provisioning the second environment, not after.
2. Generate a plan per environment with `GET /api/admin/environments/plan?environmentId=&portBase=`.
   It derives every isolated resource, port and key requirement from the environment id, so the
   list is **checkable rather than remembered**. Plans are secret-free and are never applied by the
   Portal — an environment able to provision another is not isolated from it.
3. Give every environment its own: Portal database, Orchestrator database, artifact root, Data
   Protection key ring, JWT secret, dataset at-rest key, Orchestrator API key, and
   **security-event outbox path**. That last one is the trap: its default is a machine-wide path
   under `LocalApplicationData`, so two environments on one host write security events into a
   single queue unless you set it. See [security events](platform/security-events.md).
4. Validate with `POST /api/admin/environments/validate`. **Any shared resource is a collision, not
   a warning** — sharing one is enough to break isolation.
5. [Deployment promotion](platform/deployment-promotion.md) and
   [profile transitions](platform/profile-transitions.md) when moving work between environments.

> [!WARNING]
> Support for a mutually untrusted tenant boundary is **not certified**. The Enterprise happy path
> is not evidence that it holds. See the
> [v0.18.0 deployment profile review](../architecture/decisions/v0.18.0-deployment-profile-review.md).

---

## Related

- [Deployment Profile Standards](../architecture/standards/Deployment_Profile_Standards.md) — capability matrix and evidence contract
- [Platform administration](platform/README.md) · [Portal administration](portal/README.md) · [Orchestration](orchestration/README.md)
