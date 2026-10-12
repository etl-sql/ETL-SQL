# Studio Reader Journey Evidence

**Tasks:** ST18 and ST19 complete.
**Run:** 2026-10-12, Windows, production Portal host, Chromium.
**Result:** two passed, zero failed, zero skipped.

[PortalReaderJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/PortalReaderJourneyTests.cs)
reads a report as someone who did not write it. No administrator signs in.

| Person | Identity |
| --- | --- |
| Author | A new account with exactly the `Publisher` role, owning one folder. Publishes the report through Studio's create action. |
| Reader | A new account with exactly the `Viewer` role. Belongs to one group that holds `Execute` on the author's folder. |

## ST18: verified sequence

1. The reader finds the report in the library by its name and opens it.
2. The reader runs it under their own `Execute` grant and sees all three regions.
3. **Parameter.** Choosing South in the report's slicer leaves exactly sales 3 and 4.
4. **Saved view.** Views, then *Save current view as…*, names and keeps that state.
5. **Interaction.** Clicking another region's bar moves the same parameter; the table follows.
6. **Reopen.** From the library again the report opens unfiltered. Choosing the saved view brings
   back South, sales 3 and 4, and the slicer's value.
7. **No author controls.** The Design button and the Studio navigation entry are absent, not
   disabled. Asked for directly, the script and delete routes answer 403 and the stored script is
   unchanged. No other request the reader's pages made was refused or failed.

Another reader's saved view staying invisible is covered at the API by `SavedReportViewStateTests`.

## Defect ST18 found

**A Portal reader could not save a view.** The report runs in its own frame, and that frame never
loaded the feedback module. *Save current view as…* threw before its name prompt opened; the same
was true of every runtime confirmation and notice inside a Portal report. The frame document now
loads the module. Saved views had API tests and no browser test.

## ST19: two identities, one report

`TwoReadersOfOneReportEachSeeOnlyTheirOwnRows` passes. Two Viewers, each holding only Read on the
author's folder through a different group, open one report whose table is filtered with `HAS_GROUP`.
Sales 5 and 6 belong to a region neither reader's group covers.

| Step | North reader | South reader |
| --- | --- | --- |
| Opens the report; it runs at once, labelled *Live · your rows* | sales 1, 2 | sales 3, 4 |
| Sets the minimum total to 70 in the report's slicer | sales 1, 2 | sale 3 |
| Saves a view, reopens the report from the library, applies the view | | sale 3 |
| Exports CSV | North rows only | South rows only |
| Presses Refresh | sales 1, 2 | |
| Subscribes from the live view | one subscription kept | |

Neither reader sees West on screen or in a file. No snapshot row exists afterwards. Neither reader is
offered Run Report or Design. The server-side cases (a reader removed from a group, a reader with no
grant, the administrator bypass, nothing left in snapshot storage, one delivery run per recipient) are
listed in [the design's as-built table](portal-rls-report-viewing.md#as-built).

### What the first attempt found

Before the fix, the first reader ran the report and was returned to *Ready for first run*. Nothing
was displayed. Cause, from the code:

- An identity-sensitive report never stores a shared snapshot. That is the rule that stops one
  reader's rows reaching another, and it is correct.
- The Portal viewer shows only snapshots. After a run it asks for the snapshot, gets 404, and
  offers the first run again. The run's own result is written but no route serves it.
- The interactive session behind parameters, saved views and bookmarks is created without an
  execution identity, so `HAS_GROUP` would answer false there.
- Export reads the snapshot too, so it has nothing to export.

So a row-level-secured report could not be viewed in the Portal UI. No rows leaked: every path
failed closed. The [administration guide](../../administration/platform/row-level-security.md) says
these reports "run fresh on each view"; that was true of the execution and not of the viewer.
`RowLevelSecurityPortalTests` asserted that the job completes and that no snapshot exists. It did
not read rows back as a viewer.

The owner decided on 2026-10-12 that such a report is shown live from the reader's own session, that
Read is enough to open it, and that Subscribe is offered:
[Viewing row-level-secured reports in the Portal](portal-rls-report-viewing.md).

The fix also found that a run of such a report left its result file in snapshot storage with no
snapshot row, so nothing pruned one reader's rows. That file is no longer written.

## Validation and limits

```powershell
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter FullyQualifiedName~PortalReaderJourneyTests
```

- The report's rows are an inline table, so no connection grant is exercised for the reader.
- One slicer, one bar click and one saved view. The filter and interaction matrices (R10, R11)
  keep their own acceptance.
- Export is offered to the reader and is not downloaded in ST18. ST19 downloads and reads the CSV.
- Both journeys open the report through its folder. The library home lists what the whole Portal
  has viewed most, so a report nobody has opened is not on it once any other report has views.
- ST19 covers one report, one parameter and CSV. Excel and PDF use the same manifest and are not opened.
