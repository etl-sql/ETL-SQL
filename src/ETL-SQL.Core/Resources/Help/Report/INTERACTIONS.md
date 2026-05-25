# INTERACTIONS
Defines how a visual responds when a user selects data in another visual on the same page.

Syntax:
```sql
INTERACTIONS (
    ON_SELECT = HIGHLIGHT | FILTER | NONE,
    [MATCHING = <column>]
)
```

Modes:
  FILTER     — re-query and hide non-matching rows
  HIGHLIGHT  — keep the full visual and ghost non-matching data
  NONE       — ignore cross-visual selections

```sql
CREATE VISUAL CategoryBreakdown AS BAR (
    SOURCE = #sales,
    MAPPINGS (X = Category, Y = Revenue),
    INTERACTIONS (ON_SELECT = HIGHLIGHT)
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
