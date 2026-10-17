# Studio Sandbox Submitter Identity

**Decision:** ST21b, 2026-10-13. Implemented 2026-10-13; accepted on a real Standard sandbox
2026-10-17 ([evidence](#acceptance-evidence--2026-10-17)).

The authenticated Orchestrator constructs the execution identity after verifying the federated
caller and the run-slot Execute grant. Submitted SQL, document metadata, variables, and report
parameters cannot supply identity claims or select a sandbox policy.

## Handoff

1. Carry an optional `ExecutionIdentity` in the provider-neutral sandbox request. Snapshot its group,
   role, and scope collections. Require its tenant to equal the job's server-verified tenant.
2. A submitted revision supplies the immutable artifact's text, not the run slot's stored placeholder.
   Policy, limits, tenant, admission, and physical runtime continue to come from operator configuration.
3. Docker stages a versioned identity envelope in a private directory under the single assignment
   root. It binds the identity to tenant, run, attempt, and script hash. Mount that directory read-only
   at `/run/etl-sql/identity`, outside the script's approved safe zones. Do not place claims in argv,
   environment values, capability handles, logs, or a checkpoint.
4. Worker environment values carry only the expected assignment bindings and a required-envelope flag.
   The CLI's trusted bootstrap reads the fixed `/run/etl-sql/identity/request.json` before evaluating
   any SQL, validates version, size, nonempty principals, tenant/run/attempt/hash bindings, and injects
   the resulting identity directly into the evaluator. Missing or malformed required input fails closed.
5. The envelope is assignment-local and is removed by the existing coordinator's verified teardown.
   Cancellation, timeout, or an unproven detach retains the existing cleanup/refusal semantics.

Local operator jobs without a submitted identity retain their current behavior. Executors must
explicitly advertise identity support; an older or alternative sandbox executor still refuses a
revision. There is no fallback to in-process execution when sandbox preparation or execution fails.
No identity is restored from checkpoint state or inferred from a job's owner.

## Boundary and limits

The read-only envelope is a trusted host input. It is not a signed remote caller credential: the
authenticated control-plane boundary has already verified the caller, and only that host stages the
assignment mount. Worker-image provenance, server-owned mounts, immutable artifacts, resource limits,
and runtime isolation remain mandatory. Standard/runc evidence applies to trusted local workloads;
it cannot certify the Hardened boundary required for hostile shared tenants.

The learner exercise stays within MOCKDB and temporary tables. Network remains `none`, and no
production connection capabilities or data mounts are granted. Script access to the identity mount
must be rejected by the path boundary. Assertions can read identity system variables; ordinary SET,
parameters, and saved state cannot overwrite them.

## Acceptance

- Unit/provider tests: immutable claim snapshot; matching and foreign tenants; required envelope
  failures; version/hash/run/attempt mismatch; read-only mount outside safe zones; no claims in runtime
  arguments; unchanged policy and isolation-tier refusals; unsupported executors remain refused.
- Scheduler tests: submitted text and identity reach the sandbox; tenant/policy/job binding remains
  operator-owned; cancellation and terminal run evidence use the same durable history ID.
- Real Docker and Portal browser evidence: scoped Publisher, saved-revision hash, failed ASSERT before
  load, fresh-session isolation, repaired exact rows, same-session explicit cleanup, repeatability,
  denied external egress, terminal status, and verified container/workspace removal.
- Keep the observed learner walkthrough separate. Passing automated security or browser tests does
  not record a human's understanding.

## Acceptance evidence — 2026-10-17

Run on Docker Desktop 29.6.2 (Linux engine, runc, Standard tier) with the worker image rebuilt from
`72b614c21`: local image ID `sha256:c565a9915963cbeb1a9a5ebaffa9517f4a7c4c7df561b626e6bd3ad1605b147a`.

```powershell
pwsh -File scripts\Test-SandboxWorkerImage.ps1 -Tag etlsql-sandbox-worker-test:local
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter "FullyQualifiedName~StudioSandboxPracticeTests|FullyQualifiedName~StudioPortalPracticeTests"
```

`StudioSandboxPracticeTests` drives the real Portal browser journey against an Orchestrator whose only
executor is `DockerSandboxExecutionProvider`. It passed in three consecutive runs with no skips.

| Requirement | Observed |
| :--- | :--- |
| Scoped Publisher identity in the sandbox | Every learner attempt carried tenant `portal-host`, role Publisher, not Admin; the script's `ASSERT @@CURRENT_USER` passed inside the container |
| Saved-revision hash | Each run's `ScriptHashAtRunTime` equals the saved revision's hash |
| Failed ASSERT before load, repair, repeatability | First run `FAILURE` with the repair hint; clean-state run, then two repaired runs `SUCCESS` with distinct run IDs |
| Policy egress fence | `169.254.169.254` refused: "cloud instance metadata service) is denied" |
| Network egress | `1.1.1.1` passes the fence and fails at TCP; every container's `NetworkMode` is `none` |
| Terminal status | At least two `Failed` and three `Succeeded` provider outcomes recorded per run |
| Cleanup | Every assignment's workspace directory and `etlsql-<assignment>` container is gone; `docker ps -a` shows no `etlsql-` container afterwards |

Supporting checks: 46 envelope, provider, coordinator and scheduled-executor tests and 19 Portal
revision-run, execution-identity and pipeline-run tests pass. TRX files are under `artifacts/st21b/`.

Limits: this is Standard/runc on a trusted local host, not Hardened (gVisor) or hostile-tenant
certification. A local image ID is accepted only in Standard mode. Learner understanding is not
evidenced (ST16b).

## References

- [Practice execution decision](studio-practice-execution.md)
- [Deployment profiles](../deployment-profiles.md)
- [Tenant isolation](../saas-tenant-isolation.md)
- [Execution identity](../../../src/ETL-SQL.Core/Governance/ExecutionIdentity.cs)
