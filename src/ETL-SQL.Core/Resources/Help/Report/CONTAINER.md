# CONTAINER
Groups visuals within a page using its own nested layout grid. Useful for grouping related charts in a card region, a scrollable sub-panel, or a collapsible filter drawer.

Syntax:
  CREATE CONTAINER <name> AS BOX | SCROLL (
    STRUCTURE    = '<grid-template-areas>',
    MAP ('<slot>' = <visual_name>, ...),
    [TITLE       = '<string>',]
    [STYLE (KEY = value, ...),]
    [COLLAPSIBLE = ON | OFF,]
    [PINNABLE    = ON | OFF,]
    [ICON        = '<name>']
  );

Types:
  BOX     — fixed-height container with a grid layout
  SCROLL  — vertically scrollable container (useful for long tables or lists)

## Collapsible Drawer Containers
When COLLAPSIBLE = ON the container becomes an overlay drawer — it floats
on top of the layout and is toggled by a trigger icon on the page edge.

  COLLAPSIBLE  — ON|OFF (default OFF). Renders as an overlay drawer when ON.
  PINNABLE     — ON|OFF (default ON). Lets the user pin the drawer inline so it
                 pushes the layout aside instead of floating over it.
  ICON         — Icon name for the trigger button (e.g. 'filter', 'settings').

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

-- Collapsible filter drawer
CREATE CONTAINER FilterDrawer AS BOX (
  TITLE       = 'Filters',
  COLLAPSIBLE = ON,
  PINNABLE    = ON,
  ICON        = 'filter',
  STRUCTURE   = 'A / B',
  MAP ('A' = RegionSlicer, 'B' = YearSlider)
);

CREATE PAGE Dashboard AS LAYOUT (
  STRUCTURE = 'K K / O O',
  MAP ('K' = KpiGroup, 'O' = OrderScroll)
);
```
