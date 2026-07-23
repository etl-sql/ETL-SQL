# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.17.0 Release

Release focus: promote the actionable roadmap work into the sprint, finish the workstation editor,
improve authoring surfaces, and close the maintainability work that makes future connector and Portal
changes safer.

### Visual Reporting and Dashboard Designer Enhancements

- [x] **Design-Time Script DAG Preview for Authoring.**
      Add a read-only "Flow" / "DAG" pane to the script editor surfaces so authors can see the
      expected pipeline shape before execution. The script remains the source of truth; the diagram
      is derived from parsed `.etlsql` / `.rptsql` text and node clicks jump back to source lines.
      Reuse existing pieces first: `ETL-SQL.Analysis/Lineage/ScriptDagBuilder.cs` for the graph,
      `ScriptDagProjectionService` / `DagDto` for Portal-shaped DTOs, the shared `renderDag`
      browser renderer, the Workstation `/api/analyze` metadata pass, and the VS Code Visual Flow
      panel plumbing. Extend only where needed to classify ETL-specific nodes more clearly:
      connections, file reads/writes, `#temp` staging, datasets, report visuals/pages, SFTP/FTP/API
      sends, `RUN SCRIPT`, conditionals, loops, and destructive or outbound actions. Show uncertainty
      explicitly for unresolved dynamic SQL, variable paths, or parse errors instead of pretending
      the graph is complete.
      Implementation shape: add a shared `IScriptDagProjection` service in Analysis/Core-facing code,
      expose `/api/script/dag` in Workstation and Portal designer endpoints, wire a Flow tab into
      `createScriptEditorWorkbench`, and route VS Code webview requests through the existing LSP
      custom-request pattern. Validation should include unit tests for `ScriptDagBuilder`
      classifications, Portal/Workstation endpoint tests, and one Node renderer smoke test.

- [x] **Canvas Keyboard Shortcuts & Ergonomics.**
      Add canvas-level keyboard listener to `createDesigner` handling `Delete` / `Backspace` to remove
      selected visual cards, `Ctrl+S` / `Cmd+S` to trigger report save, `Escape` to clear visual
      selection, and Arrow keys (`Up`/`Down`/`Left`/`Right`) to nudge visual grid positions (`gridCol`/`gridRow`).

- [x] **Unsaved Changes Guard (`beforeunload`).**
      Track designer dirty state (`isDirty`) when canvas visual cards move, resize, delete, or when script text
      is modified. Attach a `beforeunload` listener prompting the user before navigating away or closing the tab.

- [x] **Dynamic Column Autocomplete in Properties Panel.**
      Upgrade mapping input fields in `renderProps` from plain text inputs to use a `<datalist>` or interactive
      dropdown pre-populated with actual dataset columns (from loaded `.etlsnap` packages or `/api/designer/schema`),
      while preserving custom expression entry.

- [x] **Canvas Layout Undo / Redo Stack.**
      Maintain a 20-step history stack of `DesignState` snapshots for visual card additions, movements, resizes,
      and deletions, supporting `Ctrl+Z` / `Ctrl+Y` undo/redo actions on the grid canvas.

- [x] **Explicit Container Detachment UX.**
      Add an explicit "Unnest / Detach from Container" action button on card headers when a visual is nested inside
      a `CONTAINER`, simplifying container extraction alongside the existing property dropdown.

- [x] **Visual Card Duplication & Clipboard Ergonomics (`Ctrl+C` / `Ctrl+V` / Duplicate Button).**
      Add a `Duplicate` (`📋`) button on visual card headers and register global `Ctrl+C` / `Ctrl+V` canvas listeners to clone selected visuals into the grid layout with auto-offset grid coordinates.

- [x] **Multi-Select Keyboard Nudging for All Selected Cards.**
      Update Arrow key nudging (`Up`/`Down`/`Left`/`Right`) to move all cards in `selVisualIds` simultaneously by calculating and applying column/row offsets across the selection set.

- [x] **Contextual Role Mapping Hints & Validation.**
      Enhance `renderProps` mapping fields to visually highlight required versus optional roles per visual type (e.g. `Source`/`Target` for `SANKEY`, `Category`/`Value` for `DONUT`, `Value` for `GAUGE`) and surface subtle validation indicators when required roles are missing.

- [x] **Container Canvas Fold / Collapse Toggle (`▼` / `►`).**
      Add a fold/collapse button on container card headers in canvas mode to temporarily minimize container card bodies, saving vertical canvas grid space when editing large multi-container dashboards.

- [x] **Tab / Section Assignment for Tabbed Containers.**
      Add a `Tab / Section` assignment dropdown in `renderProps` when a visual's container is a `TABS` or `ACCORDION` layout, allowing explicit binding of child visuals to tab indices (`Tab 1`, `Tab 2`, etc.).

- [x] **Expandable Dataset Tree with Drag-and-Drop Mapping.**
      Expand dataset rows under `#dsgn-ds-list` to display dataset columns, allowing drag-and-drop of column pills directly into property panel mapping target slots.

### Portal Business Consumer UX & Discovery

