# CONTAINER

Groups visuals within a page using nested layout grids, tabs, accordions, modals, or collapsible drawers.

## Syntax

```sql
CREATE CONTAINER <name> AS BOX | SCROLL | DRAWER | SIDEBAR | TABS | ACCORDION | MODAL | POPOVER | LAYER (
  [TITLE = '<string>',]
  [VISIBLE = ON | OFF,]
  [ICON = '<icon_name>',]
  [COLLAPSIBLE = ON | OFF,]
  [STYLE (KEY = value, ...),]
  LAYOUT (
    STRUCTURE = '<grid-template-areas>',
    MAP (
      '<slot>' = <visual_name> [(ICON = '<icon_name>', BADGE = '<badge_text>')],
      ...
    ),
    [GAP = '<css-size>',]
    [PINNABLE = ON | OFF]
  ),
  [OPTIONS (
    [TAB_POSITION = TOP|BOTTOM|LEFT|RIGHT,]
    [DEFAULT_OPEN = '<slot_name>',]
    [DEFAULT = OPEN|CLOSED,]
    [SHOW_ACTIVE_COUNT = ON|OFF,]
    [REFRESH = <seconds>,]
    [POSITION = LEFT|RIGHT]
  )]
);
```

## Container Types

- **BOX** — Standard nested card layout box with optional collapsible header.
- **SCROLL** — Scrollable sub-panel with maximum height/width boundary constraints.
- **DRAWER** — Collapsible side drawer panel with slide-out animations.
- **SIDEBAR** — Pinned vertical sidebar container for filters or navigation.
- **TABS** — Tabbed navigation switcher displaying one active visual slot at a time.
- **ACCORDION** — Vertically stacked collapsible panels with expand/collapse toggles.
- **MODAL** — Center-screen overlay dialog presented via `SHOW_MODAL` actions.
- **POPOVER** — Contextual popup container anchored relative to trigger elements.
- **LAYER** — Stacks multiple visuals within the same grid cell using Z-index order.

## Options

- **TAB_POSITION = TOP|BOTTOM|LEFT|RIGHT** — Position of tab switcher navigation buttons in `TABS` container (default `TOP`).
- **DEFAULT_OPEN = 'slot'** — Identifies the slot or accordion item initially expanded on report load.
- **DEFAULT = OPEN|CLOSED** — Initial open or collapsed state for `DRAWER` and collapsible `BOX` containers (default `CLOSED`).
- **COLLAPSIBLE = ON|OFF** — Enables expand/collapse toggle button on card headers for `BOX` containers.
- **SHOW_ACTIVE_COUNT = ON|OFF** — Displays badge indicator showing count of active non-default filter values in container.
- **REFRESH = seconds** — Periodic refresh interval in seconds for re-evaluating visuals enclosed in the container.
- **POSITION = LEFT|RIGHT** — Slide direction anchor for `DRAWER` containers (default `RIGHT`).

## Per-Slot Decorations

Inside `LAYOUT (MAP (...))`, slot mappings can include:
- **ICON = 'icon-name'** — Tab or accordion header icon.
- **BADGE = 'text'** — Badge label rendered alongside tab or header text.

## Examples

```sql
CREATE CONTAINER SalesTabs AS TABS (
  LAYOUT (
    MAP (
      'Overview' = SummaryCard (ICON = 'dashboard', BADGE = 'New'),
      'Regional' = RegionalMap (ICON = 'globe')
    )
  ),
  OPTIONS (
    TAB_POSITION = LEFT,
    SHOW_ACTIVE_COUNT = ON,
    REFRESH = 60
  )
);
```

```sql
CREATE CONTAINER FilterDrawer AS DRAWER (
  TITLE = 'Filters',
  ICON = 'filter',
  LAYOUT (
    STRUCTURE = 'A / B',
    MAP ('A' = RegionSlicer, 'B' = YearSlider),
    PINNABLE = ON
  ),
  OPTIONS (
    DEFAULT = OPEN,
    SHOW_ACTIVE_COUNT = ON
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
