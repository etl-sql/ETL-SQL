# CONTAINER
Groups visuals within a page using its own nested layout grid. Useful for grouping related charts in a card region, a scrollable sub-panel, or a collapsible filter drawer.

Syntax:
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

Types:
  BOX, SCROLL, DRAWER, SIDEBAR, TABS, ACCORDION, MODAL, POPOVER, LAYER

`LAYER` stacks mapped visuals in the same region in map order. Use visual/container
`STYLE (Z_INDEX = n)` when a specific stacking order needs to be explicit.

## Collapsible Drawer Containers
Use a DRAWER container for filter panels that can float over the page or be pinned inline.

  PINNABLE     — ON|OFF (default ON). Lets the user pin the drawer inline so it
                 pushes the layout aside instead of floating over it.
  ICON         — Top-level icon name for the trigger button (e.g. 'filter', 'settings').

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

-- Layered KPI over a supporting chart
CREATE CONTAINER RevenueLayer AS LAYER (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = RevenueTrend, 'B' = RevenueCard)
  )
);

CREATE PAGE Dashboard AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'K K / O O',
    MAP ('K' = KpiGroup, 'O' = OrderScroll)
  )
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
