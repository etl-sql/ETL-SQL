# Studio Script and ETL Authoring Completeness Inventory

**Task:** ST02 — inventory complete; acceptance fixtures remain unverified.
**Reviewed:** 2026-10-10, working tree based on 525e8fbb6acbd1c011eed582fd040832ad905ba2.
Local changes are present. This is source/test inspection, not a passing build or runtime certification.
**Parent:** [Studio authoring completeness](studio-authoring-completeness.md).

## Scope and evidence rules

WorkstationEditor contributes the desktop host, workspace, language services and execution services.
Studio combines these with the shared editor and guided ETL canvas. The required result is a complete
SQL-first workflow for a new query author or a user familiar with SSIS, in desktop and Portal.

- **P — Partial fixture:** inspected production-host browser coverage exists, but has not been run
  for this inventory and does not prove the complete row's acceptance.
- **U — Unverified:** source, API/service tests or component checks provide a starting point, but the
  inspected coverage does not establish the full browser workflow on that host.
- **G — Observed gap:** the inspected path does not complete the intended workflow.
- **N/A — Host boundary:** an explicitly different host responsibility.

Every E fixture below names an expected result. Run both hosts unless a host boundary is recorded.
Use ordinary scoped Portal identities and the production desktop host; create data/grants during
setup, then author through visible controls or real editor keystrokes. Keep real parser/patcher and
execution responses. Record revision, host, grants, fixture method, output and diagnostics. Capture
text independently before saving and compare saved/reopened content under the declared line-ending
policy. Source presence, a test name, nonempty results or injected editor text alone cannot certify
a beginner journey. No feature or test implementation belongs to this inventory task.

## Inspected evidence and limits

| Key | Source / fixture | Actual coverage and limits |
| --- | --- | --- |
| ETL | [StudioSsisJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioSsisJourneyTests.cs) | Desktop production host. Certifies_TheSsisLikeEtlJourney programmatically appends extraction/transformation SQL, adds validation/parallel/execution tasks, inspects staged scope and saves. It never runs the pipeline. Its load tasks create tables; they do not demonstrate loaded row correctness. The saved string is supplied as both certification arguments. |
| HELPERS | Same class: AnExtractAndALoadAreBuiltFromThePalette and TransformsAreBuiltFromThePaletteOnStagedTables | Desktop UI authors extraction/load and staged reshape/summary/join helpers, checking generated script and task presence. Expressions such as derived columns and measures are still typed. This is stronger guided-authoring evidence than the older class comment suggests, but not end-to-end executed ETL evidence. |
| FLOW | Same class: AnIfIsGivenAnElseAndEachBranchATask, ATasksFieldsAreEditedInPlace, ParallelBranchesAreDrawnAsLanes | Desktop branch/task editing and lane geometry fixtures. Their presence does not certify success/failure dependency semantics, rollback, cancellation or expected loaded data. |
| DESK | [DesktopStudioJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/DesktopStudioJourneyTests.cs) | AuthenticatedDesktopHosts_AuthoringShutdownRelaunchAndProjectWindows exercises Git diff against an injected edit, Run with a result trace, save/reload, file/folder rename/move/delete, two projects, dirty Exit and relaunch. Exit_WaitsForTheProjectHostToStopBeforeReportingSuccess verifies confirmed shutdown. No complete keyboard assist or GUI Git commit journey. |
| PORT | [StudioPortalPipelineJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioPortalPipelineJourneyTests.cs) | APipelineIsCreatedSavedAndReopenedInThePortal creates from Home as Admin, injects script, checks catalog Pipeline kind and exact normalized reopened text. It does not run, cancel or schedule the pipeline or prove ordinary-author grants. |
| API | [WorkstationEditorTests](../../../tests/ETL-SQL.Tests/WorkstationEditor/WorkstationEditorTests.cs) | API/service coverage includes completion, schema suggestions, star expansion, snippets, hover, format, workspace boundaries, revisions, Git and run restrictions. Starter-script tests assert deliberate failure, then success with expected column names and nonempty rows. They do not assert exact summary values or cleanup through Studio. |
| SAVE | [StudioPersistenceTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioPersistenceTests.cs) | Portal document identity/leases, save conflicts and per-document data/result contexts. Some cases invoke Studio methods or replace responses; scope their evidence to their assertions, not every operation named in this inventory. |
| REC | [StudioRecoveryJourneyTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioRecoveryJourneyTests.cs) | Portal Admin catalog-report draft restore, successful token refresh and changed-revision warning. Does not cover unpublished pipeline identity, failed refresh or startup timeout. |
| DREC | [StudioDesktopRecoveryTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioDesktopRecoveryTests.cs) | Keeps a host draft after a browser context closes; file stays unchanged, a fresh context restores the injected marker. Host stays running. Does not prove host-process restart, draft conflict or policy refusal UX. |
| DATA | [StudioDesktopDatasetWizardTests](../../../tests/ETL-SQL.Portal.BrowserTests/StudioDesktopDatasetWizardTests.cs) | Desktop schema discovery after a newly declared connection with delayed analysis. Does not certify the entire task-helper lifecycle. |
| DRAFT | [WorkstationDraftStoreTests](../../../tests/ETL-SQL.Tests/WorkstationEditor/WorkstationDraftStoreTests.cs) | Store-level round-trip/deletion, workspace isolation, plaintext-credential refusal, expiry and allowed-file checks; not browser recovery evidence. |
| COMPONENT | [execution scope checks](../../../scripts/checks/test-studio-execution-scope.mjs), [helper preview checks](../../../scripts/checks/test-studio-helper-preview.mjs), [editor tests](../../../scripts/checks/test-script-editor.mjs), [host adapter tests](../../../scripts/checks/test-studio-host.mjs) | Supporting source/component checks. Execution-scope and helper-preview checks read generated source; none substitutes for the named production-host journeys. |

