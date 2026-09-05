# PAGE

Defines a report page as a responsive CSS grid layout or a physical paginated print layout. Visuals, containers, and buttons are mapped into named grid slots. The page mode (`DASHBOARD` or `PAGINATED`) is required.

## Syntax

```sql
CREATE PAGE <name> AS DASHBOARD | PAGINATED (
  [TITLE = '<string>',]
  [SUBTITLE = '<string>',]
  [TOOLTIP = '<string>',]
  [VISIBLE = ON | OFF | <expression>,]
  [REFRESH = <seconds>,]
  LAYOUT (
    STRUCTURE = '<grid-template-areas>',
    MAP ('<slot>' = <visual_or_container>, ...),
    [GAP = '<css-size>']
  ),
  [MOBILE_LAYOUT (
    STRUCTURE = '<grid-template-areas>',
    MAP ('<slot>' = <visual_or_container>, ...),
    [BREAKPOINT = <px>]
  ),]
  [OPTIONS (
    [BACKGROUND_IMAGE = '<url>',]
    [BACKGROUND_SIZE = 'cover' | 'contain' | '<size>',]
    [MAX_WIDTH = <number_in_px>,]
    [ALIGN_CONTENT = 'CENTER' | 'LEFT' | 'RIGHT',]
    [OVERFLOW = 'SCROLL' | 'CLIP',]
    [TRANSITION = 'NONE' | 'FADE' | 'SLIDE']
  ),]
  [ACTIONS (
    ON_LOAD = (<action>, ...)
  ),]
  [PRINT_LAYOUT (
    [PAGE_SIZE = 'Letter' | 'A4' | 'Legal' | 'Custom',]
    [ORIENTATION = 'PORTRAIT' | 'LANDSCAPE',]
    [CUSTOM_WIDTH = <number>,]
    [CUSTOM_HEIGHT = <number>,]
    [MARGINS = (<top>, <right>, <bottom>, <left>),]
    [UNITS = 'in' | 'cm' | 'mm' | 'pt' | 'px',]
    [OVERFLOW = 'AUTO' | 'CLIP' | 'SPLIT' | 'SCROLL']
  ),]
  [STYLE (KEY = value, ...)]
);
```

## Page Modes

- **`DASHBOARD`** — Dynamic interactive mode. Result visuals execute immediately on page load, and any control change (e.g. slicer selection) updates dependent visuals in real time.
- **`PAGINATED`** — Print-ready and parameterized execution mode. Staged prompt controls load first, and changes to parameters are held in a pending state until an `APPLY_PARAMETERS` button action or CLI `--run-page` execution occurs. When `PRINT_LAYOUT` is configured, content is compiled into physical sheet pages with automatic table splitting.

## Layout Options

- **`STRUCTURE = '<grid>'`** — CSS `grid-template-areas` layout definition. Each quoted section represents a row, with slots separated by spaces.
- **`MAP ('<slot>' = <visual>, ...)`** — Maps slot letters to visual or container definitions.
- **`GAP = '<size>'`** — Spacing between grid cells (e.g. `'16px'`).

## Mobile Layout Options (`MOBILE_LAYOUT`)

- **`STRUCTURE = '<grid>'`** — Alternative responsive grid template used on mobile viewports.
- **`MAP ('<slot>' = <visual>, ...)`** — Slot mapping for mobile layouts.
- **`BREAKPOINT = <px>`** — Viewport width threshold in pixels below which the mobile layout activates (default `768`).

## Page Options and Styling