- [x] **Fuzzy & Synonym Catalog Search (`SHOW CATALOG SEARCH` Enhancement).**
      Upgrade portal catalog search to use fuzzy matching (reusing core fuzzy/Levenshtein matching utilities) across report titles, descriptions (`SET REPORT DESCRIPTION`), tags (`#sales`, `#inventory`), and metric/column names, ensuring non-exact searches (e.g. "Q3 Sales") match technical titles like `RPT_2026_SALES_Q3_FINAL`.
      Acceptance notes: rank exact title matches first, then fuzzy title matches, then tags/descriptions,
      then metric/column/folder matches. Return a match reason for each result so the UI can explain
      "matched title", "matched #inventory tag", or "matched revenue metric". Search must be
      permission-aware: by default return only reports the caller can open; if restricted-report
      discovery is allowed by policy, return only minimal metadata needed to request access and never
      leak report contents, dataset values, secrets, or unpublished metadata. Cover typo, synonym,
      tag, metric/column, restricted-visibility, and ranking-order tests.

- [x] **Self-Service "Request Access" Workflow for Restricted Reports.**
      Replace cold `403 Forbidden` / access denied error screens with an interactive "Request Access" card that identifies the report owner/group and enables 1-click access request submission, logging a pending access request and notifying the report owner via outbox/email.
      Acceptance notes: define whether policy allows revealing the restricted report's existence; if
      yes, show only safe metadata such as title, owner/team, and optional description. If no, render
      a generic request card without confirming existence. Model request states (`Pending`,
      `Approved`, `Denied`, `Cancelled`) and make duplicate submissions return the existing pending
      request instead of creating notification spam. Start with report-scoped requests unless folder,
      dataset, or global grants are already supported. Emit audit events and durable notification
      outbox/email entries for request creation and disposition.

- [x] **Business Consumer Home Dashboard (Favorites, Recent, & Featured).**
      Add a consumer-oriented landing page mode to the Portal homepage highlighting "My Favorites" (`SHOW FAVORITES`), "Recently Viewed" (`SHOW RECENT REPORTS`), and "Popular in My Department" visual cards as the default view for non-admin business users, bypassing technical folder structures.
      Acceptance notes: define the "business user" rule concretely from role claims, permissions, or
      absence of admin roles. Recently viewed reports should be per-user and update when a report is
      opened. Favorites should reuse the existing favorites model/commands. "Popular in My Department"
      needs a department source from OIDC/group claims or profile metadata, with a global-popular
      fallback when department is unknown. Keep technical catalog/admin navigation reachable, but not
      the default first screen for business users. All cards must respect report permissions.

- [x] **Report Ownership & Data Freshness Badges.**
      Render a standardized metadata header on published reports displaying the owning team/contact, last refresh timestamp, data freshness indicator, and interactive tag badges.
      Acceptance notes: define owner/contact/team precedence from report metadata, tags, folder
      ownership, and catalog records. Distinguish `last refreshed`, `expected refresh interval`, and
      freshness state (`fresh`, `stale`, `unknown`). Missing metadata should render explicitly as
      "Owner unknown" or "Freshness unknown" instead of hiding the field. Tags should be clickable
      into catalog search. Add tests for complete metadata, missing metadata, stale calculations, and
      permission-safe rendering.

- [ ] **Report Owner Access Approval Queue UI & 1-Click Grant.**
      Add an access request inbox view for report owners and admins (`GET /api/reports/access-requests/pending`) allowing owners to view pending user access requests and approve (`POST /api/reports/access-requests/{id}/approve`) or deny them with 1-click, automatically updating folder/report ACLs upon approval.
      Clarification before implementation: access requests are currently report-scoped
      (`ReportAccessRequest`), but effective Portal permissions are folder/group-scoped through
      `FolderAcl`. Do not silently approve by granting folder access unless that broader exposure is
      intentional. Decision: add report-level ACL support and have approval grant only the requested
      report. Use the same `FolderPermission` levels (`Read`, `Execute`, `Manage`) for consistency,
      with business-user access approvals defaulting to `Read` unless an owner/admin explicitly chooses
      a higher permission. Effective report permission should combine admin, folder owner/folder ACL,
      report owner/creator, and report-level ACL grants. The approval queue should show enough context
      for owners/admins to understand the grant scope, update request states (`Approved`/`Denied`),
      emit audit events, and avoid duplicate notification spam.

- [ ] **Interactive Tag Badge Filtering.**
      Make report header tag badges (`🏷️ #sales`) interactive in `report-runtime.js`, so clicking a tag badge automatically navigates to a catalog search filtered by that tag.
      Clarification before implementation: the runtime already renders tag badges as links to
      `/?search=<tag>`. Confirm this is the correct Portal catalog route/query contract; if not, wire
      the badges to the actual catalog search route (for example `/catalog?search=` or `/catalog?q=`)
      and add a small browser/runtime test that clicking a tag lands on filtered catalog results.

