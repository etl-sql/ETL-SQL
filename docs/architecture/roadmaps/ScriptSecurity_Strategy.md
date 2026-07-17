# Script Security Strategy

> [!NOTE]
> **Security design rationale.** This document records the decision to use hash pinning instead of full PKI script signing, and why. The `SET SCRIPT_HASH_POLICY` statement and `USE PASSWORD` / `ENC:` encryption features implement this rationale. For current security behavior and configuration options, see `SECURITY.md` and `docs/guides/getting-started.md`.

**Status:** Implemented (Hash-pinning policies and session-password decryption features are in production)

## The Question

Should ETL-SQL scripts be cryptographically signed, and should the engine refuse to execute unsigned or tampered scripts?

## Conclusion First

**Full PKI script signing is not recommended.** The key management overhead is disproportionate to the threat it addresses for ETL-SQL's typical deployment. The real concern — detecting whether a script has been modified since it was last reviewed or scheduled — is better solved by **hash pinning**: storing a SHA-256 hash of the script at approval/schedule time and comparing it at run time. Hash pinning has zero key infrastructure, zero certificate management, and covers the same practical risk at a fraction of the cost.

---

## What Script Signing Would Actually Provide

Full cryptographic signing (RSA or Ed25519 signature over the script content, verified by the engine before execution) provides:

1. **Tamper detection with attribution** — you can prove not just that the script changed, but who signed the approved version.
2. **Approval workflow enforcement** — an unsigned or invalidly signed script cannot run, even if it exists on the filesystem.
3. **Audit integrity** — when combined with an audit log, you can prove that a specific version of a script ran at a specific time.

These are real properties. They matter in environments where:
- Scripts are deployed by an external pipeline (e.g. a CI/CD system controlled by a security team) and the signing key is held only by that pipeline
- The data pipeline runs in a regulated environment (SOX, HIPAA) where change-control evidence is required
- Multiple untrusted parties can write files to the script directory

## Why It's Probably Not Worth It for Most Deployments

### The threat model is weak

ETL-SQL scripts are written by trusted internal developers — data engineers, data scientists, analysts. These are not external or adversarial actors. The script directory is not a public upload endpoint.

If an attacker has write access to the script directory, they almost certainly also have:
- Write access to `appsettings.json` (connection strings, credentials)
- Write access to connector credential files
- Access to the same network segments as the data sources the scripts query

Script signing does not protect against a compromised host. It only protects against a very specific threat: an attacker who can write to the script directory but cannot modify the signing infrastructure. That threat is real but narrow.

### Signing proves identity, not safety

A signed script is an approved script, not a safe script. A developer can sign and deploy a script that drops a table, exfiltrates data, or runs for 10 hours. Signing shifts accountability but does not reduce harm. The actual security guardrails — path restrictions, connection permissions, operation count limits, execution timeouts — already exist in the engine and are not bypassed by signing.

### Key management is expensive

A signing system requires:
- A key pair (or certificate) per signer, or a shared CI/CD signing key
- Secure storage for private keys (HSM, Vault, or at minimum an encrypted secrets store)
- Key rotation policy and procedures
- Revocation mechanism for compromised keys
- Re-signing when keys expire
- CI/CD pipeline integration to sign on commit or deploy

For a team of 5 data engineers managing 50 scripts, this is a non-trivial operational burden that scales with team size and script volume. The benefit — detecting a specific class of file tampering — rarely justifies it.

### Developer friction

Every script change requires re-signing before it can run. In rapid development (running scripts locally, iterating on queries), this adds a mandatory step to every edit cycle. CI/CD automation reduces this friction but adds pipeline complexity and does not eliminate it for local development and testing.

---

## What the Real Risk Actually Is

In practice, the concern that motivates "script signing" is almost always one of these:

1. **Scheduled job runs a stale or accidentally modified version of a script** — someone edited the file after the job was configured, and the next run silently executes different code.

2. **Portal runs a script that has changed since it was published** — the portal already shows a "stale" indicator, but there is no hard block.

3. **Audit trail gap** — when investigating a data incident, you cannot prove what exact code ran.

None of these require cryptographic signing. They all require **knowing when the script changed relative to when it was last approved or scheduled**.

---

## Recommended Approach: Hash Pinning

### What it is

At the time a script is scheduled (Orchestrator) or published (Portal), compute `SHA-256(file content)` and store it alongside the job or report record. At execution time, recompute the hash and compare.

- **Match**: proceed normally.
- **Mismatch**: warn in the log and optionally block execution (configurable).

### What it gives you

- Detects accidental edits between schedule/publish and run time.
- Detects file replacement attacks (same filename, different content).
- Produces an exact hash in the execution log — an auditor can verify which version ran.
- Zero cryptographic key infrastructure.
- Zero signing step in the developer workflow.
- Trivial to implement: a single `File.ReadAllBytes` + `SHA256.HashData` call.

### What it does not give you

- Attribution (who approved the version). If that's required, use git blame — source control is the right tool for approval history, not script signing.
- Prevention of a malicious insider writing and scheduling a new script (they can update the hash too). Full PKI signing prevents this; hash pinning does not.

### Configuration

Two behaviors, both configurable per-deployment:

