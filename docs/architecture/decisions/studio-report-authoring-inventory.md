# Studio Report-Authoring Completeness Inventory

**Task:** ST01 — inventory complete; acceptance fixtures below remain unverified.
**Reviewed:** 2026-10-10, working tree based on commit 525e8fbb6acbd1c011eed582fd040832ad905ba2.
The tree contains local changes; this is a source review, not a passing build or release certification.
**Parent:** [Studio authoring completeness](studio-authoring-completeness.md).

## Scope and evidence rules

Studio combines ReportBuilder's visual authoring with code editing and guided workflows in desktop
and Portal. This inventory maps the report foundation, identifies beginner improvements, and defines
missing acceptance fixtures. Completing ST01 means every required workflow has an evidence path.
It does not mean those workflows pass. ST03 remains dependent on actual production-host results.

Coverage codes below describe inspected source, not execution results:

- **P — Partial fixture:** an inspected production-host test exercises part of the workflow, with the
  limits recorded below. It has not been run for this inventory.
- **U — Unverified:** shared implementation or sandbox coverage exists, but the reviewed tests do not
  establish this complete workflow on that host. This is not a claim that no other test exists.
- **G — Observed gap:** source or fixture steps expose a missing part of the intended beginner flow.
- **N/A — Host boundary:** an explicitly different host responsibility, not a missing shared feature.

For every fixture, use real parser/patcher and execution responses. Portal authors have explicit
folder, authoring and connection grants without Admin; readers use a separate identity. Desktop uses
the production WorkstationEditor host and local workspace policy. Fixture setup may seed data and
permissions, but the authoring actions must use visible controls or ordinary editor keystrokes.
A JavaScript call that changes editor text cannot stand in for a beginner authoring step.

Capture editor text before Save, compare it with host-returned text and the reopened buffer, and
state the line-ending policy. Untouched ranges must survive edits; reopening or cancelling a helper
must not change text. Record host, identity/grants, revision, test method, result and artifacts.

## Subsequent acceptance run

On 2026-10-11 the two FLOW tests were strengthened and passed: Portal now uses a Publisher, both
use keyboard code edits and helper explanations, independently compare pre-save/saved/reopened text,
and execute the saved report. [Run evidence and limits](studio-beginner-report-evidence.md) supersede
the historical FLOW row and its persistence limitation below. Other rows remain source-review findings.

## Inspected evidence

| Key | Source / fixture | What it establishes and what it does not |
| --- | --- | --- |
| DASH | [StudioDashboardJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioDashboardJourneyTests.cs), Certifies_ThePowerBiLikeDashboardJourney | Portal Admin; seeds a report and shared connection through APIs, then creates a named dataset, CARD/LINE/BAR/TABLE, titles and a promoted slicer through controls. Reads saved text and checks a viewer mark is visible. Does not start from Home, click the slicer to check expected rows, or assert all numeric outputs. |
| PAGE | [StudioPaginatedJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioPaginatedJourneyTests.cs), Certifies_TheSsrsLikePaginatedJourney | Portal Admin; seeded report, parameter, summary/details, total, header/footer, page break, A4 landscape and PDF download. Checks multiple pages, page numbers and header date. Injects the parameter predicate and redirects visual sources using editor.setValue. Exports with All; does not verify filtered rows, total values or an ordinary-author desktop path. |
| FLOW | [StudioContinuousJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioContinuousJourneyTests.cs), Portal_DrivesTheContinuousJourneyFromConnectToRun and Desktop_DrivesTheContinuousJourneyFromConnectToRun | Both production hosts; selects a seeded connection/table, drags a visual, assigns roles, filters and enters Split. Appends a comment programmatically, runs a selected statement and checks a result trace exists. This is not a whole-report correctness assertion or a learner code-edit observation. |
| INT | [StudioInteractionJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioInteractionJourneyTests.cs) | Portal Admin with seeded visuals; tests selective filter targets, Ctrl-click selection, drill-down/right-click action, row detail, field/popover tooltips, goal/reference band and annotation placement. Several checks assert actual rows or chart geometry. Does not cover ordinary-reader isolation or the complete workflow on desktop. |
| SAVE | [StudioPersistenceTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioPersistenceTests.cs) | Portal save/reload/conflict, document context, connection wizard, numeric/date filters and visual mutations. VisualMutations_UseCanonicalPatcherAndPreserveHandAuthoredSql invokes Studio methods and injects a snapshot; it checks a protected SQL prefix, not an end-to-end user gesture sequence. |
| WIZ | [StudioWizardContractTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioWizardContractTests.cs) | Serves the UI sandbox, not either production host. Checks helper previews/explanations, parameter edit/delete, number/date formats, and reopening helpers after hand edits. Useful component coverage; insufficient production-host evidence. |
| DATA | [StudioDesktopDatasetWizardTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioDesktopDatasetWizardTests.cs), AConnectionDeclaredAMomentAgo_ListsItsTables | Desktop; delays analysis and checks a newly declared connection lists Users. Does not create/save a dataset or finish a report. |
| REC | [StudioRecoveryJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioRecoveryJourneyTests.cs) and [StudioDesktopRecoveryTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioDesktopRecoveryTests.cs) | Existing recovery fixtures to reuse. Unpublished Portal draft acceptance stays governed by ST12–ST15; recovery of a catalog report does not close it. |

