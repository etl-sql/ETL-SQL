# Visual Report Builder & Dashboard Designer Guide

The **Visual Report Builder & Dashboard Designer** is the integrated WYSIWYG authoring surface for ETL-SQL. It allows developers, analysts, and stewards to visually build, edit, lay out, and configure interactive dashboards across all platform surfaces (**Portal**, **Workstation Editor**, **VS Code Extension**, and **Report Player**) while automatically maintaining clean, diffable, source-control-friendly `.rptsql` scripts behind the scenes.

---

> **Applies to:** authoring on any profile. The designer runs in VS Code without a Portal, and inside Portal Studio where one is deployed.

## Overview & Core Concept

ETL-SQL combines **script-first pipeline reproducibility** with **WYSIWYG visual design**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       Visual Report Builder (4-Zone Surface)                │
├───────────────────┬───────────────────────────────────┬─────────────────────┤
│ Left Sidebar      │ 12-Column Grid Canvas             │ Properties Panel    │
│ - Visual Palette  │ - Multi-card arrangement          │ - Mappings & Badges │
│ - Datasets & Pills│ - Alignment & Snap guides         │ - Container Tabs    │
│ - Component Tree  │ - Fold, Duplicate, Detach UX      │ - Actions/Events    │
└───────────────────┴───────────────────────────────────┴─────────────────────┘
                                      ▲
                                      │ Bi-directional Sync
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      Report-SQL Script Artifact (.rptsql)                  │
│  CREATE DATASET &sales AS (...)                                             │
│  CREATE VISUAL RevenueChart AS BAR MAPPINGS (...)                           │
│  CREATE CONTAINER MainTabs AS TABS STRUCTURE = 'A / B'                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Four-Zone Interface Layout

### Top Bar & Navigation
The top bar provides global controls for report governance, theme testing, split-screen script editing, preview execution, and source control:

- **Report Title Input:** Edit the report title directly.
- **Page Tabs:** Switch between report pages or click `+ Page` to create multi-page dashboards.
- **Tidy Layout:** Automatically re-orders and packs visual cards to remove empty vertical grid gaps.
- **Theme Selector:** Test canvas aesthetics across 5 themes (`Light`, `Dark`, `Midnight`, `Dracula`, `Nord`).
- **Split Script View (`Ctrl+Shift+S`):** Toggles a side-by-side CodeMirror script editor and visual grid canvas.
- **Preview Toggle:** Compiles `.rptsql` into an interactive live report preview using sample/live datasets.
- **Save & Commit:** Saves the report manifest and surfaces a `Commit` action for Git repository check-ins.

### Left Sidebar
- **Visual Palette:** Drag or click to append visual cards organized by category:
  - *Charts:* `BAR`, `HBAR`, `LINE`, `SCATTER`, `PIE`, `DONUT`, `COMBO`, `BOXPLOT`, `TREEMAP`, `HEATMAP`, `FUNNEL`, `GAUGE`, `WATERFALL`, `BUBBLE`, `RADAR`, `CANDLESTICK`, `MAP`, `SANKEY`, `SUNBURST`, `NETWORK`, `TRELLIS`, `MATRIX`, `GANTT`, `TABLE`, `CARD`.
  - *Filter Controls:* `SLICER`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`, `TEXTBOX`, `NUMBERBOX`, `CHECKBOX`.
  - *Containers & Structural:* `CONTAINER`, `TEXT`, `IMAGE`, `BUTTON`.
- **Datasets & Column Explorer:** Lists attached dataset queries (`#name`). Click `▸` to expand dataset columns as draggable pills (`📄 colName`).
- **On This Page (Component Tree):** Displays a hierarchical tree view of root cards and nested container children.

### 12-Column Grid Canvas
The canvas renders a 12-column CSS grid where cards can be moved, resized, grouped, and aligned:
- **Card Badges:** Displays visual type (`BAR`, `SANKEY`), container type (`📁 BOX`), and security flags (`🔒 RLS`, `⚡ Sampled`).
- **Card Controls:**
  - `▼` / `►` *(Fold / Expand Container):* Minimizes container height to 1 grid row for dense canvas editing.
  - `📋` *(Duplicate Visual):* Clones the selected card with auto-offset row/col positioning.
  - `↗` *(Detach from Container):* Un-nests a child card back to root canvas level.
  - `✕` *(Remove Visual):* Deletes the visual card.
- **Alignment Toolbar:** Appears when 2 or more cards are multi-selected (Left, Top, Equal Width, Equal Height).