| Mode | Behavior on hash mismatch |
| :--- | :--- |
| `Warn` (default) | Log a warning; execution proceeds. The stale indicator appears in the portal. |
| `Block` | Refuse execution; operator must re-schedule or re-publish to acknowledge the change. |

```json
"Engine": {
  "ScriptHashPolicy": "Warn"
}
```

Per-job override via `SET SCRIPT_HASH_POLICY = 'Block'` at the top of a script (for high-risk jobs that should never run a modified version).

---

## Orchestrator Integration

When a script is scheduled via the Orchestrator, store the hash in the job record:

```
OrchestratorJob
  ScriptPath:    /Reports/Sales/Daily.etlsql
  ScriptHash:    sha256:a3f1c8d2...          ← new field
  HashPolicy:    Warn                        ← new field
```

At run time, `JobRunner` computes the current hash, logs both, and applies the policy. The execution history record already captures the execution context — add `ScriptHashAtRunTime` and `HashMatchedAtSchedule` (bool) to the history entry for audit purposes.

---

## Portal Integration

The portal already tracks a "stale" indicator (script modified since last snapshot). Extend this:

- At publish time, store `PublishedScriptHash` on the report record.
- At snapshot build time, compare and record in the snapshot log.
- Surface "script has changed since last run" prominently in the Admin → Reports view (currently only shows "stale" at the top level — the hash makes this precise).
- Add `ScriptHash` to the execution audit log entry.

No new UI needed beyond what already exists for the stale indicator.

---

## When Full PKI Signing Would Be Worth It

If ETL-SQL evolves into any of the following, revisit this decision:

1. **Multi-tenant hosted service** — users upload scripts to a shared runner. The signing key is held by the platform; user-submitted scripts must be reviewed and signed before they can run. This is a fundamentally different threat model.

2. **Regulated environment with hard change-control requirements** — if an auditor requires cryptographic proof of approval (not just a hash), PKI signing backed by an HSM and a formal key management procedure is the right answer.

3. **Air-gapped or offline deployment** — scripts are transferred via media from a secure signing station to a production runner with no network connectivity. Hash pinning requires the secure station to also control the scheduler; signing allows the runner to verify independently.

These are real scenarios. They are not the typical ETL-SQL deployment.

---

## Implementation Checklist

### Phase 1 — Hash pinning in Orchestrator

- [ ] `OrchestratorJob` entity: Add `ScriptHash` (nullable `TEXT`) and `HashPolicy` (`Warn`/`Block`, default `Warn`).
- [ ] New EF Core migration: `AddJobScriptHash`.
- [ ] `JobScheduler`: Compute and store hash when scheduling a job.
- [ ] `JobRunner`: Recompute hash at run time; compare; apply policy; log result.
- [ ] `ExecutionHistory` entity: Add `ScriptHashAtRunTime` (TEXT) and `HashMatched` (bool).
- [ ] `appsettings.json`: Add `Engine.ScriptHashPolicy` (global default).
- [ ] `SET SCRIPT_HASH_POLICY` statement: Parse and apply per-script override.
- [ ] Tests: hash match → runs; hash mismatch + Warn → runs with log entry; hash mismatch + Block → `ExecutionException`.

### Phase 2 — Hash pinning in Portal

- [ ] `Report` entity: Add `PublishedScriptHash` (TEXT).
- [ ] Portal publish flow: Compute and store hash.
- [ ] Snapshot builder: Compare hash before execution; log `ScriptHashAtRunTime` and `HashMatched` on the snapshot record.
- [ ] Admin → Reports view: Show "script changed since published" (distinct from "stale" which is modification-time based) when hash mismatches.
- [ ] Audit log: Include `ScriptHash` on `EXECUTE_REPORT` events.

### Phase 3 — Documentation

- [ ] `docs/guides/administration.md`: Add `Engine.ScriptHashPolicy` to configuration reference.
- [ ] `docs/guides/portal-admin.md`: Document hash tracking in the publishing and execution sections.
- [ ] `Docs/Architecture/Orchestrator.md`: Document hash fields on job and execution history entities.

---

## Effort Estimate

| Phase | Estimate |
| :--- | :--- |
| Phase 1 — Orchestrator hash pinning | ~1 day |
| Phase 2 — Portal hash pinning | ~0.5 day |
| Phase 3 — Documentation | ~0.5 day |
| **Total** | **~2 days** |

This is roughly one-fifth the effort of a full PKI signing implementation and provides the same protection against the most common real-world risk (accidental script drift between schedule and run time).

---

## Decision Log

| Question | Decision |
| :--- | :--- |
| Full PKI signing? | Not recommended. Disproportionate key management overhead for the threat model. |
| Hash pinning? | Yes. Covers the real risk (script drift detection) at low cost. |
| Block on mismatch by default? | No — `Warn` default. Blocking by default will surprise operators on first deployment and cause legitimate jobs to fail when scripts are intentionally updated. Opt-in `Block` for high-risk jobs. |
| Store hash in audit log? | Yes. Provides the audit trail benefit without the signing overhead. |
| Attribution (who approved)? | Out of scope — that's what git history and PR approvals are for. |
| Revisit full PKI signing? | Yes, if ETL-SQL becomes a multi-tenant hosted service or enters a regulated environment with hard change-control requirements. |
