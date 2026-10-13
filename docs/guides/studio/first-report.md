# Your First Studio Report

[« Back to Studio Guides](README.md)

**Applies to v0.21.0.** Build a dashboard from sample data, inspect the SQL behind a visual, then
prove that your own edits survive a helper round trip and a save. You need no external database
for the sample-data exercise.

## Open Studio

| Host | Entry point | Where Save keeps the document |
| :--- | :--- | :--- |
| Portal | Sign in and choose **Studio**, or open `/studio.html` on your Portal. | A catalog document or your private draft, according to its current state and your permissions. |
| Desktop | Open a project directory or `.rptsql` file with the command below. | A file inside the opened workspace. |

From the repository root, open an existing project directory:

```powershell
dotnet run --project src\ETL-SQL.App -- studio "C:\Projects\StudioPractice"
```

The directory must exist. With the installed CLI, use `etl-sql studio "C:\Projects\StudioPractice"`.
The local host opens in your browser and normally reconnects to an existing host for that workspace.
It is local execution under your OS account. Keep its authenticated URL private.

Portal needs the Designer module and Studio access enabled. Reading scripts, saving, and publishing
are separate capabilities. For a catalog exercise, ask for a writable folder; a reader's right to
view a report does not grant authoring. If you lack publish rights, the
[private-draft path](drafts-and-recovery.md) can keep your unpublished work when enabled.

## Build and inspect

For a quick preview, **Start with sample data** opens a working revenue dashboard. The tested
connect-to-chart exercise below uses a Users starter instead:

- **Portal:** ask the operator to provide a shared MOCKDB connection and a starter catalog report
  reading its Users table, in your writable folder. Open that report from Studio Home. The ordinary
  author does not need connection-administration rights. Its declaration refers to the operator's
  shared alias, not a credential you type into the editor.
- **Desktop:** save the following as `first-report.rptsql` in your workspace and open it in Studio.

```sql
CREATE CONNECTION sample_data AS MOCKDB();
SELECT UserID, UserName INTO #users FROM sample_data.Users;
CREATE VISUAL UsersTable AS TABLE (
  SOURCE = #users,
  MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
);
CREATE PAGE Main AS DASHBOARD (
  LAYOUT (STRUCTURE = 'A', MAP ('A' = UsersTable))
);
```

1. Open the starter report. Keep its extension `.rptsql`.
2. Open the data catalog, choose the prepared MOCKDB connection, and select **Users**. Wait for its
   sample rows before adding a visual. The sample is an inspection aid, not a completed pipeline load.
3. Open the visual palette and add a **BAR** chart to the canvas. Select the chart, then map
   **UserName** to its category/X role and **UserID** to its value/Y role. Both roles are required.
4. Switch to **Split** so the canvas and script are visible together. Find the chart's
   `CREATE VISUAL` statement. `SOURCE` names its data; `MAPPINGS` connects columns to chart roles.
5. Select **UsersTable** on the canvas, return to the data catalog, and choose **UserName** to add
   a filter. Apply it and select a user value. Inspect the changed script and selected values.
   A source filter changes which rows reach the visual; it is not a database write.
6. Open the visual helper, choose a visual type, and use **Build** to inspect its SQL preview and
   explanation. Cancel this attempt. Opening or cancelling the helper should not change your script.

The [BAR reference](../../reference/visuals-reporting/visuals/bar.md) owns mapping syntax.
The [Report-SQL guide](../feature-guides/report-sql.md) explains pages, sources, and visuals in detail.

## Keep a hand edit through the helper

At the end of the script, type:

```sql
-- My first Studio round trip
SELECT 42 AS LearningCheck;
```

Open the helper again, inspect its preview, and cancel. Your comment and query should remain exactly
as typed. Helpers make focused edits; they are not permission to regenerate unrelated hand-written SQL.

Select only `SELECT 42 AS LearningCheck;` and choose **Run selected**. The result must contain one
column, `LearningCheck`, and one row with `42`. An empty selection does not run the whole document.
This checks the query you selected; it does not prove that the entire report rendered.

## Save, reopen and view

1. Choose **Save** or press **Ctrl+S**. Wait for confirmation and for the dirty indicator to clear.
   A kept recovery draft is not the same as a successful Save.
2. Close and reopen the document from Studio Home. On desktop, reopen its workspace file. Verify
   that your comment, query, chart mappings, and filter are still present.
3. In Portal, open the catalog report in the report library and **Execute** or **Refresh** it.
   This is the reader-facing report, with the reader's permissions. A private draft must be published
   before it appears there. On desktop, run the report from Studio and inspect its preview/results.

If reopening a helper drops hand edits, saving fails, or a report cannot run, keep the document and
use [Drafts, publishing and recovery](drafts-and-recovery.md). Do not replace the script just to
make the canvas look right.

## Exercise scope

The automated exercise covers connect → table sample → visual → filter → Split → helper/code round
trip → selected query → save/reopen in both production hosts, plus Portal reader execution. It does
not establish a new learner's understanding or certify every visual or paginated-report workflow.

References: [SELECT](../../reference/statements/dml/select.md),
[MOCKDB](../../reference/connectors/services/mockdb.md),
[continuous report journey](../../../tests/ETL-SQL.Portal.BrowserTests/StudioContinuousJourneyTests.cs).
