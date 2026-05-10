# CONTAINER
Groups visuals within a page using its own nested layout grid. Useful for grouping related charts in a card region or a scrollable sub-panel.

Syntax:
  CREATE CONTAINER <name> AS BOX | SCROLL (
    STRUCTURE = '<grid-template-areas>',
    MAP ('<slot>' = <visual_name>, ...),
    STYLE (KEY = value, ...)
  );

Types:
  BOX     — fixed-height container with a grid layout
  SCROLL  — vertically scrollable container (useful for long tables or lists)

```sql
-- Group two KPI cards in a horizontal box
CREATE CONTAINER KpiGroup AS BOX (
  STRUCTURE = 'A B',
  MAP ('A' = RevenueCard, 'B' = CustomerCard)
);

-- Scrollable table panel
CREATE CONTAINER OrderScroll AS SCROLL (
  STRUCTURE = 'A',
  MAP ('A' = OrderTable),
  STYLE (MAX_HEIGHT = '400px')
);

CREATE PAGE Dashboard AS LAYOUT (
  STRUCTURE = 'K K / O O',
  MAP ('K' = KpiGroup, 'O' = OrderScroll)
);
```
