# Viewing Row-Level-Secured Reports in the Portal

**Status:** decided with the owner and built 2026-10-12. Evidence is listed [below](#as-built).
**Owner decisions:** rows are produced live per viewer and not stored; Read on the folder is enough
to open such a report; Subscribe is offered for these reports in this slice.
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

- Requires **Read** on the report, the same as viewing a snapshot (owner decision). Answering runs
  the script, so for these reports a Read-only reader causes an execution. That is the one place
  Read does so; `POST …/execute` and Refresh of an ordinary report still need Execute.
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
| Subscribe | Offered. See §7. |
| Design | Unchanged: only for authors. |

Refresh only discards the caller's own session, so it needs no more than Read here.

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

### 7. Subscribe is offered

Delivery already handles these reports: `SubscriptionDeliveryService` runs an identity-sensitive
report once per recipient under that recipient's identity, and records a failure for a recipient
who is not a Portal user. Creating a subscription does not need a snapshot. Only the viewer
withholds the button, because it ties Subscribe to a snapshot existing.

The live view offers Subscribe and opens the existing dialog. No delivery rule changes. What is
added is proof: a delivery to two recipients in different groups carries each one's own rows.

## What does not change

- No shared snapshot for identity-sensitive reports. Scheduled refresh still skips them.
- The admin bypass setting, impersonation, and their audit entries.
- How subscriptions are delivered: they already run per recipient.
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

5. A reader with Read but not Execute can open, filter and export the report; a reader with no
   grant on the folder is refused all three.
6. A reader removed from their group gets no rows on the next request, with no sign-out.
7. With no resolvable identity the report returns no rows.
8. An administrator sees all rows with the bypass on and only their own with it off.
9. A report that does not read identity behaves exactly as before: snapshot, first-run gate, export.
10. A reader subscribes from the live view. One delivery to two recipients in different groups
    produces, for each, an export holding only that recipient's rows.

## As built

| Evidence item | Test |
| --- | --- |
| 1, 2, 3, 4, and Subscribe from the live view | `PortalReaderJourneyTests.TwoReadersOfOneReportEachSeeOnlyTheirOwnRows` (real Chromium, two Viewers holding Read) |
| 1, 3, 4 at the API, and the `VIEW_REPORT_LIVE` audit entry | `RowLevelSecurityLiveViewTests.EachReaderGetsOnlyTheirRows_WithReadAlone_AndNothingIsStored` |
| 5 | `…AReaderWithNoGrantIsRefused`; every reader in that class holds Read only |
| 6 | `…AReaderRemovedFromTheirGroupGetsNoRowsOnTheNextRequest`, `SessionCacheTests.GetOrCreate_GroupRemoved_ReplacesTheSession` |
| 7 | `PortalExecutionIdentityServiceTests.AnUnknownUserHasNoIdentity`; the engine returns no rows without one |
| 8 | `…AnAdministratorFollowsTheBypassSetting` |
| 9 | `…AnOrdinaryReportStillHasNoManifestUntilItHasASnapshot`, and the unchanged viewer, export and library suites |
| 10 | `SubscriptionDeliverySecurityTests.RlsReport_DeliveredToTwoRecipients_RunsOncePerRecipientUnderEachIdentity` |
| §5 export | `…EachReadersCsvHoldsOnlyTheirRows` |
| §6 nothing on disk | `RowLevelSecurityPortalTests.IdentitySensitiveReport_PersistsNoSharedSnapshot_PlainReportDoes` |

Changed after an independent review of the first build (2026-10-12):

- **A rewritten script ends its sessions.** A session is also bound to its script file's last-write
  time and length. Without that, an author who tightened a filter and saved did not reach a reader
  who kept the report open, because no snapshot run exists to end these sessions.
  `RowLevelSecurityLiveViewTests.ARepublishedScriptAppliesToAReaderWhoAlreadyHasItOpen`.
- **A service account needs `reports.execute`.** "Read is enough" is for people. A service-account
  token with only `portal.read` could not run a report before, and is refused the live manifest,
  export and reset. `…AServiceAccountNeedsTheExecuteScopeToRunTheReport`. This narrows the owner's
  Read decision for service accounts only and is the owner's to confirm.
- **Refresh rebuilds the session in place.** `live/reset` runs the report again inside the caller's
  existing session, behind its lock, and answers 409 for a report that is not shown live.
  Discarding the session let repeated resets each start another run beside the last.
  `…ResetRunsTheReportAgainForTheCallerOnly`.
- **A script that is gone is not run live.** A missing file is left to the snapshot path, so an
  ordinary report whose script was removed still shows its snapshot. A file that exists and cannot
  be read still counts as identity-sensitive.
  `…AnOrdinaryReportIsStillServedFromItsSnapshot_EvenWithItsScriptGone`.
- **A result the remote Orchestrator wrote is always deleted** for these runs, whether or not it
  also returned one. That branch has no test.
- Evidence added: an ungranted reader is refused export and parameter changes as well
  (`…AReaderWithNoGrantIsRefused`), and a session with no identity returns no rows
  (`…ASessionWithNoIdentityReturnsNoRows`).

Differences from the design above:

- `perViewer` is set on the single-report read only. List and catalog responses do not carry it;
  they would have to read every script.
- Item 10 proves one run per recipient under that recipient's identity. It does not open the two
  exported files; that identity filters rows is the engine's, covered by `RowLevelSecurityIdentityTests`.
- Item 7 is proven in two halves, not as one request.

Still open:

- A live view writes no `VIEW_SNAPSHOT` entry and does not set the report's last-viewed time, so
  these reports never appear under Popular or Recently viewed on the library home.
- Whether an identity reference reached only through an included script is detected was not checked.
- Export still uses its older permission check (group grants on the folder). A folder owner with no
  grant row, or a reader holding only a report-level grant, can open the live view and is refused
  Export. It predates this work and applies to every report.
- A failure while running live (a script error, a timeout, an unreadable file) answers 500 with no
  reader-facing reason. The run job gave one.
- Live runs are bounded per reader by their one session, but do not pass through the report job
  queue or the tenant quota.
- The live manifest does not carry the owner, steward and tag fields the snapshot manifest adds.
- A reader's parameter state resets when their groups or roles change, on every report.

## Files expected to change

- `src/ETL-SQL.Portal/Services/`: new identity service; `SessionCache`; `ExecutionJobService`.
- `src/ETL-SQL.Portal/Controllers/`: `ExecutionController`, `ExportController`, `ReportsController`.
- `src/ETL-SQL.Portal/BrowserSources/`: `report-viewer.ts`, `report-viewer-view.ts`.
- Tests: `PortalReaderJourneyTests`, `RowLevelSecurityPortalTests`, the viewer script checks.
- Docs: the row-level-security administration page and guide, `CHANGELOG.md`, `TODO.md`.

No database migration. No new dependency.