- [ ] **"Request Data Refresh" Button on Stale Reports.**
      Add an interactive "Request Data Refresh" button on report headers when the freshness indicator displays `Stale`, allowing business consumers to trigger an on-demand snapshot build or notify the report owner via audit outbox.
      Clarification before implementation: existing refresh execution requires `Execute` permission.
      For users with `Execute`, the button may call the existing refresh endpoint. For read-only
      business consumers, do not bypass permissions; create a durable/audited refresh request or owner
      notification instead. Define the response states clearly (`Requested`, `AlreadyQueued`,
      `Started`, `Denied`) and make duplicate requests coalesce rather than creating notification spam.

- [ ] **1-Click "Set as My Default View" for Saved Slicer States.**
      Add a 📌 "Set as My Default View" button next to report parameters/slicers in `report-runtime.js` and Portal header views, allowing business users to save their parameter selections via `/api/reports/{id}/saved-views` so reports open in their preferred parameter state automatically.
      Clarification before implementation: saved views and `IsDefault` already exist. Focus this work
      on capturing the current runtime parameter/slicer state, creating or updating a user default via
      `/api/reports/{id}/saved-views`, and applying that default when the report opens. Add tests that
      only one default saved view exists per user/report and that updating the default does not expose
      another user's saved view.

### Comprehensive Engine & Portal Performance Optimizations

- [x] **0-Allocation `ComputeLevenshtein` in Catalog Search.**
      Replace 2D array allocations (`new int[n+1, m+1]`) in `CatalogController.cs` with stack-allocated or pooled 1D rolling buffers (`Span<int>`), eliminating thousands of heap allocations per search request.

- [x] **Compiled Regex Caching in `ExpressionEvaluator.cs`.**
      Compile `${@var}` interpolation regexes as `static readonly Regex` with `RegexOptions.Compiled`, accelerating inline variable substitution in tight expression loops.

- [x] **Portal Request-Scoped Group ID Caching in `FolderPermissionService.cs`.**
      Cache user group IDs (`_cachedUserGroupIds`) on the scoped `FolderPermissionService` instance to eliminate duplicate DB queries to `UserGroups` during multi-folder and multi-report permission checks.

- [x] **High-Performance DateTime & SIMD Binary Equality in `EvaluationUtils.cs`.**
      Optimize `IsSoftEqual` by replacing 6-property calendar conversions (`dta.Year == dtb.Year...`) with direct `DateTime` comparisons, and replace manual byte loops with SIMD-accelerated `MemoryExtensions.SequenceEqual`.
      Clarification before implementation: the current DateTime equality semantics ignore fractional
      seconds/ticks by comparing only year/month/day/hour/minute/second. A raw `DateTime == DateTime`
      comparison would be a behavior change. Preserve the existing second-level semantics unless the
      tests and docs are intentionally updated. Byte-array comparison can safely use
      `ReadOnlySpan<byte>.SequenceEqual` / `MemoryExtensions.SequenceEqual`.

### Data Stewardship: Protected Data Audit Workflow

- [x] **Script-first protected-data audit command.**
      Add `SHOW PROTECTED DATA [AT <portal_or_orchestrator>] [LIMIT n] [INTO #temp]` so new
      stewards can find PII, PHI, PCI, sensitive, confidential, and restricted lineage without
      memorizing multiple tag-specific `SHOW LINEAGE HISTORY FOR TAG ...` queries.

- [x] **Packaged protected-data audit report.**
      Add a runnable `.rptsql` sample/report that inventories protected lineage, missing metadata,
      stale protected assets, affected reports/datasets/subscriptions, and recent steward-impact
      audit events.
      Implemented in `samples/08_Reporting/protected_data_audit.rptsql`; live Portal audit rows can
      be loaded with the included `SHOW PORTAL AUDIT ACTION 'STEWARD_LINEAGE_IMPACT'` block when a
      Portal admin connection is available.

- [x] **Portal steward audit page.**
      Add a steward-focused Portal page or Lineage mode that combines protected inventory, missing
      metadata, stale protected assets, impact, and audit/outbox events into one workflow.

- [x] **Protected-data classifier suggestions.**
      Add non-authoritative suggestions from column names, known patterns, source catalog metadata,
      and optional sampled values. Suggestions should create reviewable findings, not silently set
      `@pii`/`@classification`.

- [x] **Tag-driven governance policy gates.**
      Add lint/publish/execution policy checks for sensitive exports, restricted published datasets,
      missing owner/steward/contact/classification/quality, and promotion to `@quality=gold`.

### Release Verification

- [ ] Run the fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
- [ ] Run the full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Run enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
- [ ] Run scale certification for advertised scale claims:
      `.\scripts\Test-ScaleCertification.ps1 -Tier Standard`.
- [ ] Run the recovery drill and retain the report: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] Run HA failure certification and retain the transcripts: `etl-sql admin ha-soak fault-run` then `etl-sql admin ha-soak validate`.
- [ ] Confirm the documentation boundary guards still pass:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests`.
- [ ] Collect the evidence required by [Enterprise_Release_Evidence_Checklist.md](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md)
      — that document is the authoritative list; the entries above are the commands, not a substitute for it.
- [ ] Confirm `CHANGELOG.md`, release notes, sample inventory, and docs reflect v0.17.0 behavior.