Portal identity is confirmed by [PortalBrowserFixture.SignInAsync](../../../tests/ETL-SQL.Portal.BrowserTests/PortalBrowserFixture.cs),
which signs in as Admin; StudioAuthoringFixture inherits it. Role-navigation tests exercise access to
entry points but do not establish an ordinary author's complete report journey.

DASH, PAGE and FLOW pass the same reloaded string as both the artifact and comparison argument to
[StudioCertification.Certify](../../../tests/ETL-SQL.Portal.BrowserTests/StudioCertification.cs).
That equality cannot detect a difference between pre-save text and saved text. Preserve their useful
syntax and output assertions, but require an independent pre-save capture in the acceptance fixtures.

## Source ownership

These are live implementation entry points, not assertions that every control is reachable in Studio:

- **Data:** [studio-authoring-data.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-data.ts)
  owns reuse/create/live-query choices, table/query selection and sample binding. Its source pane offers
  a whole table or a query workbench; guided multi-table shaping is not established by this path.
- **Visuals:** [studio-authoring-chart.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-chart.ts)
  owns visual type/role selection and previews; [designer-inspector.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-inspector.ts)
  owns report/visual settings, mappings, container membership and interactions.
- **Layout:** [designer.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer.ts),
  [designer-visual-actions.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-visual-actions.ts)
  and [designer-history.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-history.ts)
  provide page/visual actions, bookmarks, undo/redo and clipboard operations.
- **Integration:** [studio-visual-stage.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-visual-stage.ts)
  mounts the designer with hideTopbar and hideSidebar. A control in the foundation is not automatically
  reachable in Studio. Page/layout/recipe acceptance must exercise the actual Studio entry point.
- **Paginated workflow:** [studio-report-workflow.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-report-workflow.ts)
  selects the report workflow and page settings; [studio-authoring-report.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-report.ts)
  owns parameters, details, totals and furniture. Parameter creation explicitly tells the author to
  reference it in a dataset query. Details bind to the current sample, not automatically to a separately
  parameterized staging table. This explains the manual SQL steps in PAGE.
- **Delivery:** [studio-authoring-preview.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-preview.ts)
  owns preview, parameter prompts, pagination and PDF export. CSV/Excel directs users to Results.
  [studio-file-commands.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-file-commands.ts)
  owns Save; an unpublished Portal document is currently directed toward publishing or keeping its tab open.

## Workflow matrix

Each R fixture is defined below. P/G/U describe the current evidence for that row; none is certified.
A named missing fixture satisfies inventory scope without claiming the product is complete.