## Source ownership and verified behavior

- [script-editor.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/script-editor.ts)
  owns the shared CodeMirror editor, completion integration, hover, search and snippets.
  [studio-editor-host.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-editor-host.ts)
  mounts it with host routes and exposes degraded textarea mode with Retry. The banner names missing
  highlighting, diagnostics and completion; formatting is disabled in this mode.
- [studio-authoring-pipeline.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-pipeline.ts)
  owns task dialogs and run-plan confirmation.
  [studio-pipeline-view.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-pipeline-view.ts)
  asks the server for a prefix plan, refuses unresolved plans, obtains consent when effects exist,
  then sends that plan's script as a selection. Scope information is not proof that predecessors ran.
- [studio.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio.ts)
  routes Run to the current buffer and Run selected to a selection or statement at cursor; an
  unresolved statement produces a warning. Both use the shared run path.
  [studio-run-session.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-run-session.ts)
  sends to the designer run endpoint and stores trace/run state by document. User Stop aborts the
  client request and reports an unconfirmed cancellation when no server confirmation arrived.
  HTTP 499 and 408 have distinct server-cancelled and timeout messages. These branches require
  runtime evidence; their presence does not prove which outcome a particular host produces.
- [WorkstationRunService](../../../src/ETL-SQL.WorkstationEditor/WorkstationRunService.cs)
  creates a new ExecutionSession for each call, uses selection text when supplied, applies a timeout
  and memory/row limits, and refuses destructive work without ConfirmDestructive. It is not evidence
  of a persistent REPL across separate Run actions. The inspected Studio run request does not send
  ConfirmDestructive; test the refusal and recovery path rather than assuming a confirmation button
  completes it. Prefix consent and server destructive authorization are distinct contracts.
