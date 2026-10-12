# Studio Beginner Acceptance Assessment

**Task:** ST03 — assessment complete. **Verdict: not yet accepted.**
**Assessed:** 2026-10-12, working tree based on 9f966778f318f334a49db644bf4eae804f65ae0c. The tree held
unrelated uncommitted Orchestrator schema changes; no Studio source or test was modified.
**Parent:** [Studio authoring completeness](studio-authoring-completeness.md).

## Verdict

A beginner can complete two of the six required exercises with evidence that meets the acceptance
rules: a dashboard-style report in each host. The desktop ETL exercise passes its run/repair
lifecycle. The other three exercises are not accepted:

- **Portal ETL** cannot be run at all. Saved-pipeline submission (ST04) does not exist yet.
- **Paginated report, Portal** passes only as Admin, and only with SQL the test injects.
- **Paginated report, desktop** has no journey.

No learner has been observed, so "explain the SQL they generated" is unproven in every exercise.
Closing ST03 records this assessment. It does not certify the beginner experience.

> **Since this assessment (2026-10-12, later the same day):** ST04–ST06 and ST10 landed. A Publisher
> can now submit, follow and stop a saved Portal pipeline through the Orchestrator
> ([as built](portal-etl-documents.md#5-slice-2-as-built-2026-10-12)). The Portal ETL *exercise*
> (ST21: deliberate failure, repair, exact rows and cleanup in a real sandbox) is still open, so the
> verdict above is unchanged. The rest of this document is the assessment as it was made.
>
> **Later still (2026-10-12):** ST21 passed on the Orchestrator's in-process executor
> ([evidence](studio-portal-practice-evidence.md)), so *ETL, Portal* criterion 4 is now met for a
> scoped Publisher, outside the sandbox. ST18 gave the dashboard a separate reader with a saved view
> ([evidence](studio-reader-journey-evidence.md)). Still open: a learner has not been observed
> (ST16b), a row-level-secured report cannot be viewed in the Portal (ST19), the sandbox run
> (ST21b), and both paginated exercises. The verdict stays **not yet accepted**.

## Evidence run

Assets were prepared and passed Check mode. Fifteen tests ran; fifteen passed, none skipped.

~~~powershell
dotnet test tests\ETL-SQL.Portal.BrowserTests\ETL-SQL.Portal.BrowserTests.csproj --filter "FullyQualifiedName~StudioContinuousJourneyTests|FullyQualifiedName~StudioDashboardJourneyTests|FullyQualifiedName~StudioPaginatedJourneyTests|FullyQualifiedName~StudioSsisJourneyTests|FullyQualifiedName~StudioPortalPipelineJourneyTests|FullyQualifiedName~StudioDesktopPracticeTests" --logger "trx;LogFileName=st03-journeys.trx" --results-directory .tmp\studio-journey-results
~~~

| Key | Test class | Host and identity | Tests |
| --- | --- | --- | --- |
| FLOW | StudioContinuousJourneyTests | Portal Publisher; desktop host | 2 |
| DASH | StudioDashboardJourneyTests | Portal Admin | 2 |
| PAGE | StudioPaginatedJourneyTests | Portal Admin | 1 |
| ETL | StudioSsisJourneyTests | Desktop host | 8 |
| PORT | StudioPortalPipelineJourneyTests | Portal Admin | 1 |
| PRAC | StudioDesktopPracticeTests | Desktop host | 1 |

A passing test supports only what it asserts. The limits recorded in the
[report inventory](studio-report-authoring-inventory.md) and
[script/ETL inventory](studio-script-etl-authoring-inventory.md) still apply to DASH, PAGE, ETL and PORT.

## Assessment by exercise

Criteria are the five in the parent document: **1** start, **2** build and inspect SQL,
**3** hand edit then reopen a helper, **4** run, check and repair, **5** save, reopen, recover and hand off.

- **Met** — automated, ordinary identity, visible controls or real keystrokes, exact comparison.
- **Partial** — part of the criterion is evidenced; the note names what is missing.
- **None** — no production-host evidence.
- **Blocked** — the product path does not exist yet.

| Exercise | 1 Start | 2 Build | 3 Round trip | 4 Run / repair | 5 Save / hand off |
| --- | --- | --- | --- | --- | --- |
| Dashboard, Portal | Partial | Partial | Met | Partial | Partial |
| Dashboard, desktop | Partial | Partial | Met | Partial | Partial |
| Paginated, Portal | Partial | Partial | None | Partial | Partial |
| Paginated, desktop | None | None | None | None | None |
| ETL, desktop | Partial | Partial | Partial | Met | Partial |
| ETL, Portal | Partial | None | None | Blocked | Partial |

### Dashboard (both hosts, FLOW; Portal also DASH)

1. Starts from a seeded sample report and MOCKDB connection. Creation from Home is unproven (R01).
2. One BAR visual is dragged, mapped and filtered through controls; the helper shows its SQL and a
   nonempty explanation. DASH builds a dataset, four visuals and a slicer, but as Admin.
3. A typed code edit survives reopening and cancelling the helper; the whole buffer is compared.
4. A typed query returns its expected value. Portal renders one viewer row; desktop runs the saved
   script. No exact chart aggregates, and no failure is introduced and repaired.
5. Pre-save, saved and reloaded text match apart from line endings. Draft recovery, a separate
   reader, RLS and scheduling are unproven (ST12–ST15, ST18, ST19, ST11).

### Paginated report

1. Portal only, Admin, seeded `AS PAGINATED` report.
2. Parameter, bands, total, header/footer, page break and page setup are built through controls.
   The test then injects the parameter predicate and repoints the bands at the staged table with
   `editor.setValue`. Without that, the prompt changes nothing on the page (R15).
3. No hand-edit and helper-reopen assertion exists for this workflow.
4. PDF export with `All` produces multiple pages, page numbers and a header date. Filtered rows,
   totals and repair are unchecked (R16–R18).
5. The saved text is read back, but the same string is supplied as both comparison arguments.

### ETL

1. Desktop opens a prepared exercise in a disposable workspace. Portal creates a pipeline from Home
   as Admin.
2. Desktop builds extract, load, reshape, summarise and join tasks from the palette and checks the
   generated script; expressions are still typed. Portal injects the script.
3. Desktop edits task fields in place and repairs by keyboard. No test hand-edits a task and then
   reopens its helper to prove untouched text survives (E14). Portal has none.
4. Desktop: the ASSERT failure, diagnostic, keyboard repair, exact loaded rows, same-session
   cleanup and a repeat run all pass. Portal: blocked on ST04, then ST21.
5. Desktop saves the repaired script. Portal reopens exact normalized text as Admin. Host-restart
   recovery, ordinary-author grants, schedule and promotion are unproven (E22, ST10, ST07–ST09).

## Answers to the ST03 questions

| Question | Reports | ETL |
| --- | --- | --- |
| Can users complete the workflow? | Dashboard: yes, from seeded data, in both hosts. Paginated: not without writing the parameter binding by hand. | Desktop: yes for the bounded practice exercise. Portal: authoring only; no execution. |
| Do helper and code round trips keep work? | Yes for one visual helper in both hosts. | Unproven as a round trip; task field edits and saved text are covered. |
| Can they explain the generated SQL? | Not observed. The helper presents an explanation. | Not observed. |

## Remaining gaps

Ordered by how much of the matrix each one unblocks.

| Gap | Affects | Owner |
| --- | --- | --- |
| Saved Portal pipeline submission, then the Portal practice exercise | ETL Portal 2–4 | ST04, ST21 |
| Guided paginated parameter binding; no injected SQL | Paginated 2, 4 | R15; needs its own sized task |
| Ordinary-author paginated journey on both hosts with exact rows and totals | Paginated, all criteria | R16–R18; needs its own sized task |
| Learner walkthrough: explain one generated clause and the repair | Criterion 2, every exercise | ST16, ST17, ST21 |
| Start from Home as an ordinary author | Criterion 1, every exercise | R01, ST10 |
| Introduce and repair a failure in a report | Dashboard and paginated 4 | R14 |
| Hand edit a pipeline task, reopen its helper, compare untouched text | ETL 3 | E14; supplemental |
| Private drafts, crash recovery, publish to a reader, RLS, schedule | Criterion 5 | ST12–ST15, ST18, ST19, ST11, ST07 |
| Independent pre-save capture in DASH, PAGE and the SSIS certification | Criterion 5 | Fix when those journeys are next strengthened |

R15 through R18 and E14 have no ST task. Size them before v0.21.0 claims a paginated or guided-ETL
beginner journey.

## Host-specific limits

- **Portal interactive Run** accepts one read-only query. Mutating pipelines must be saved and
  submitted through the Orchestrator sandbox; there is no in-process fallback.
- **Portal** owns catalog grants, leases, tenant isolation, publishing and scheduling. Unpublished
  work has no private draft store until ST12 decides one.
- **Desktop** owns project files, Git and host lifecycle. Each Run is a fresh session, so staged
  tables do not carry between runs. Execution is trusted local execution, not a sandbox, under a
  60-second timeout, 128 MB operator grant, 256 MB session ceiling and 1,000 returned rows.
- **Desktop** has no catalog, reader identity, RLS delivery or scheduler. Those rows are host
  boundaries, not gaps.

## Claim boundary

v0.21.0 may claim the automated beginner dashboard sequence in both hosts and the desktop ETL
practice exercise. It may not claim a beginner paginated journey, a Portal ETL lifecycle, or
observed learner understanding.