### Properties Panel
Configures selected visual details:
- **Properties:** Name, type, container group, title, dataset binding, width, and height.
- **Mappings & Role Validation:** Column assignment text fields with `<datalist>` auto-suggestions and mandatory role validation badges (`* Required` vs `✓ Required` vs `Optional`).
- **Container Section / Tab Binding:** Input `CONTAINER_SECTION` (e.g. `Tab 1`, `Section A`) when nested inside `TABS` or `ACCORDION` containers.
- **Actions & Interactions:** `ON_CHANGE` (e.g. `SET_PARAMETER(@var, value)`), `ON_CLICK` (e.g. `DRILL_DOWN(...)`), and `ON_SELECT` (e.g. `HIGHLIGHT`).
- **Grid Position:** Fine-tune numeric `Col`, `Row`, `Width` (`W`), and `Height` (`H`).

---

## Ergonomics & Keyboard Shortcuts

The designer includes complete keyboard navigation and clipboard operations:

| Shortcut | Action | Description |
| :--- | :--- | :--- |
| `Ctrl+S` / `Cmd+S` | **Save Report** | Saves report manifest and updates versioning |
| `Ctrl+Z` / `Cmd+Z` | **Undo Layout Action** | Reverts last grid movement, resize, deletion, or addition (20-step history stack) |
| `Ctrl+Y` / `Cmd+Y` | **Redo Layout Action** | Re-applies undone canvas layout state |
| `Ctrl+C` / `Cmd+C` | **Copy Visuals** | Copies selected card(s) to the designer clipboard |
| `Ctrl+V` / `Cmd+V` | **Paste Visuals** | Pastes copied card(s) with offset grid coordinates |
| `Delete` / `Backspace` | **Remove Visuals** | Deletes currently selected visual card(s) |
| `Escape` | **Deselect All** | Clears canvas visual card selection |
| `Arrow Keys` | **Nudge Position** | Moves selected card(s) by 1 grid column or row in the specified direction |

> [!NOTE]
> **Unsaved Changes Guard (`beforeunload`):** If you attempt to close the tab or navigate away while canvas layout edits are unsaved (`isDirty`), the browser will prompt for confirmation.

---

## Drag-and-Drop Column Mapping

Connecting dataset fields to visual roles is fast and interactive:

1. Expand any dataset under **Datasets** in the left sidebar to reveal its column list (`📄 colName`).
2. Click and drag a column pill over to any input field in the **Mappings** section of the Properties Panel.
3. Target mapping fields highlight with a blue outline (`drag-over`).
4. Drop the pill to automatically populate the column name.

---

## Container & Structural Layout Patterns

Containers group visuals into organized dashboards (`BOX`, `SCROLL`, `DRAWER`, `SIDEBAR`, `TABS`, `ACCORDION`, `MODAL`, `POPOVER`):

### Nesting Visuals into Containers
- **Drag-and-Drop:** Drag a visual card over a container card on the grid canvas. The container highlights with a blue drop-zone indicator. Release to group.
- **Properties Dropdown:** Select the container in the **Container Group** property dropdown.

### Tab & Accordion Section Assignment
When a card is nested inside a `TABS` or `ACCORDION` container, a **Tab / Section** property input appears in the Properties Panel. Enter the tab title (e.g. `Summary`, `Regional Breakdown`) to assign the child visual to that specific tab page.

### Un-nesting & Canvas Space Optimization
- **Detach (`↗`):** Click the `↗` button on any nested card header to extract it from its parent container.
- **Container Fold (`▼` / `►`):** Click `▼` on a container header to collapse its height while working on lower sections of a large dashboard.

---

## Bi-Directional Split Script Authoring

Click **Split Script** (`Ctrl+Shift+S`) to open the CodeMirror editor alongside the visual canvas:

- **Grid → Script Sync:** Moving, resizing, adding, or deleting cards automatically updates the generated `.rptsql` script in real time.
- **Script → Grid Sync:** Modifying raw ETL-SQL script clauses (`CREATE VISUAL`, `STRUCTURE = 'A B / C D'`) automatically parses and updates the visual grid upon applying script changes.
- **Cursor Focus Tracking:**
  - Clicking a visual card on the canvas scrolls the CodeMirror editor directly to its `CREATE VISUAL` declaration.
  - Moving your text cursor inside a `CREATE VISUAL` block in CodeMirror selects that card on the canvas.

---

## Related References

- [Report-SQL Scripting Guide](report-sql.md) — Detailed statement syntax for `.rptsql` files
- [Portal User Guide](portal-user.md) — Running, filtering, and subscribing to published reports
- [Syntax Index](../syntax-index.md) — Quick reference for all ETL-SQL keywords and visual types
