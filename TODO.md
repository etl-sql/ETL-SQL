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

- [ ] **Tag-driven governance policy gates.**
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