- [PortalDesignerRunService](../../../src/ETL-SQL.Portal/Services/PortalDesignerRunService.cs)
  accepts a single read-only query. The inspected Studio shared run path does not supply the accepted
  saved-revision Orchestrator pipeline submission. ST04–ST10 remain required under the
  [Portal ETL decision](portal-etl-documents.md); sending mutating scripts into Portal is not a substitute.
- [studio-query-workbench.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-query-workbench.ts)
  composes engine/native helper requests with connection context. Its preview path must not silently
  execute arbitrary predecessors to obtain a variable or temporary table.
- [studio-file-commands.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-file-commands.ts),
  [studio-workspace-explorer.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-workspace-explorer.ts)
  and [studio-document-tabs.ts](../../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-document-tabs.ts)
  are the file/Git/tab integration points. Host capabilities and persistence boundaries must remain visible.

The old SSIS journey commentary describes Portal as report-only and staging as lacking a palette chip.
PORT and HELPERS now provide contrary evidence for authoring. Use live behavior and focused contracts;
those comments must not be used to declare present product limitations.

## Workflow and missing-fixture matrix

Each row is a named missing acceptance fixture with its own expected result. P is partial source-reviewed
coverage, not a passing result. Supplemental fixtures are explicitly outside existing bounded tasks
when those tasks do not cover the whole row; size feature fixes only after the fixture exposes them.

