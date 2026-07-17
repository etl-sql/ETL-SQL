# Report Runtime Asset Standards

This document establishes the official development rules and synchronization protocols for shared frontend browser assets (JavaScript, CSS, themes, and UI modules) used across **ETL-SQL** visual hosts.

---

## 1. Single Source of Truth

To prevent code drift and duplication, all browser-based report player and catalog components have exactly one canonical home:

```text
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

- **Rule**: Any change to shared JavaScript logic (e.g. `designer.js`, `chart-rendering.js`), visual styles (`theme-dark.css`), or third-party web libraries must be made **exclusively** inside this folder.
- **Strictly Prohibited**: Never edit files directly inside the generated target directories of host applications. Any direct edits in host directories will be flagged as drift and overwritten by the asset synchronizer.

---

## 2. Generated Host Copies

The canonical shared assets are compiled and synchronized into these specific host directories:

- **Report Player**: `src/ETL-SQL.ReportPlayer/wwwroot/`
- **Portal**: `src/ETL-SQL.ReportPortal/wwwroot/js/` and `src/ETL-SQL.ReportPortal/wwwroot/css/`
- **VS Code Extension**: `src/etl-sql-vscode/media/`

---

## 3. The Synchronization Workflow

After modifying files inside the canonical `Shared` directory, you must run the asset synchronizer to update the host applications:

1. **Synchronize Assets**:
   Run the sync script from the repository root:
   ```powershell
   node .\scripts\sync-assets.js
   ```
2. **Verify Sync State**:
   Run the sync script in verification mode to ensure no drift remains:
   ```powershell
   node .\scripts\sync-assets.js -Check
   ```
   *Note: CI/CD build pipelines execute the `-Check` step to block merges with unsynced assets.*

---

## 4. UI Sandbox Prototyping

Before committing a user interface or charting change, you should prototype and verify the layout inside the dev-only **UI Sandbox**:

- **Location**: `tools/ui-sandbox/`
- **Command**: Run `pwsh -File tools\ui-sandbox\serve.ps1` to launch the local sandbox development server.
- **Workflow**:
  - The UI Sandbox imports canonical JavaScript/CSS files directly and bypasses compilation cache layers.
  - Develop your dashboard features in the sandbox using isolated mock datasets (`mockApi.js`) and story scenarios (`tools/ui-sandbox/stories/`).
  - This avoids the overhead of launching docker containers, databases, or web servers just to verify visual components.

---

## References

- [Presentation Standards](Presentation_Standards.md)
- [Report SQL Strategy](../roadmaps/Report_SQL_Strategy.md)
