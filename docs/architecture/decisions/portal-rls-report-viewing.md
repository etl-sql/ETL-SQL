# Viewing Row-Level-Secured Reports in the Portal

**Status:** proposed 2026-10-12, awaiting owner review. Nothing here is built.
**Task:** ST19. **Parent:** [Row-level security](row-level-security.md).
**Finding it answers:** [reader journey evidence](studio-reader-journey-evidence.md#st19-two-identities-one-report).

## Problem

A report that reads the viewer's identity (`HAS_GROUP`, `@@CURRENT_USER` and the rest) keeps no
shared snapshot. That rule is right and stays. But the Portal viewer, export and first-run flow all
read only snapshots, so a reader who runs such a report is sent back to *Ready for first run* and
never sees it. The interactive session behind parameters, bookmarks and saved views is built with no
identity at all.

Nothing leaks today. Nothing displays either.

## Goal

Two ordinary readers open the same identity-filtered report in the Portal and each sees, filters,
saves a view of, and exports **their own rows only**. No new row-level-security model.

## Decision

**An identity-sensitive report is viewed live, from the caller's own session, run under the
caller's identity. Its rows are never written to storage.**

The Portal already keeps one live `DashboardService` per tenant, report and user for parameter
changes. That session becomes the single source for an identity-sensitive report: first display,
every interaction, and export.

### Rejected alternatives

| Alternative | Why not |
| --- | --- |
| A stored snapshot per user | Puts filtered rows at rest per person. Needs its own retention, and a stored result outlives a group change. |
| Serve a finished run's result to whoever ran it | Displays the report but leaves parameters, saved views and export broken, and still stores rows. |

## Design

### 1. One place builds the identity

`ExecutionJobService.BuildExecutionIdentityAsync` moves into a small Portal service that both the
job service and the session cache call. Same inputs, same output: user name and ID, roles, groups,
`IsAdmin`, and the `AdminBypassRowLevelSecurity` setting. If the user cannot be resolved it returns
nothing and the report fails closed, as now.

### 2. Sessions carry the identity

`SessionCache.GetOrCreate` receives the caller's identity and passes it to `DashboardService`.

A session is reused only when its script, its caller context **and its identity** match. Identity
is compared by user, admin flag, bypass setting, and the sorted group and role names. A reader
removed from a group gets a new session, and new rows, on their next request. The identity is
rebuilt from the database on each request (three small reads); it is never taken from the client.

This applies to every report. A report that does not read identity is unaffected by it.

### 3. The manifest route answers live for these reports

`GET /api/reports/{id}/manifest` is what the report frame loads. For an identity-sensitive report
it returns the caller's session manifest instead of looking for a snapshot.

- Requires **Execute** on the report, not just Read: answering runs the script.
- The report is identified as identity-sensitive by the existing scan of its script. If the script
  cannot be read, it is treated as identity-sensitive.
- Audited as `VIEW_REPORT_LIVE` with the report ID.
- `GET …/snapshot`, `…/snapshot/manifest` and `…/snapshot/rows/*` keep answering 404 for these
  reports. There is no snapshot.

The report runtime in the frame does not change.

### 4. The viewer opens these reports directly

`ReportDto` gains `perViewer` (true when the scan says identity-sensitive). When it is true the
viewer skips the snapshot probe and the first-run gate and shows the frame at once.

| Control | Identity-sensitive report |
| --- | --- |
| Header | A "Live · your rows" pill in place of Built / Stale. |
| Refresh | Discards the caller's session and reloads the frame. |
| Export | Offered. See §5. |
| Structure, Dependencies | Unchanged. |
| History | Unchanged; it lists no snapshots. |
| Subscribe | Not offered in this slice. |
| Design | Unchanged: only for authors. |

A reader with Read but not Execute sees the report in the library and is told on opening that
running it needs Execute. This is the one place a reader's access gets narrower than today, where
today they would see nothing at all.

### 5. Export comes from the same session

For an identity-sensitive report, CSV, Excel and PDF export render the caller's session manifest
with its current parameters, instead of the latest snapshot. Permission, audit entries and the PDF
rate limit are unchanged. The file is produced in the response and not kept.

### 6. A run's result is not left on disk

`POST /api/reports/{id}/execute` and `execute-as` stay for API callers. Today such a run writes its
manifest into snapshot storage and then, correctly, records no snapshot row. The file is left
behind with one person's rows in it and nothing prunes it. For an identity-sensitive or
impersonated run the manifest will no longer be written.

*To confirm while building:* that nothing reads a job's manifest path for these runs. A search
found no reader.

## What does not change

- No shared snapshot for identity-sensitive reports. Scheduled refresh still skips them.
- The admin bypass setting, impersonation, and their audit entries.
- Subscriptions, which already run per recipient.
- The sandbox, the remote Orchestrator path, and reports that do not read identity.

## Limits accepted

- **Cost.** Each reader of such a report costs one live execution and one session, bounded by the
  session cache size, its idle timeout and the execution timeout. These opens do not pass through
  the report job queue. Parameter changes already work this way for every report.
- **Freshness.** A reader's session is reused until it idles out, they press Refresh, the report is
  republished, or their identity changes.
- **Detection.** Sensitivity is decided by scanning the report's own script. Whether a reference
  reached only through an included script is caught is to be checked while building, and filed
  separately if it is not.

## Evidence required to close ST19

Browser, real Chromium, no administrator:

1. Two Viewers in different groups each see exactly their rows, and a row belonging to neither
   group is seen by neither.
2. The same holds after a parameter change, after reopening a saved view, and in the exported CSV.
3. The first reader, returning after the second has viewed, still sees only their own rows.
4. No snapshot row and no manifest file exists for the report afterwards.

API:

5. A reader with Read but not Execute is refused the live manifest and the export.
6. A reader removed from their group gets no rows on the next request, with no sign-out.
7. With no resolvable identity the report returns no rows.
8. An administrator sees all rows with the bypass on and only their own with it off.
9. A report that does not read identity behaves exactly as before: snapshot, first-run gate, export.

## Files expected to change

- `src/ETL-SQL.Portal/Services/`: new identity service; `SessionCache`; `ExecutionJobService`.
- `src/ETL-SQL.Portal/Controllers/`: `ExecutionController`, `ExportController`, `ReportsController`.
- `src/ETL-SQL.Portal/BrowserSources/`: `report-viewer.ts`, `report-viewer-view.ts`.
- Tests: `PortalReaderJourneyTests`, `RowLevelSecurityPortalTests`, the viewer script checks.
- Docs: the row-level-security administration page and guide, `CHANGELOG.md`, `TODO.md`.

No database migration. No new dependency.
