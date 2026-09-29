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
- **Actions & Interactions:**
  - **Cross-filtering** links the visual (`INTERACTIONS (ON_SELECT = HIGHLIGHT | FILTER)`). A linked visual sends a selection when clicked, and responds to selections made elsewhere. Tables and slicers send by default; charts only once linked.
  - **A selection here reaches** ticks the visuals a selection may reach (`EMIT_FILTER (TARGETS = (...))`). Nothing ticked means every linked visual.
  - A selection arrives as a parameter named after the clicked column, for example `@Region`. A linked visual only narrows if its own query reads that parameter. When it doesn't, the panel says so, and **Filter this visual on @Region** writes `WHERE @Region = 'All' OR Region = @Region` into its source and declares `@Region` with `'All'` as its resting value.
  - **When a data point is clicked** writes `ACTIONS (ON_CLICK = ...)`: show details in another visual (`DRILL_DOWN`), drill into the next level (`DRILL_IN`), go to a page (`NAVIGATE_PAGE`), set a parameter (`SET_PARAMETER`), or a custom action. A drill-down sets `@<key>` for each key column, so the panel offers to make the target read it. A linked visual's click selects instead of running its action; a drill-down stays on its right-click menu.
  - `ON_CHANGE` (e.g. `SET_PARAMETER(@var, value)`) for input controls.
- **Tooltip** writes the visual's `TOOLTIP` clause:
  - **Text:** `TOOLTIP = 'text'`.
  - **Fields from the hovered row:** `TOOLTIP ('heading', FIELDS (Region, Revenue FORMAT 'C0'))`. It starts from the columns the visual plots, and each field takes a DATA_LABELS-style format.
  - **Other visuals, in a popover:** `TOOLTIP (VISUALS (Detail))`, or **a container**: `TOOLTIP = Box`. A popover's visuals receive the hovered X (or LABEL, NAME, REGION, Y) value as `@hover_value`, so the build needs one of those mapped. **Show Detail for the hovered Region** writes `WHERE Region = @hover_value` into a popover visual's own source.
  - A tooltip the panel cannot write back exactly is shown read-only and left as written.
- **Row detail** (tables only) writes `ROW_DETAIL (TARGET = ..., BINDINGS (@column = Column), LIMIT = n)`. Each row gets an expand button that shows the target's rows where the named column equals this row's value. The target usually sets `VISIBLE = OFF` so it appears only under rows.
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

## Bi-Directional Split Script Authoring & Round-Trip Fidelity

Click **Split Script** (`Ctrl+Shift+S`) to open the CodeMirror editor alongside the visual canvas:

- **Surgical Script Patching & Trivia Preservation:**
  - When moving, resizing, styling, or deleting visual cards on the grid canvas, the engine uses **surgical AST span patching** (`DesignerScriptPatcher`).
  - Preceding data prep statements (`CREATE CONNECTION`, `SELECT ... INTO #temp`, `DECLARE @var`, `RUN SCRIPT`, CTEs), interleaved comments (`-- comment` and `/* banner */`), and custom code formatting are preserved **100% character-for-character**.
  - Only the exact statement spans for the modified visual, page layout, or dataset query are replaced.
- **Grid → Script Sync:**
  - Moving, resizing, adding, or deleting cards automatically updates the `.rptsql` script in real time without causing CodeMirror text cursor jumps or losing user scroll positions.
- **Script → Grid Sync & Fault-Tolerant Canvas State:**
  - Modifying raw ETL-SQL script clauses (`CREATE VISUAL`, `STRUCTURE = 'A B / C D'`, `STYLE (...)`) automatically parses and synchronizes to the visual grid.
  - If a script contains a transient syntax error during live typing (e.g. unclosed parenthesis or incomplete keyword), the designer displays a non-intrusive warning badge (`⚠ Script syntax warning`) with full line diagnostics in the topbar, while **retaining all active canvas visual objects and interactions** without wiping state.
  - When the syntax error is resolved on subsequent keystrokes, the warning badge clears and the canvas updates smoothly.
- **12-Column Bounds Clamping & Outlier Normalization:**
  - Card dragging and resizing operations are strictly bounded to the 12-column grid (`gridCol` in `1..12`, `gridColSpan` in `1..13-gridCol`).
  - Negative, zero, or out-of-range heights/widths are automatically normalized to safe minimums (`gridRowSpan >= 1`), preventing corrupted script layout strings.
- **Cursor Focus Tracking:**
  - Clicking a visual card on the canvas scrolls the CodeMirror editor directly to its `CREATE VISUAL` declaration.
  - Moving your text cursor inside a `CREATE VISUAL` block in CodeMirror selects that card on the canvas.

---

## Related References

- [Report-SQL Scripting Guide](../feature-guides/report-sql.md) — Detailed statement syntax for `.rptsql` files
- [Portal User Guide](portal-user.md) — Running, filtering, and subscribing to published reports
- [Syntax Index](../../syntax-index.md) — Quick reference for all ETL-SQL keywords and visual types
