# PAGE
Defines a report page as a CSS grid layout. Visuals, containers, and buttons are mapped into named grid slots.

Syntax:
  CREATE PAGE <name> AS (
    STRUCTURE = '<grid-template-areas>',
    MAP ('<slot>' = <visual_or_container>, ...),
    STYLE (KEY = value, ...)
  );

STRUCTURE uses CSS grid-template-areas syntax. Each quoted string is a row; cells in the same row are separated by spaces. Repeat a slot letter to span columns.

Style options:
  GAP         — space between grid cells (e.g. '12px')
  PADDING     — page padding (e.g. '16px 24px')
  BACKGROUND  — page background colour or CSS value

```sql
-- Full-width header, then two equal columns
CREATE PAGE Dashboard AS (
  STRUCTURE = 'H H / L R',
  MAP (
    'H' = KpiRow,
    'L' = RegionChart,
    'R' = OrderTable
  ),
  STYLE (GAP = '16px', PADDING = '24px')
);

-- Four-slot grid
CREATE PAGE Detail AS (
  STRUCTURE = 'A B / C D',
  MAP (
    'A' = TrendLine,
    'B' = CategoryPie,
    'C' = TopCustomers,
    'D' = RecentOrders
  )
);
```