- **`BACKGROUND_IMAGE = '<url>'`** — URL or asset path for a page background image.
- **`BACKGROUND_SIZE = 'cover' | 'contain' | '<size>'`** — Sizing rule for the background image (default `'cover'`).
- **`MAX_WIDTH = <px>`** — Maximum container width in pixels for page content.
- **`ALIGN_CONTENT = 'CENTER' | 'LEFT' | 'RIGHT'`** — Content alignment within the viewport when `MAX_WIDTH` is applied (default `'CENTER'`).
- **`OVERFLOW = 'SCROLL' | 'CLIP'`** — Viewport overflow behavior for long dashboard content.
- **`TRANSITION = 'NONE' | 'FADE' | 'SLIDE'`** — Visual transition effect when navigating to this page.
- **`VISIBLE = ON | OFF | <expression>`** — Controls page visibility. Accepts boolean literals or dynamic parameter expressions (e.g. `@ShowAdminTab`).
- **`ACTIONS (ON_LOAD = (...))`** — Page-level actions triggered immediately upon page mounting (e.g. `SET_PARAMETER(@Loaded, 1)` or `REFRESH_REPORT`).

## Print Layout Options (`PRINT_LAYOUT`)

- **`PAGE_SIZE = '<size>'`** — Standard sheet dimensions: `'Letter'` (default), `'A4'`, `'Legal'`, `'Executive'`, `'Tabloid'`, `'A3'`, `'A5'`, or `'Custom'`.
- **`ORIENTATION = 'PORTRAIT' | 'LANDSCAPE'`** — Sheet orientation (default `'PORTRAIT'`).
- **`CUSTOM_WIDTH = <n>`** — Width in `UNITS` when `PAGE_SIZE = 'Custom'`.
- **`CUSTOM_HEIGHT = <n>`** — Height in `UNITS` when `PAGE_SIZE = 'Custom'`.
- **`MARGINS = (top, right, bottom, left)`** — Four-value margin tuple (default `(1.0, 1.0, 1.0, 1.0)` in inches).
- **`UNITS = 'in' | 'cm' | 'mm' | 'pt' | 'px'`** — Unit of measure (default `'in'`).
- **`OVERFLOW = 'AUTO' | 'CLIP' | 'SPLIT' | 'SCROLL'`** — Multi-page overflow strategy.

## Style Options

- **`PADDING`** — Page padding (e.g. `'16px 24px'`).
- **`BACKGROUND`** — Page background color or CSS value.

## Examples

```sql
-- Responsive Dashboard with Mobile Layout & Page Options
CREATE PAGE ExecutiveSummary AS DASHBOARD (
  STRUCTURE = 'K K / L R',
  MAP (
    'K' = KpiRow,
    'L' = RevenueTrend,
    'R' = RegionalBreakdown
  ),
  MOBILE_LAYOUT (
    STRUCTURE = 'K / L / R',
    MAP ('K' = KpiRow, 'L' = RevenueTrend, 'R' = RegionalBreakdown),
    BREAKPOINT = 768
  ),
  OPTIONS (
    MAX_WIDTH = 1440,
    ALIGN_CONTENT = 'CENTER',
    TRANSITION = 'FADE'
  ),
  ACTIONS (
    ON_LOAD = (SET_PARAMETER(@VisitedExecutive, 1))
  ),
  VISIBLE = @ShowExecutiveSummary,
  STYLE (PADDING = '24px')
);

-- Paginated Print Report Page with Physical Page Layout
CREATE PAGE MonthlyStatement AS PAGINATED (
  LAYOUT (
    STRUCTURE = 'H / T / F',
    MAP (
      'H' = StatementHeader,
      'T' = TransactionsTable,
      'F' = StatementFooter
    )
  ),
  PRINT_LAYOUT (
    PAGE_SIZE = 'Letter',
    ORIENTATION = 'PORTRAIT',
    MARGINS = (0.75, 0.75, 0.75, 0.75),
    UNITS = 'in'
  )
);
```

## Lifecycle

```sql
CREATE OR REPLACE PAGE Overview AS DASHBOARD (...);   -- redefine, including the layout
ALTER PAGE Overview (TITLE = 'Q3 Overview', VISIBLE = OFF, REFRESH = 300);
DROP PAGE IF EXISTS Overview;
```

References:
- [PRINT_LAYOUT Reference](print-layout.md)
- [VISUAL Reference](visual.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
