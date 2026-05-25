# PAGE
Defines a report page as a CSS grid layout. Visuals, containers, and buttons are mapped into named grid slots. The page mode is required.

Syntax:
  CREATE PAGE <name> AS DASHBOARD | PAGINATED (
    [TITLE = '<string>',]
    [TOOLTIP = '<string>',]
    [VISIBLE = ON | OFF,]
    [REFRESH = <seconds>,]
    LAYOUT (
      STRUCTURE = '<grid-template-areas>',
      MAP ('<slot>' = <visual_or_container>, ...),
      [GAP = '<css-size>']
    ),
    STYLE (KEY = value, ...)
  );

DASHBOARD pages load data immediately and refresh as controls change. PAGINATED pages stage prompt changes until an APPLY_PARAMETERS button is clicked.

STRUCTURE uses CSS grid-template-areas syntax. Each quoted string is a row; cells in the same row are separated by spaces. Repeat a slot letter to span columns. Page LAYOUT (...) is preferred, but STRUCTURE/MAP/GAP may also be written directly in the page body.

Layout options:
  GAP         — space between grid cells (e.g. '12px')

Style options:
  PADDING     — page padding (e.g. '16px 24px')
  BACKGROUND  — page background colour or CSS value

```sql
-- Full-width header, then two equal columns
CREATE PAGE Dashboard AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'H H / L R',
    MAP (
      'H' = KpiRow,
      'L' = RegionChart,
      'R' = OrderTable
    ),
    GAP = '16px'
  ),
  STYLE (PADDING = '24px')
);

-- Run-to-data paginated page
CREATE PAGE Detail AS PAGINATED (
  LAYOUT (
    STRUCTURE = 'F F / R R',
    MAP (
      'F' = FilterPanel,
      'R' = RecentOrders
    )
  )
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
