# Secrets and Keys

ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate
passwords, and connection strings. Encrypted values use the `ENC:` prefix.

## By deployment profile

Which of the secrets on this page you actually have depends on what you are running. A Solo
workstation has no JWT secret because it has no Portal; a departmental deployment has several of
everything, and sharing any one of them breaks the isolation it exists to provide.

| Profile | What you do |
| :--- | :--- |
| **Solo / Workstation** | Encrypt values with `ETL-SQL encrypt`, or store them in the machine-protected store and reference them as `SECRET:name`. Nothing else on this page applies — there is no Portal and no Orchestrator service. |
| **Team / SME** | Adds the two shared keys: a **Portal JWT secret** and an **Orchestrator API key** that must match on both halves. Prefer the Portal secret store (`Governance:Secrets:Provider=PortalStore`) over `ENC:` values in files, so credentials are references rather than values. |
| **Enterprise / Corporate** | As Team, plus rotation with overlap (below), provider-backed secrets, ACLs and audit. **In HA, every node must carry the identical JWT secret, dataset at-rest key, Orchestrator API key and Data Protection key ring** — a node with its own key ring serves traffic and then fails unpredictably per request. |
| **SaaS / Departmental** | As Enterprise, **applied separately to every environment**. Each department gets its own JWT secret, its own key ring, its own Orchestrator key and its own security-event outbox path. `GET /api/admin/environments/plan` derives the full list of required keys per environment so this is checkable rather than remembered. Sharing one is enough to break isolation. |

## Encrypting a value

Encrypt with an explicit master password:

```bash
ETL-SQL encrypt "my-secret-password" --pass "YourMasterKey"
```

The CLI also supports machine-bound encryption when no password is supplied. That is convenient for
local services, but the encrypted value will not be portable if the machine key changes or the
configuration moves to another host.

> [!TIP]
> On Team and larger profiles, prefer `SECRET:name` references resolved from the Portal secret store
> over `ENC:` values embedded in configuration files. A reference can be rotated centrally; an
> encrypted literal has to be found and replaced everywhere it was copied.

## Portal JWT secret

*Team, Enterprise and SaaS only — a Solo workstation has no Portal.*

The Portal requires a strong JWT secret. Generate one during deployment:

```bash
ETL-SQL config setup-jwt --update
```

> [!CAUTION]
> Record the plaintext secret in a password manager or deployment vault. If it is stored only as an
> encrypted value and the machine key is lost, the plaintext cannot be recovered.

For a non-disruptive rotation, place the replacement in `Portal__Jwt__Secret` and retain the old
value temporarily as `Portal__Jwt__PreviousSecrets__0`. The portal signs only with the current secret
and validates against both. Remove the previous value after the maximum access-token lifetime has
elapsed. Removing it sooner intentionally invalidates access tokens signed by that key.

## Orchestrator API key

*Team, Enterprise and SaaS only.*

A shared API key protects every Orchestrator route that submits, cancels, inspects, schedules, or
manages jobs — including the ad-hoc execution routes `POST /jobs`, `DELETE /jobs/{id}`, and
`GET /jobs/{id}`. Only the unauthenticated probes `GET /health`, `GET /metrics`, and
`GET /metrics/prometheus` are exempt. The portal sends the key in the `X-Orchestrator-Key` header.

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "C:\\ETL-SQL\\scripts"
  }
}
```

The installers (MSI custom action and Linux `postinst`) generate a random `Orchestrator:ApiKey` on
first install and mirror it to `Portal:Orchestrator:ApiKey` so the two halves match out of the box.

Rotate without downtime by first adding the new key to `Orchestrator__PreviousApiKeys__0`, restarting
the Orchestrator, switching `Portal__Orchestrator__ApiKey` to the new key, then making the new key
current on the Orchestrator while retaining the old key temporarily in `PreviousApiKeys`. Remove the
old key after every caller has moved. The service compares fixed-length key digests in constant time.

> [!IMPORTANT]
> **The Orchestrator refuses to start unauthenticated on a network-reachable address.** If
> `Orchestrator:ApiKey` is empty *and* the service binds to a non-loopback address (for example
> `http://*:5001` or `http://0.0.0.0:5001`), startup fails fast with an actionable error. Configure a
> key, or bind the service to loopback only (`http://127.0.0.1:5001`). An empty key is permitted
> **only** for loopback-only bindings, which is development/isolated-host behavior.

## Related

- [Portal configuration reference](../portal/portal-config-reference.md) — every setting named here
- [Departmental isolation](../../architecture/decisions/departmental-isolation.md) — what must never be shared between environments
- [State and high availability](state-and-ha.md) — which keys must be identical across HA nodes
