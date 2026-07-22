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

- [ ] **Canvas Keyboard Shortcuts & Ergonomics.**
      Add canvas-level keyboard listener to `createDesigner` handling `Delete` / `Backspace` to remove
      selected visual cards, `Ctrl+S` / `Cmd+S` to trigger report save, `Escape` to clear visual
      selection, and Arrow keys (`Up`/`Down`/`Left`/`Right`) to nudge visual grid positions (`gridCol`/`gridRow`).

- [ ] **Unsaved Changes Guard (`beforeunload`).**
      Track designer dirty state (`isDirty`) when canvas visual cards move, resize, delete, or when script text
      is modified. Attach a `beforeunload` listener prompting the user before navigating away or closing the tab.

- [ ] **Dynamic Column Autocomplete in Properties Panel.**
      Upgrade mapping input fields in `renderProps` from plain text inputs to use a `<datalist>` or interactive
      dropdown pre-populated with actual dataset columns (from loaded `.etlsnap` packages or `/api/designer/schema`),
      while preserving custom expression entry.

- [ ] **Canvas Layout Undo / Redo Stack.**
      Maintain a 20-step history stack of `DesignState` snapshots for visual card additions, movements, resizes,
      and deletions, supporting `Ctrl+Z` / `Ctrl+Y` undo/redo actions on the grid canvas.

- [ ] **Explicit Container Detachment UX.**
      Add an explicit "Unnest / Detach from Container" action button on card headers when a visual is nested inside
      a `CONTAINER`, simplifying container extraction alongside the existing property dropdown.

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
