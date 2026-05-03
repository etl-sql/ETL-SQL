# NAVIGATION
Creates a menu or tab strip that links multiple report pages, providing user navigation between views.

Syntax:
  CREATE NAVIGATION <name> AS TAB | BUTTON | SIDEBAR (
    ORIENTATION = 'HORIZONTAL' | 'VERTICAL',
    PAGES       = (<page1>, <page2>, ...)
  );

Types:
  TAB     — horizontal tab strip (default for dashboards)
  BUTTON  — a row of navigation buttons
  SIDEBAR — vertical navigation panel on the left

```sql
CREATE PAGE Overview AS LAYOUT (
  STRUCTURE = 'A', MAP ('A' = SummaryChart)
);
CREATE PAGE Detail AS LAYOUT (
  STRUCTURE = 'A', MAP ('A' = DetailTable)
);
CREATE PAGE Trends AS LAYOUT (
  STRUCTURE = 'A', MAP ('A' = TrendLine)
);

CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = 'HORIZONTAL',
  PAGES       = (Overview, Detail, Trends)
);
```

The NAVIGATION component renders outside the page content area as a persistent menu. It is declared at the report level, not inside a page.