| ID | Foundation workflow and required Studio improvement | Portal | Desktop | Evidence / acceptance |
| --- | --- | --- | --- | --- |
| R01 | Start a dashboard or paginated report from Home; correct workflow and safe handling of ambiguous/mixed pages | U | U | DASH/PAGE start with seeded reports; workflow module exists. R01 fixture. |
| R02 | Connect or choose authorized sample data; discover tables/fields and explain failed discovery | P | P | SAVE connection wizard, DASH data choice, DATA analysis race. R02; ST16/ST17. |
| R03 | Create, name and reuse a dataset; distinguish shared, script-local and live-query sources | P | U | DASH creates one dataset; WIZ reuse/preview is sandbox only. R03. |
| R04 | Shape data before charting: choose columns, filter, sort, join and derive a value with visible SQL | U | U | Data wizard offers a table or authored query. Guided report-shaping outcome needs R04; do not assume ETL helpers complete this report flow. |
| R05 | Add chart, KPI and table; assign/reassign fields, aggregation and repeatable roles | P | P | DASH four types; FLOW drag and role clicks. R05; ST16/ST17. |
| R06 | Set report title/theme and visual title, number/date format, axes and legend | P | U | DASH titles; WIZ formats; inspector source. R06 validates saved settings and rendered values. |
| R07 | Add/switch pages; arrange, move, resize, align and tidy visuals | U | U | Foundation controls; Studio hides its topbar. R07 must prove reachable controls and persisted layout; ST25 for keyboard move/resize. |
| R08 | Group/nest visuals, choose container sections, fold/unfold and detach | U | U | Foundation canvas/inspector; R08 validates hierarchy and tab/section content after reopen. |
| R09 | Duplicate, copy/paste, delete, undo and redo without corrupting SQL or references | P | U | SAVE programmatic duplicate/delete; history module. R09; ST23/ST24 cover keyboard access only. |
| R10 | Apply dataset-wide versus visual-local filters and promote categorical/numeric/date controls | P | P | SAVE scope/typed controls, DASH promotion; FLOW one visual filter. R10 checks affected and unaffected outputs. |
| R11 | Configure interaction targets, multi-selection, drill-down and page/parameter actions | P | U | INT actual filtering/drill checks; not every action covered. R11; ST18/ST19 reader boundaries. |
| R12 | Configure field/popover tooltips, table row detail and analytical overlays | P | U | INT tooltip/detail/goal/band/annotation checks. R12 verifies representative features and untouched unsupported options. |
| R13 | Create/edit/default/delete author bookmarks and reopen a reader's saved view | U | U | Foundation bookmark actions; author bookmarks and reader views are separate. R13; ST18 for Portal saved view. |
| R14 | Edit code, reopen a helper, cancel, repair a syntax error and preserve unrelated text/editor position | P | P | SAVE protected prefix; FLOW injected comment; WIZ sandbox round trips. R14; ST16/ST17 and ST38/ST39 for load failure. |
| R15 | Add/edit/delete report parameters and bind them to the data used by every intended visual | G | U | PAGE injects predicate/source changes; parameter helper owns declarations. R15 requires effective filtering through user actions. |
| R16 | Choose group/detail fields, matrix summaries and totals; retain correct data binding | P | U | PAGE builds bands and total but rewrites their sources; no expected total assertion. R16. |
| R17 | Configure header/footer, page size, orientation, margins, breaks and pagination | P | U | PAGE PDF checks page count/header/footer; does not cover all settings or visual clipping. R17. |
| R18 | Preview with different parameter answers and export a correct PDF | P | U | PAGE uses All and checks multi-page output. R18 checks subset, amounts, dimensions and content completeness. |
| R19 | Export the intended result rows as CSV/Excel; explain distinction from report PDF | U | U | Export dialog delegates to Results. R19 must verify a reachable host-specific route and exact rows/columns. |
| R20 | Save/reopen and handle conflicts, private drafts and crash recovery | P | P | SAVE/REC/FLOW partial coverage; unpublished Portal save limit in source. R20; ST12–ST15, ST40/ST41, ST52. |
| R21 | Publish to a permitted catalog folder and deliver to a separate reader with correct RLS | U | N/A | Portal catalog responsibility. R21; ST15, ST18/ST19. Desktop file/export covered by R18–R20. |
| R22 | Hand off a report for authorized scheduled execution and inspect its artifact | U | N/A | Portal/Orchestrator handoff ST11; R22. Local scheduling UI is not required for ST01. |
| R23 | Save and inspect source-control changes where the host supports them | U | U | Foundation Save/Commit and Studio file commands; R23 requires explicit unavailable behavior when unconfigured. |
| R24 | Complete report authoring with keyboard, usable zoom/focus and meaningful assistive labels | U | U | ST23–ST29 own the bounded host journeys. R24 ties them to the report controls above. |

