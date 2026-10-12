# Studio Reader Journey Evidence

**Tasks:** ST18 complete. ST19 not complete: see [what ST19 found](#st19-two-identities-one-report).
**Run:** 2026-10-12, Windows, production Portal host, Chromium.
**Result (ST18):** one passed, zero failed, zero skipped.

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

`TwoReadersOfOneReportEachSeeOnlyTheirOwnRows` is written and **fails**. It is not in the suite yet.

Two Viewers in different groups open one report whose table is filtered with `HAS_GROUP`. The first
reader runs it and is returned to *Ready for first run*. Nothing is displayed.

Cause, from the code:

- An identity-sensitive report never stores a shared snapshot. That is the rule that stops one
  reader's rows reaching another, and it is correct.
- The Portal viewer shows only snapshots. After a run it asks for the snapshot, gets 404, and
  offers the first run again. The run's own result is written but no route serves it.
- The interactive session behind parameters, saved views and bookmarks is created without an
  execution identity, so `HAS_GROUP` would answer false there.
- Export reads the snapshot too, so it has nothing to export.

So a row-level-secured report cannot be viewed in the Portal UI today. No rows leak: every path
fails closed. The [administration guide](../../administration/platform/row-level-security.md) says
these reports "run fresh on each view"; that is true of the execution and not of the viewer.
`RowLevelSecurityPortalTests` asserts that the job completes and that no snapshot exists. It does
not read rows back as a viewer.

Closing ST19 needs a decision on how a viewer receives their own result. That changes a security
boundary, so it is the owner's call and is not made here.

## Validation and limits

```powershell
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter FullyQualifiedName~AReaderOpensAReport
```

- The report's rows are an inline table, so no connection grant is exercised for the reader.
- One slicer, one bar click and one saved view. The filter and interaction matrices (R10, R11)
  keep their own acceptance.
- Export is offered to the reader and is not downloaded in ST18.
