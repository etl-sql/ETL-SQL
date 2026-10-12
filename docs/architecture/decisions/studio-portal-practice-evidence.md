# Studio Portal Practice Evidence

**Task:** ST21, the bounded Portal failure/repair lifecycle.
**Run:** 2026-10-12, Windows, production Portal host joined to a federated Orchestrator, Chromium.
**Result:** one passed, zero failed, zero skipped.
**Executor:** the Orchestrator's in-process executor, not its sandbox. See [the limit](#validation-and-limits).

[StudioPortalPracticeTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioPortalPracticeTests.cs)
runs the exercise from the [practice contract](studio-practice-execution.md) as a learner in real
Chromium. Every edit goes through Studio's editor with the keyboard, and every run is Studio's Run
button on a saved revision. No external service is used and no administrator signs in.

## Identity and run record

| What | Value in the run |
| --- | --- |
| Tenant | `portal-host` |
| Learner | A new account with exactly the `Publisher` role, owning one folder. No Orchestrator role. |
| Operator | Acts on the Orchestrator directly: provisions the job `PIPELINE:<id>` and grants the learner `EXECUTE`. |
| Revision | Each run's `ScriptHashAtRunTime` equals the hash of the text the Portal returns for the saved document. |
| Run identity | The run ID Studio follows equals the one new entry in the Orchestrator's job history. |
| Terminal status | `FAILURE`, `SUCCESS`, `SUCCESS`, `SUCCESS`, in that order, read from the Orchestrator's history. |

## Verified behavior

1. MOCKDB users 1 through 3 are staged. A deliberately wrong filter drops user 3. The ASSERT stops
   the run before the load. Studio reports `Failed`, shows the repair message, and names the ASSERT
   as the failed statement.
2. A separate saved revision asserts that the engine column catalog holds none of the exercise's
   temporary tables. It succeeds: the failed attempt left nothing for the next run.
3. Keyboard editing restores the filter and keeps the validation. The saved text is the repaired
   script, by hash.
4. The repaired run asserts exactly three loaded rows and exactly one each of `(1, 10)`, `(2, 20)`
   and `(3, 30)`. A Portal run returns a statement log, not rows, so the script proves its own
   output: a wrong row fails the run.
5. An assertion first confirms the temporary tables are visible. The script drops all three, then
   asserts the catalog count is zero in the same session.
6. Running the repaired revision again gives a second `SUCCESS` with the same hash. Reopened from
   Studio Home, the document is the repaired script.

## Defects this exercise found

Both were engine defects, fixed with their own tests.

- **A repeated scalar subquery answered from before a change.** The subquery cache lived for the
  whole script. `ASSERT (SELECT COUNT(*) FROM #t) = 3`, an `INSERT`, then the same subquery
  returned 3 again; after `DROP TABLE`, the catalog count stayed above zero. The cache is now
  emptied after any statement other than a plain query.
  `SubqueryCacheTests.StaticSubquery_IsReadAgain_AfterAStatementChangesItsTable`.
- **A failed run's log blamed the statement before the failure.** A statement that threw was never
  measured, and the log marks the last measured statement as failed. A failed cleanup assertion
  was shown as `Failed: DROP TABLE #practice_stage;` above the ASSERT's own message. The throwing
  statement is now measured. `MoreHandlerTests.Profiling_MeasuresTheStatementThatFailed_AsTheLastOne`.

## Validation and limits

```powershell
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter FullyQualifiedName~StudioPortalPracticeTests
```

- **Not a sandbox run.** An Orchestrator that executes jobs in its sandbox refuses a submitted
  revision with `sandbox_identity_unsupported`, because the sandbox request carries no caller. The
  owner accepted the in-process executor for ST21 on 2026-10-12. Sandbox cleanup, egress denial and
  isolation tier are not evidenced here.
- Refusals for a missing job, a missing grant, unsaved edits and a missing Orchestrator are covered
  by `StudioPipelineRunJourneyTests` and `StudioPipelineRunTests`, not repeated here.
- Cancellation and timeout cleanup are ST06's evidence.
- Learner understanding of the failure and repair is not established by automation.
