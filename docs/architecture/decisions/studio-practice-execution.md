# Studio Restricted Practice Execution

**Decision:** ST20, 2026-10-11. Defines acceptance for ST21/ST22; does not claim either journey is implemented.

> **Amended 2026-10-12 (owner decision).** ST21 is accepted on the Orchestrator's in-process
> executor. A sandboxed Orchestrator refuses a submitted revision (`sandbox_identity_unsupported`)
> until the sandbox request carries the submitter, so the "real sandbox" evidence below cannot be
> produced yet. Wherever this document requires the sandbox for the Portal exercise, read it as the
> target, not as what ST21 proved. The sandbox run is tracked as ST21b in `TODO.md`.
> Evidence: [Portal practice evidence](studio-portal-practice-evidence.md).
>
> **Amended 2026-10-17.** ST21b produced the real-sandbox evidence: the sandbox request now carries
> the submitter, and the Portal exercise passed on Standard/runc. See
> [sandbox submitter identity](studio-sandbox-submitter-identity.md#acceptance-evidence--2026-10-17).

## Execution and identity

The exercise uses a script-declared, session-local MOCKDB connection and engine temporary tables.
It needs no external database, credentials, shared connection, file data source or network access.
Each attempt runs the complete script in a fresh session. Repair means changing the SQL and running
again from the beginning; separate Run actions do not share staged tables.

| Host | Execution path | Learner boundary |
| --- | --- | --- |
| Desktop | Existing authenticated WorkstationEditor Run endpoint and a fresh ExecutionSession in a disposable project. | Self-installed local user, with the editor token and existing path/mutation guards. This is trusted local execution, not a sandbox for hostile SQL. |
| Portal | Save a catalog Pipeline revision, then submit that exact revision through the Orchestrator sandbox under the author's federated identity. | Ordinary scoped Publisher with the existing document/folder grants and the Orchestrator job run grant. Admin does not satisfy acceptance. |

Portal follows the accepted [pipeline decision](portal-etl-documents.md): Read/ScriptRead to open,
Manage/ReportPublish to create, Author/ScriptSave plus lease to save, and document Execute plus the
Orchestrator run grant to execute. The exercise folder/job is provisioned by an operator. Learners
receive no deployment administration, shared secrets or unrelated job grants. A private unpublished
draft is not executed: the [ST12 decision](studio-private-drafts.md) allows it preview only.

ST21 therefore depends on ST04's saved-revision submission. Portal's interactive designer endpoint
continues to accept only a read-only query. A missing Orchestrator, denied grant, unavailable sandbox,
or insufficient isolation tier refuses execution explicitly; there is no in-process fallback.
Use the deployment's approved isolation policy: Standard only for trusted workloads where permitted,
Hardened or Dedicated for mutually untrusted shared SaaS. MOCKDB does not lower that requirement.
Sandbox setup must deny external egress and omit production credentials and data mounts. SQL text
and a connector name alone do not enforce those boundaries.

## Bounded exercise

Use the same deterministic exercise in both hosts:

1. Read a small, fixed set of stable MOCKDB values into a temporary staging table. Avoid time-relative
   seed values. Define exact expected columns, values and ordering in the fixture.
2. Transform into another temporary table. Deliberately make its validation fail before loading.
   Show a failed ASSERT and its actionable diagnostic; a failure must not be reported as success.
3. Repair the transformation or predicate through Studio. Keep the intended validation meaningful;
   removing the ASSERT does not satisfy the exercise.
4. Rerun the whole script, validate, then load a session-local temporary destination. Compare every
   expected row/value and the total count. A success flag or nonempty result alone is insufficient.
5. Explicitly drop the exercise's temporary tables on the successful path, then end the session.
   Repeat the repaired run and obtain the same output, without accumulated or duplicate rows.

Staging, insertion into temporary tables, and dropping those temporary tables are the permitted
mutating steps. Persistent connector writes, DELETE/MERGE/TRUNCATE, native execution, file operations,
script inclusion and scheduled jobs are outside this bounded exercise. Persistent-load correctness
remains E11's separate acceptance case; this exercise does not certify it.

For desktop acceptance, restrictions describe the reviewed exercise and disposable workspace, not a
new server-enforced SQL allowlist. Do not add a UI-only claim that arbitrary edited scripts are safe.
Existing destructive confirmation remains intact. Portal sandbox restrictions must apply to the
submitted revision even when the learner edits it; validation of the original template is insufficient.

## Failure, cancellation and cleanup

A failed ASSERT stops before load. Subsequent SQL cleanup is not assumed to have run: disposing the
execution session releases its MOCKDB and temporary state. Test a fresh attempt after failure and
prove that prior temporary tables are unavailable. On success, verify explicit drops in the same
session (for example, through the engine table inventory) before disposal; starting a new session
alone cannot prove that the drops executed.

Cancellation and timeout must end execution and dispose resources. A browser abort alone is not
proof of server cancellation. Portal acceptance records the terminal Orchestrator status and sandbox
cleanup; failure to confirm cleanup stays visible. Retrying requires a fresh attempt, never reuse of
a possibly live sandbox. Preserve the saved script and diagnostic/run history according to existing
host retention policy; cleanup removes execution state, not the learner's document.

Keep existing host limits. Desktop currently applies a 60-second timeout, 128 MB operator grant,
256 MB session ceiling and at most 1,000 returned rows. These are configured execution controls,
not hostile-code isolation. Portal uses approved job/sandbox admission and resource limits; the
exercise must fit them without asking the learner to increase policy limits.

## Required evidence

- ST22: real desktop browser failure, repair, exact results, successful cleanup, fresh-session
  isolation after failure, and repeatability. Use no external editor or service.
- ST21: the same lifecycle through a real sandbox, as the scoped Publisher, bound to a saved
  revision. Record tenant/principal, document revision, run identity, terminal status and cleanup.
- Portal refusal coverage: absent document Execute or job run grant, cross-tenant access,
  unavailable/insufficient sandbox, and edits that request forbidden external access. None may
  fall back to Portal execution. Unsaved edits must be saved before submission or explicitly refused.
- Cancellation/timeout evidence must distinguish requested cancellation from confirmed termination.
- Learner explanation of the validation failure and repair feeds ST03 separately from automation.

## Source basis

- [MOCKDB reference](../../reference/connectors/services/mockdb.md): session-scoped sample data.
- [WorkstationRunService](../../../src/ETL-SQL.WorkstationEditor/WorkstationRunService.cs): fresh
  sessions, selection behavior and execution limits.
- [WorkstationRunGuard](../../../src/ETL-SQL.WorkstationEditor/WorkstationRunGuard.cs): temporary-table
  exemption and persistent destructive-operation confirmation.
- [PortalDesignerRunService](../../../src/ETL-SQL.Portal/Services/PortalDesignerRunService.cs): read-only
  interactive execution.
- [Deployment profiles](../deployment-profiles.md): sandbox tiers, admission and lifecycle policy.
- [Script/ETL inventory](studio-script-etl-authoring-inventory.md): outstanding acceptance gaps.