## Missing acceptance fixtures

These are fixture specifications, not new test implementations. Run each in both hosts unless its
matrix row declares a host boundary. Reuse small fixed datasets; each result must have a predetermined
row set and amounts, not merely a nonempty canvas. Where existing tasks cover only part of a fixture,
keep the remaining work separately estimated rather than enlarging a bounded task silently.

| Fixture | Concrete action and expected result | Backlog routing |
| --- | --- | --- |
| R01 | From Home create each report kind, save/reopen, and verify its inferred workflow. Open one mixed-mode report; choose a workflow without dirtying the script. | ST16/ST17 cover first creation; mixed-mode fixture is additional acceptance work. |
| R02 | Choose MOCKDB, inspect fields, then force a schema denial/failure. A useful sample appears on success; failure gives a remedy and leaves the buffer intact. | ST16/ST17 for sample path; separate discovery-failure fixture. |
| R03 | Create a named dataset, bind two visuals, reopen the helper and reuse it without duplicate declarations. Exercise one permitted shared dataset and one live query; verify the source used after reopen. | Additional dataset lifecycle fixture. |
| R04 | With a fixed sales table and region lookup, select fields, join the lookup, derive an amount, filter and sort. Build a report using that result and compare expected rows/amounts. Observe where the user must write SQL and define the smallest helper improvement for that step. | Separate report-data-shaping assessment/implementation task; not silently added to ST16/ST17. |
| R05 | Create CARD, LINE, BAR and TABLE using the same dataset; change an aggregation and add/remove a table column. Verify values, labels and bindings in preview and after reopen. | ST16/ST17 cover one visual; broader roles/aggregation fixture separate. |
| R06 | Change report theme/title and currency/date formats on two visuals. Reopen and verify rendered values, titles, axes and legend; opening the style panel alone must not edit the script. | Separate formatting fixture; WIZ is supporting coverage. |
| R07 | Create two pages, move/resize two visuals, align/equal-size and tidy them. Reopen and assert page membership and final geometry; perform move/resize without dragging. | ST25 keyboard subset; page/layout lifecycle separate. |
| R08 | Nest two visuals in a tabbed container, assign sections, fold/unfold, detach one and reopen. Check both content and saved parent/section relationships. | Separate container lifecycle fixture. |
| R09 | Duplicate and copy/paste one visual with unique names, delete a copy, then undo/redo. Save/reopen each final state; untouched preparation, parameter references and original visual remain correct. | Separate history/clipboard fixture; ST23/ST24 supply keyboard coverage. |
| R10 | Filter one of two visuals, then their shared dataset; verify the precise affected rows. Promote categorical, numeric and date filters, change each reader control and compare expected outputs. | ST18 reader subset; full filter/promotion matrix separate. |
| R11 | Link three visuals with one excluded target, select one and two categories, drill to detail and navigate to another page. Assert target rows, excluded rows, destination and parameter state. | ST18/ST19 for ordinary reader/RLS; desktop counterpart and action matrix separate. |
| R12 | Add a formatted field tooltip, a filtered popover, row-detail binding, goal/band and annotation. Inspect row content and positions after reopen. Preserve one unsupported hand-authored option when another setting changes. | Separate richer-interaction fixture, reusing INT. |
| R13 | Author two bookmarks, edit/default/delete one and save/reopen. Separately, a Portal reader saves a view and reopens it without author controls. | Author bookmarks separate; reader view ST18. |
| R14 | Type a real code change, reopen a helper, cancel it, then make a visual edit. Check unchanged text, selection/scroll and successful execution. Introduce/repair a syntax error; retain last valid canvas and text. | ST16/ST17; deeper preservation assertions support those tasks. |
| R15 | Create a parameter through controls, bind it to a dataset and use it in a matrix and table. Change from All to one category and verify both outputs narrow. Edit name/default and handle deletion/references explicitly. No injected source rewrite. | Separate guided parameter-binding fixture and any needed implementation. |
| R16 | Build a group summary, detail table and grand total over known amounts. Verify every row and both totals for All and a category subset after save/reopen. | Separate both-host paginated-data fixture; PAGE is partial coverage. |
| R17 | Change page size/orientation/margins; add header/footer and a break. Inspect pagination preview and PDF dimensions, repeated bands and an explicit row near a page boundary. No clipped or missing required content. | Separate both-host print-layout fixture. |
| R18 | Export All and one category. Parse PDF page content; verify row completeness, filtered totals and page numbering. Compare preview and export under the same parameters. | Separate paginated-delivery fixture; do not close from download/page-count alone. |
| R19 | Export a known result as CSV and Excel from the visible workflow. Check headers, row count and exact values; verify text and decimal handling. The UI must explain which result is exported. | Separate result-export handoff fixture. |
| R20 | Compare pre-save, saved and reopened text; introduce an external revision conflict and recover a dirty draft after restart. A learner without publish rights can retain a permitted private draft once that policy is implemented. | ST12–ST15, ST40/ST41, ST52; desktop recovery under ST02. |
| R21 | Publish the saved report as an ordinary author, open it as a separate reader and then a second RLS identity. Check disjoint rows in exposed preview/data/export paths and denied author controls. | ST15, ST18/ST19. |
| R22 | Schedule a saved report under the intended identity, trigger one occurrence with a controlled clock and inspect the expected artifact and data. | ST11. |
| R23 | With Git configured, save then inspect a diff showing only intended script edits and complete the supported commit action. Without Git, explain the unavailable action while Save still works. | ST02 source-control inventory; no requirement for a new hosted Git service. |
| R24 | Traverse the report workflow with keyboard and declared zoom; verify focus return and screen-reader announcements for errors and changes. Keep the observed transcript. | ST23–ST29; learner explanation remains ST03. |