| ID | Workflow / foundation | Portal | Desktop | Required acceptance fixture and expected result | Backlog owner |
| --- | --- | --- | --- | --- | --- |
| E01 | Keyword highlighting and language completion | U | U | Type a query, distinguish keyword/string/comment highlighting, invoke suggestions with keyboard and accept one at the correct range. No unrelated text changes. API completion is supporting coverage. | Supplemental editor-assist fixture; ST23/ST24 for keyboard context. |
| E02 | Schema suggestions, star expansion and snippets | U | U | Declare one authorized connection, request table/column suggestions, expand a star and insert a snippet; cycle placeholders and compare resulting text. A denied connection exposes no schema. | Supplemental editor-assist fixture using API cases. |
| E03 | Hover, lint, formatting and repair | U | U | Hover one function, introduce a syntax error, navigate its diagnostic, repair it and format twice. Error clears, the second format is unchanged and focus/selection remain usable. | Supplemental language-service journey; ST16/ST17 learning overlap. |
| E04 | Search/replace, undo/redo, projections and tab isolation | P | U | Edit two documents with different selections and unsaved text; search/replace in one, undo/redo, switch Code/Split/Canvas and return. Text, selection and results remain owned by the correct document. | SAVE supports Portal context; supplemental editor-state fixture. |
| E05 | New/open/save/close, dirty guards and line endings | P | P | Create query and pipeline from Home, save, reopen and close a dirty tab using each offered choice. Compare independent pre-save/host/reopened text and the declared LF/CRLF behavior. | PORT/DESK; ST10 ordinary Portal pipeline, supplemental desktop lifecycle. |
| E06 | Workspace rename/move/folders/delete and path boundaries | N/A | P | Rename/move an open dirty script and folder; reject a collision and outside-root path without losing edits. Delete one selected file only after the visible guard, and keep adjacent files intact. | DESK/API; supplemental workspace integrity fixture. |
| E07 | Catalog discovery, permissions, leases and stale opens | P | N/A | As an ordinary author find/open a permitted pipeline, reject a denied one; lose/renew a lease and handle external move/delete while preserving dirty text. | ST10, ST45/ST46; lease-specific supplement. |
| E08 | External save conflicts and revision integrity | P | U | Modify the host copy after opening; attempt save and verify explicit conflict, retained buffer and unchanged external version. Resolve through a visible supported path, then compare reopened text. | SAVE/API; supplemental both-host conflict fixture. |
| E09 | Git status, unsaved diff and commit | U | P | In a configured repository inspect an unsaved diff, save and commit via Studio; inspect the commit for only intended script changes. An unconfigured host explains unavailability and still saves. | DESK diff/API commit; supplemental GUI commit and capability fixture; closes R23 from ST01. |
| E10 | Connection discovery and task helper input | U | P | Create/select a permitted MOCKDB connection, choose a table/columns, preview a task and cancel without modifying text. Exercise schema failure with a remedy and retry. | HELPERS/DATA; ST21/ST22 sample path, supplemental failure case. |
| E11 | Extract, stage and load | U | P | Build extraction and load through helpers against a disposable destination, save/reopen, execute and compare exact staged/loaded rows. Verify cleanup and no duplicate load on an accidental repeat. | HELPERS is authoring-only; ST20–ST22 practice, supplemental load correctness case. |
| E12 | Reshape/derive, filter, summarise and join | U | P | Build the four operations from fixed inputs, explain the typed expression, inspect preview and execute. Compare expected columns, row set, groups and totals; reopen helpers after one hand edit. | HELPERS; ST21/ST22 bounded exercise plus supplemental transform cases. |
| E13 | Validation failure, diagnosis, repair and cleanup | G | P | Run the deliberate ASSERT failure as a learner, inspect the message, repair through ordinary controls/code and rerun. Verify exact output and cleanup, not only a success flag or nonempty rows. | ST20 policy first; ST21/ST22; API tests are partial. |
| E14 | Task add/edit/rename/reorder/remove and code round-trip | U | P | Edit fields, rename a connected task, reorder and remove another, then hand-edit and reopen. Compare untouched ranges, references, ordering and saved text. | FLOW; supplemental both-host task lifecycle fixture. |
| E15 | Conditions, dependencies, loops, parallel and transaction containers | U | P | Use small separate success/failure/condition fixtures; nest and unnest tasks, refuse a cross-scope dependency, and execute expected branch order. Verify loop output, parallel independence and rollback after a controlled failure. | FLOW branch/lane subset; supplemental control-flow fixtures. |
| E16 | Positional scope and data-model inspection | U | P | Select tasks before/after a declaration and inside a branch; inspect available variables/temp tables and source location. Distinguish unresolved/no-data from empty scope and runtime metrics from design facts. | ETL scope subset; supplemental scope/model fixture. |
| E17 | Run all, selection, statement and run-to-task scope | U | U | Exercise no selection/unresolved statement and prefix consent refused/accepted. Capture actual server statements and compare with displayed scope; refusal runs nothing. Test the server destructive refusal and visible recovery separately. | ST30/ST31; consent-recovery supplement if not covered. |
| E18 | Fresh execution sessions and helper preview context | U | U | Stage data in one Run, then request a dependent statement separately. Explain unavailable context; an explicit approved run-to-task prefix produces correct rows. Test a named native dialect, empty result and staged variable without hidden predecessor execution. | ST34–ST37; supplemental fresh-session explanation fixture. |
| E19 | Saved pipeline submission, status/log and cancellation | G | N/A | Submit a saved revision under federated author identity, retain its run ID, follow only that run and confirm cancellation or state uncertainty. Missing Orchestrator and denied grants produce useful refusal. | ST04–ST06, ST10. |
| E20 | Results, messages, execution trace and diagnostic ownership | P | P | Run two documents with distinct outputs and one deliberate error; switch tabs during execution. Check exact rows, bounded logs, diagnostic locations and correct run identity; no stale output presented as new success. | SAVE/DESK trace subset; ST05 and ST30/ST31 plus tab-isolation supplement. |
| E21 | Stop, timeout and lost response | U | U | Exercise confirmed server cancellation, client abort with unknown outcome, server timeout and lost completion response. Keep run ID and diagnostics, do not label unknown execution as confirmed stopped, and never replay automatically. | ST32/ST33; Portal submitted-job cancellation ST06. |
| E22 | Draft restore, expiry, policy refusal and crash conflicts | P | P | Restore unsaved query/pipeline after browser close and host restart where supported; detect a newer source revision, test discard/expiry/policy refusal, and preserve recoverable text. Another workspace/principal cannot read it. | REC/DREC/DRAFT; ST12–ST15, ST52; supplemental desktop restart/conflict fixture, closes R20 desktop portion. |
| E23 | Degraded editor, session startup and authentication retry | P | U | Fail the editor module, edit two buffers and Retry; text survives and unavailable assists are explained. Separately fail startup/refresh and recover the intended identity/capabilities without losing permitted drafts. | ST38–ST41; REC successful refresh is only partial. |
| E24 | Desktop host lifecycle and project isolation | N/A | P | Reopen an existing project host, use two browser windows and two projects, exercise dirty/running Exit, idle timeout and restart. Verify correct host discovery, no cross-project state and confirmed shutdown. | DESK/API partial; supplemental lifecycle edge cases. |
| E25 | Schedule, package export/import and promotion | U | N/A | Schedule one saved Portal pipeline and inspect expected output. Round-trip its permitted portable configuration, exclude secrets/host bindings, then promote to one target and reject a collision atomically. | ST07–ST09; desktop equivalent is not required by this catalog contract. |
| E26 | Large buffers, slow analysis and accessible editing | U | U | Use the declared large-document fixture; throttle analysis and recover without losing text. Complete the keyboard/zoom/screen-reader editing sequence and record an observed transcript. | ST23–ST29, ST47–ST49, ST53; no new general editor rewrite. |

