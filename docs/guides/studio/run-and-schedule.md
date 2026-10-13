# Run and Schedule a Studio Pipeline

[« Back to Studio Guides](README.md)

**Applies to v0.21.0.** A pipeline is an `.etlsql` script. Desktop runs the current buffer locally.
Portal **Run** on a saved catalog pipeline submits its saved revision to the connected Orchestrator.
Separate attempts start fresh execution sessions; a later Run does not reuse earlier `#temp` tables.

## Choose the execution host

Open Studio using the [Portal or desktop entry point](first-report.md#open-studio). Create an ETL
pipeline or open a `.etlsql` file. On desktop, use a disposable workspace for this exercise.
In Portal, name the pipeline and choose a folder where you may create documents. If you first kept
it as a private draft, [publish it](drafts-and-recovery.md#publish-a-private-draft) before trying to
run or schedule it through the Orchestrator.

For Portal, an operator must connect the Orchestrator, provision the job corresponding to this
pipeline, and grant your identity access. The job is named `PIPELINE:<catalog document ID>`.

| Action | Required access |
| :--- | :--- |
| Open in Studio | Studio access, script-read capability, and an authoring grant on the catalog document. |
| Create/publish | Script-save and report-publish capabilities, plus Manage on the destination folder. |
| Save an existing pipeline | Script-save capability, Author or higher on the document, and an edit lease. |
| Run a saved pipeline | Script-run capability, Execute or higher on the document, and EXECUTE on its Orchestrator job. |
| Change/remove its schedule | Script-run capability and document Execute, plus MANAGE on its Orchestrator job and any existing schedule being changed. |
| Stop a run | The run's submitter with the applicable job access, or an operator with MANAGE on its job. |

Permissions are evaluated within your tenant. A Portal role or a visible Run button does not grant
access to an Orchestrator job. Ask the operator to resolve a refusal; do not switch to an administrator
identity to bypass the exercise's grants. See [Orchestrator integration](../../administration/portal/orchestrator-integration.md).

## Run, fail and repair

Replace the exercise document with this bounded script. It uses only MOCKDB and temporary tables;
the first run deliberately fails before the load.

```sql
CREATE CONNECTION sample_data AS MOCKDB();
SELECT UserID INTO #practice_stage
FROM sample_data.Users WHERE UserID BETWEEN 1 AND 3;

SELECT UserID, UserID * 10 AS Score INTO #practice_transform
FROM #practice_stage WHERE UserID < 3;
ASSERT (SELECT COUNT(*) FROM #practice_transform) = 3,
  'Expected three users: repair the transformation filter.';

SELECT UserID, Score INTO #practice_load FROM #practice_transform;
ASSERT (SELECT COUNT(*) FROM #practice_load) = 3,
  'Load must contain exactly three rows.';
ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 1 AND Score = 10) = 1;
ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 2 AND Score = 20) = 1;
ASSERT (SELECT COUNT(*) FROM #practice_load WHERE UserID = 3 AND Score = 30) = 1;

ASSERT (SELECT COUNT(*) FROM eng.columns
  WHERE table_name IN ('#practice_stage', '#practice_transform', '#practice_load')) > 0;
DROP TABLE #practice_load;
DROP TABLE #practice_transform;
DROP TABLE #practice_stage;
ASSERT (SELECT COUNT(*) FROM eng.columns
  WHERE table_name IN ('#practice_stage', '#practice_transform', '#practice_load')) = 0,
  'Cleanup must drop every practice table.';
SELECT COUNT(*) AS RemainingColumns FROM eng.columns
WHERE table_name IN ('#practice_stage', '#practice_transform', '#practice_load');
```

1. Save. In Portal, wait for Save to succeed before choosing **Run**. Unsaved edits are refused for
   a saved pipeline; nothing runs. On desktop, **Run** uses the current text, even before Save.
2. Find the failure message: **Expected three users: repair the transformation filter.** Only two
   rows passed `UserID < 3`. The ASSERT stopped the script before loading.
3. Change that predicate to `UserID <= 3`. Keep the ASSERT: it is your correctness check.
4. Save and run the complete script again. Expect success. The assertions check exactly one each
   of `(UserID, Score) = (1, 10), (2, 20), (3, 30)` before cleanup.
5. On desktop, the final result `RemainingColumns` must be `0`. Portal pipeline runs show status
   and statement logs, not the query result grid; inspect successful validation and cleanup statements.
6. Repeat the repaired run. It should pass again. Save and reopen to confirm the repaired predicate
   remains in the document.

After a failed ASSERT, later DROP statements did not run. Session disposal releases that attempt's
temporary state. This exercise never loads a persistent destination; use a reviewed transaction or
dry-run pattern before adapting it to destructive production work.

## Follow and stop a Portal run

Studio follows the accepted **run ID**, saved revision/hash, status, and statement log. Keep that ID
when asking an operator for help. A failure should identify the failed statement and its diagnostic.
If the log is truncated, it is not a complete execution transcript.

**Stop** requests cancellation of that particular run. Wait for a terminal status. A lost or refused
Stop request leaves cancellation unconfirmed; Studio continues following the run. Closing a tab,
losing a response, or seeing **Unknown** does not prove the server stopped. Check the durable run
history before submitting again. On desktop, an aborted request can likewise leave the client unable
to confirm the outcome; retain its support run ID.

## Schedule the saved Portal revision

1. Save the intended version. Open **Governance** and find **Run it on a schedule**.
2. Set **Cadence (cron)** and **Time zone**. For example, `0 2 * * *` with `UTC` means daily at
   02:00 UTC. An empty time zone uses the server default; use an explicit zone for predictable intent.
3. Choose **Schedule saved version …**. Verify the returned cadence, zone, and next-run information.
4. Saving later edits does not update the pinned scheduled script. If Studio says the scheduled
   revision differs from the saved version, review and schedule the newer version explicitly.
5. Use **Remove schedule** to remove the recurrence. This does not itself stop an already active run.

Desktop's schedule helper writes scheduling statements into the script; it does not install a
background scheduler in the local Studio host. Review and deploy those statements to an approved
Orchestrator using the [job and schedule references](../../reference/statements/README.md).

## When execution is unavailable

- **No Orchestrator connected:** Portal explains that it cannot run or schedule the pipeline.
  Authoring, validation, and permitted previews remain available. An administrator connects the
  service under **Admin → Orchestrator**. Portal does not execute the pipeline as a fallback.
- **Missing job or grant:** ask the operator to provision/grant the correct pipeline job.
- **Unavailable service, worker or isolation tier:** keep the saved script and refusal/run ID.
  Execution needs the deployment's admitted worker and security policy; MOCKDB does not waive them.
- **Stale saved revision:** review changes and save/reload before trying again.

The practice flow has automated desktop and Portal coverage, including a Standard sandbox fixture
for trusted workloads. It does not certify a hostile-tenant isolation boundary or human understanding.

References: [MOCKDB](../../reference/connectors/services/mockdb.md),
[SELECT](../../reference/statements/dml/select.md), [ASSERT](../../reference/statements/session-control/assert.md),
[DROP](../../reference/statements/ddl/drop.md), [deployment profiles](../../architecture/deployment-profiles.md).
