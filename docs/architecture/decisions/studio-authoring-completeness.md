# Studio Authoring Completeness

**Status:** ST01 and ST02 inventories complete; ST03 assessed 2026-10-12 — beginner experience not yet accepted.
**Decision:** Studio combines the ReportBuilder and WorkstationEditor foundations into one SQL-first
product across desktop and Portal. Completeness and a better beginner experience define success.
**Scope owner:** [Studio design](etl-sql-studio.md), with task status in [TODO §2](../../../TODO.md#2-studio--unified-authoring-experience-and-completeness-criteria).

## Acceptance approach

Users familiar with Power BI, Tableau, SSRS, and SSIS are the intended audience. Those names describe
user expectations, not a promise to clone every feature or import their proprietary artifacts.
A beginner should build a useful result through helpers, inspect what SQL was generated, make a
small code edit, and continue with the helpers without losing that edit. SQL stays authoritative.

Each required workflow needs separate desktop and Portal evidence. Record the source revision,
fixture and method, host, identity/grants, observed result, saved/reopened artifact, and run result.
An implementation reference or existing test is a starting point, not a recorded passing run.
No tests were run for this initial inventory. Rows below define the acceptance work still needed;
they do not establish that a capability is absent merely because its journey remains open.

## ST01 — Report-authoring inventory

The [detailed report-authoring inventory](studio-report-authoring-inventory.md) completes ST01 with
24 workflow mappings, separate Portal/desktop coverage limits, named missing acceptance fixtures,
beginner improvements, and deliberate non-goals. This closes the inventory task only; it does not
certify Studio report authoring. No browser tests were run for the source review.

The first priorities are effective parameter binding in paginated reports, ordinary-author dashboard
creation from Home, correct report data and PDF output on both hosts, and reachable page/layout/history
controls. See that inventory for source references and bounded fixture specifications.

## ST02 — Script and ETL-authoring inventory

The [detailed script and ETL inventory](studio-script-etl-authoring-inventory.md) completes ST02 with
26 workflow/fixture mappings covering code assistance, files, Git, guided ETL, execution scope,
results, cancellation, recovery and host lifecycle. Each row separates desktop and Portal evidence
and defines the expected acceptance result. No runtime tests were run for this review.

The first priorities are an executed failure/repair ETL journey, saved Portal pipeline submission,
production-host editor assistance, clear execution-context/cancellation behavior, and recovery beyond
a healthy running host. Inventory completion does not establish passing acceptance.

## Evidence limits found in the first pass

- The reviewed dashboard and paginated-report journeys call PortalBrowserFixture.SignInAsync,
  which signs in as Admin. They do not establish ordinary-author coverage.
- [StudioCertification](../../../tests/ETL-SQL.Portal.BrowserTests/StudioCertification.cs) checks
  save/reload only when the optional reloaded argument is supplied. That comparison normalizes line
  endings and trims trailing whitespace. Its non-canvas round-trip branch checks preserved lines
  by containment, which alone does not prove ordering or exact text preservation. Record dedicated
  preservation assertions for the claimed journey; a certification method name is insufficient.
- Production-host behavior and beginner understanding need separate evidence. Browser automation can
  verify generated text and output; an observed learner walkthrough must establish whether the user
  understands the change and can repair a small mistake without an external editor.

## ST03 — Combined beginner acceptance

Use one dashboard, one paginated report, and one staged ETL exercise in each production host.
For each exercise, collect evidence that the user can:

1. Start from sample data or an authorized connection and choose the right authoring workflow.
2. Build a useful result through helpers, inspect the changed SQL, and explain one generated clause.
3. Make a small hand edit, reopen the helper, and preserve unrelated text and editor state.
4. Run or preview within the host's declared permissions, check expected output, and repair a failure.
5. Save/reopen, recover interrupted work, and complete the applicable publish/export/schedule handoff.

For each friction point, record the foundation's existing steps, Studio's observed steps, help needed,
and the improvement or remaining gap. Do not infer usability from control count. Narrow future
feature work to the gaps found.

**Outcome:** the [ST03 assessment](studio-beginner-acceptance-assessment.md) of 2026-10-12 closes the
task with the verdict *not yet accepted*. The dashboard exercise is evidenced in both hosts and the
desktop ETL exercise passes its run/repair lifecycle. Portal ETL execution is blocked on ST04, the
paginated exercise passes only as Portal Admin with injected SQL and has no desktop journey, and no
learner has been observed. Closing ST03 records the assessment; it does not certify the experience.
Repeat the five-criterion check when those gaps close.

## Host differences and non-goals

- Desktop owns project files and local host lifecycle. Portal owns catalog permissions, leases and
  tenant isolation; mutating pipeline execution follows the accepted [Orchestrator handoff](portal-etl-documents.md).
- Shared workflows should feel consistent. Unavailable host capabilities need explicit explanations.
- Removing ReportBuilder or WorkstationEditor is not a completion criterion. Neither is replacing
  SQL with hidden visual state, requiring VS Code/TUI to finish an exercise, or implementing every
  feature of the tools familiar to the target audience.
- Mobile layout authoring retains its separate roadmap scope. This inventory adds no feature or test
  implementation and makes no new shipped-feature claim.

## Current acceptance evidence

The [beginner report journeys](studio-beginner-report-evidence.md) passed their automated sequences
on 2026-10-11 in Portal as a Publisher and in the desktop host. Both compare independent pre-save
text with saved and reopened text, exercise keyboard editing and helper explanations, and execute
the saved report. ST16/ST17 closed on 2026-10-12 for that automated sequence; the learner’s own
explanation is ST16b, with a [walkthrough sheet](studio-learner-walkthrough.md) that has not been run.

On 2026-10-12 the six journey classes behind ST03 (15 tests) passed at 9f966778f; the
[assessment](studio-beginner-acceptance-assessment.md) records what each one does and does not establish.

## Next acceptance work

ST01 and ST02 are complete as inventories, and ST03 is complete as an assessment with open gaps:
the required paginated and Portal ETL journeys and all learner observations. ST20 is defined in the
[practice-execution decision](studio-practice-execution.md). Record the ST16b learner observations;
ST22’s [desktop failure/repair exercise](studio-desktop-practice-evidence.md) passed. ST04–ST06 and
ST10 landed on 2026-10-12: a Publisher submits, follows and stops a saved Portal pipeline through the
Orchestrator ([as built](portal-etl-documents.md#5-slice-2-as-built-2026-10-12)). ST21’s
[practice exercise](studio-portal-practice-evidence.md) and ST18’s
[reader journey](studio-reader-journey-evidence.md) passed the same day. ST21 ran on the in-process
executor; ST21b repeated it on a real Standard sandbox on 2026-10-17
([evidence](studio-sandbox-submitter-identity.md#acceptance-evidence--2026-10-17)). ST19 passed after a fix: a
row-level-secured report could not be viewed in the Portal at all, and is now shown live to each
reader ([design](portal-rls-report-viewing.md)). The inventories name supplemental work, including editor assistance and
guided paginated parameter binding, that needs its own sizing before implementation.