## Improvement priorities and boundaries

1. **A real failure/repair ETL exercise:** use the existing desktop helpers and starter-script service
   tests to build ST22's browser journey after ST20 defines practice restrictions. Assert exact rows
   and cleanup. The saved-script certification currently cannot establish either.
2. **Portal pipeline execution:** ST04 is the first missing link from catalog authoring to execution.
   Follow the saved revision, federated identity and sandbox contract; ordinary interactive preview
   must stay bounded and read-only. Complete status/cancel before claiming a Portal ETL lifecycle.
3. **Editor assistance as an actual user experience:** E01–E03 need typing, completion acceptance,
   hover/diagnostic navigation and repair in both production hosts. API assertions are useful inputs.
4. **Explain run context and outcome:** E17/E18/E21 must distinguish selected text, explicit prefixes,
   fresh sessions, consent, refusal and unknown cancellation. Do not promise persistent staged state
   or confirmed stopping based on UI state alone.
5. **Recovery beyond a healthy host:** E22/E23 extend the useful browser-close and successful-refresh
   coverage to host restart, draft policy, failed refresh and degraded editing.

A persistent REPL across independent runs, a full Git client, local implementations of Portal
catalog/scheduling, proprietary SSIS package import, debugger breakpoints/step execution and removal
of WorkstationEditor are not required by this bounded baseline. If existing source exposes an action,
it still needs a truthful result or explicit unavailable explanation. Batch/folder execution and
multi-file orchestration are separate scope, not implied by single-document ETL acceptance.

Keep host-specific path, grant, retention and execution boundaries. Preserve SQL as the artifact and
allow advanced hand edits; required beginner exercises must document where SQL knowledge is needed
and whether the helper explains it. Declaring a non-goal must never disguise lost text or wrong output.

## Completion record and next step

ST02's inventory deliverable is complete: each required foundation workflow has a both-host coverage
classification, source/test references, a missing acceptance fixture with expected results, and backlog
routing or an explicit supplemental scope. No tests were run and no feature/test implementation was
added. ST03 remains open.

Begin acceptance with the existing ST16/ST17 report journey and the ST20 practice-execution decision.
Then implement/verify ST22's desktop failure/repair journey and ST04's saved Portal pipeline submission
as separately scoped increments. E01–E03 and ST01's guided parameter-binding work need separately sized
implementation tasks; they are not silently included in those existing bounded journeys.
