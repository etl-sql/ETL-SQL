# CONTAINER

Groups visuals within a page using its own nested layout grid. Useful for grouping related charts in a card region, a scrollable sub-panel, or a collapsible filter drawer.

## Syntax

```sql
CREATE CONTAINER <name> AS BOX | SCROLL | DRAWER | SIDEBAR | TABS | ACCORDION | MODAL | POPOVER | LAYER (
  [TITLE       = '<string>',]
  [VISIBLE     = ON | OFF,]
  [ICON        = '<name>',]
  [STYLE (KEY = value, ...),]
  LAYOUT (
    STRUCTURE = '<grid-template-areas>',
    MAP ('<slot>' = <visual_name>, ...),
    [GAP = '<css-size>',]
    [PINNABLE = ON | OFF]
  ),
  [OPTIONS (KEY = value, ...)]
);
```

## Container Types

- **`BOX`**, **`SCROLL`**, **`DRAWER`**, **`SIDEBAR`**, **`TABS`**, **`ACCORDION`**, **`MODAL`**, **`POPOVER`**, **`LAYER`**

- `LAYER` stacks mapped visuals in the same region in map order. Use visual/container `STYLE (Z_INDEX = n)` when a specific stacking order needs to be explicit.

## Collapsible Drawer Options

Use a `DRAWER` container for filter panels that can float over the page or be pinned inline.

- **`PINNABLE`**: `ON`|`OFF` (default `ON`). Lets the user pin the drawer inline so it pushes the layout aside instead of floating over it.
- **`ICON`**: Top-level icon name for the trigger button (e.g. `'filter'`, `'settings'`).

## Examples

```sql
-- Group two KPI cards in a horizontal box
CREATE CONTAINER KpiGroup AS BOX (
  LAYOUT (
    STRUCTURE = 'A B',
    MAP ('A' = RevenueCard, 'B' = CustomerCard)
  )
);

-- Scrollable table panel
CREATE CONTAINER OrderScroll AS SCROLL (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = OrderTable)
  ),
  STYLE (MAX_HEIGHT = '400px')
);

-- Collapsible filter drawer
CREATE CONTAINER FilterDrawer AS DRAWER (
  TITLE = 'Filters',
  ICON = 'filter',
  LAYOUT (
    STRUCTURE = 'A / B',
    MAP ('A' = RegionSlicer, 'B' = YearSlider),
    PINNABLE = ON
  )
);
```

## Lifecycle

```sql
CREATE OR REPLACE CONTAINER FilterDrawer AS DRAWER (...);   -- redefine, including the layout
ALTER CONTAINER FilterDrawer (TITLE = 'Filters', VISIBLE = OFF, ICON = 'filter');
DROP CONTAINER IF EXISTS FilterDrawer;
```

`ALTER CONTAINER` patches `TITLE`, `SUBTITLE`, `TOOLTIP`, `STYLE`, `VISIBLE`, and `ICON`. An omitted
clause keeps its current value. Changing `LAYOUT` is a re-layout rather than a patch — use
`CREATE OR REPLACE CONTAINER`.

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
