# NAVIGATION

Creates a menu or tab strip that links multiple report pages, providing user navigation between views.

## Syntax

```sql
CREATE NAVIGATION <name> AS TAB | BUTTON | LINK (
  ORIENTATION = HORIZONTAL | VERTICAL,
  PAGES       = (<page1>, <page2>, ...)
);
```

## Navigation Types

- **`TAB`**: Horizontal tab strip (default for dashboards).
- **`BUTTON`**: A row of navigation buttons.
- **`LINK`**: Inline text links.

## Examples

```sql
CREATE PAGE Overview AS DASHBOARD (
  STRUCTURE = 'A', MAP ('A' = SummaryChart)
);
CREATE PAGE Detail AS DASHBOARD (
  STRUCTURE = 'A', MAP ('A' = DetailTable)
);
CREATE PAGE Trends AS DASHBOARD (
  STRUCTURE = 'A', MAP ('A' = TrendLine)
);

CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  PAGES       = (Overview, Detail, Trends)
);
```

The `NAVIGATION` component renders outside the page content area as a persistent menu. It is declared at the report level, not inside a page.

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
