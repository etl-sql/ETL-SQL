# ETL-SQL Development TODO List

Use this list as the execution ledger for all unfinished product and release work. All remaining
product work is active for the current planning horizon. Work top to bottom unless a dependency or
release-blocking defect changes the order. Once an item is verified, record its notable outcome in
`CHANGELOG.md` and check it completed.

Unfinished `ROADMAP.md` initiatives and release gates are represented below.

---

## 1. ETL-SQL Studio (Unified Dual-Projection Visual & Script Workbench)

Authoritative reference: [`docs/architecture/decisions/etl-sql-studio.md`](docs/architecture/decisions/etl-sql-studio.md).

> **Parallel Construction & Transition Policy:** `ReportBuilder` (`ETL-SQL.ReportBuilder`) and `WorkstationEditor` (`ETL-SQL.WorkstationEditor`) remain fully operational, tested, and untouched during Studio construction. `ETL-SQL Studio` is built as an independent, side-by-side flagship component. Once Studio is certified and stabilized across desktop and Portal, legacy surfaces will be gracefully deprecated and retired.

- [x] **Phase 1 — Modern Studio Shell, Dual Projections & Editor Usability**:
  - Build the **ETL-SQL Studio** layout featuring a top document tab bar, left Activity Rail icons (Files, Connections, Filters, Git, Settings), and view projection toggles (`[ 🎨 Canvas | 🌓 Split | ⌨️ Code ]` + `[ ▶ Run ]`).
  - Fix single/sub-line text selection execution in CodeMirror 6 (`Ctrl+Enter` / `Run Selection` must execute the exact character selection range when text is highlighted rather than expanding to the full line).
  - Implement a polished Save / Save-As modal dialog prompting for file name and directory; inspect script content for raw secrets/passwords and prompt for passphrase encryption (`ENC:`) or catalog reference (`SECRET:`/`SHARED:`).
- [x] **Phase 2 — Live Data `__ETLSNAP__` Ingestion & 60 FPS Visual Canvas**:
  - Implement `POST /api/designer/data-sample` executing a bounded `TOP 250` sample query under caller RLS and populating in-memory `window.__ETLSNAP__`.
  - Build responsive WYSIWYG card stage in `Shared/designer/` computing real-time client-side aggregations (KPI `reduce()`, Bar/Donut `group-by`, Line chronological bucketing, Table sorting) in ~1 ms at 60 FPS without remote DB latency per visual edit.
- [x] **Phase 3 — Type-Aware Filter Pane & 1-Click Slicer Promotion**:
  - Implement the dedicated Filter Pane supporting Dataset Global (`WHERE`) and Visual Local (`FILTERS`) scopes.
  - Provide type-aware controls: distinct value checkbox lists from `__ETLSNAP__` sample rows, numeric comparison sliders, and relative date presets (`Last 7/30 Days`, `This Quarter`, `YTD`).
  - Add 1-click **"Promote to Slicer"** converting static `WHERE` clauses into `@parameter` declarations and canvas Slicer visuals.
- [x] **Phase 4 — Surgical AST Synchronization & Split-View CodeMirror**:
  - Enhance `DesignerScriptPatcher` to patch only targeted `VISUAL`, `PAGE`, or `WHERE` AST clauses, preserving hand-crafted CTEs, custom transformations, comments, and whitespace.
  - Implement debounced code-to-canvas synchronization refreshing `__ETLSNAP__` sample rows when dataset SQL queries are edited in CodeMirror.
- [x] **Phase 5 — Multi-Surface Packaging (Desktop CLI & SaaS Portal Studio)**:
  - Package desktop distribution under `etlsql studio` (`ETL-SQL.WorkstationEditor` / `ETL-SQL.Studio` running over local loopback).
  - Host identical canonical assets in Portal SaaS (`/studio/index.html`) with Zero-Trust connection catalog and Gateway routing.
- [x] **Phase 6 — Agent-Driven Usability Audits & Playwright Browser Automation**:
  - Implement autonomous Playwright browser tests in `tests/ETL-SQL.Portal.BrowserTests` and `tools/ui-sandbox` verifying end-to-end user journeys (connect ➔ pick table ➔ drag cards ➔ filter ➔ split code ➔ run).
  - Automate bounding-box geometry and layout shift audits across screen resolutions (1024x768 to 4K).
- [ ] **Phase 7 — Stabilization & Legacy Retirement**:
  - Complete user acceptance and performance benchmarking.
  - Gracefully deprecate and retire legacy `ReportBuilder` dialogs and legacy `WorkstationEditor` entry points in favor of ETL-SQL Studio.

## Bugs & Triage

### Connection Catalog & Gateway Resource Discovery
- [ ] **Gateway Connection Verification Probe**: `POST /api/admin/connections/{alias}/verify` currently checks `SECRET:` references for direct connections. Extend `VerifySecretReferencesAsync` / `Verify` endpoint to perform live liveness and approval verification against `GatewaySessionRegistry` for gateway-bound shared connections.
- [ ] **Connection Wizard Resource Unbind Action**: In `connection-wizard.js`, add an explicit "Clear selection" / "Unbind" button on the selected gateway resource card so users can revert back to direct connection entry without changing the gateway cluster dropdown.
- [ ] **Connection Wizard Resource Refresh Trigger**: Add a manual refresh button to the resource picker card header in `connection-wizard.js` to re-fetch live published resources on demand if approvals or resources changed on the gateway daemon.
- [ ] **TUI slicers not working** The Slicers, date pickers, etc don't seem to be wired up yet.
## v0.19.0 Release Evidence Gates

Target Release: **v0.19.0**
Authoritative policy: [`release-checklist.md`](docs/releases/release-checklist.md) and
[`Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/enterprise-release-evidence-checklist.md)

- [ ] Run the full local pre-release gate required by the release checklist, including the selected
  SLT, Docker integration, scale, packaging, and platform lanes.
- [ ] Pass the Enterprise Release Evidence Checklist, `test-lane.ps1`, `Test-PreRelease.ps1`,
  `Test-EnterpriseHardeningCertification.ps1`, `admin restore --validate`, `ha-soak validate`, and
  `SecurityBoundaryDocTests` as applicable to the shipped v0.19.0 claims.
- [ ] Build the deployment-profile claim matrix from evidence and do not promote unfinished Shared
  SaaS or hosted-production outcomes into release claims.
- [ ] Verify third-party notices/inventory, secret scanning, SBOM, checksums, installers, release
  notes, upgrade guidance, and changelog entries for the final shipped scope.
- [ ] Reconcile `TODO.md` and `ROADMAP.md` immediately before release: remove verified completed work,
  retain unfinished increments with accurate status, and ensure release notes describe only
  evidence-backed outcomes.