## Improvement priorities and deliberate boundaries

1. **Effective paginated parameter binding:** PAGE needs programmatic SQL repairs; the source confirms
   declaration and data binding are separate. Start by reproducing R15 through the visible UI and
   identifying the smallest missing helper step. Keep manual SQL editing fully supported.
2. **Ordinary-author start-to-finish dashboard:** extend the useful DASH path through Home, independent
   save/reopen capture and expected output under ST16/ST17. Avoid treating Admin success as learner access.
3. **Correct paginated data and delivery on both hosts:** R16–R18 distinguish a multi-page PDF from a
   correct report and establish desktop evidence independently of the ETL journey.
4. **Reachable foundation operations:** test R07–R09 in Studio itself. Hidden foundation chrome makes
   source presence an unreliable measure of user access.

Required baseline visuals are the four in R05 plus MATRIX, text bands and filter controls used by
these fixtures. Specialist visual types remain supported through existing contracts, but exhaustively
certifying every type is outside ST01's bounded beginner baseline. Preserve unsupported handwritten
options; a helper must explain when it cannot edit one safely.

Proprietary artifact import, a Power BI/Tableau/SSRS feature clone, mobile layout authoring, external
editor fallback and removal of either foundation are non-goals. Desktop does not need an in-process
Portal catalog or a new local scheduler to pass report authoring. SQL and source control remain
first-class; advanced queries are allowed, while required beginner paths must make their SQL-writing
steps visible in the acceptance record.

## Completion record

ST01's deliverable is complete: foundation operations, both-host coverage limits, beginner improvements,
missing fixtures and non-goals are recorded. No feature or test was implemented, and no browser test
was run. All R fixtures still need execution evidence or implementation work before they can support
ST03. ST02 is also complete as an inventory; see the [script and ETL mapping](studio-script-etl-authoring-inventory.md).
