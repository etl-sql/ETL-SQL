# INTERACTIONS

Defines how a visual responds when a user selects data in another visual on the same page.

## Syntax

```sql
INTERACTIONS (
  ON_SELECT = HIGHLIGHT | FILTER | NONE,
  [MATCHING = <column>]
)
```

## Modes

- **`FILTER`**: Re-query and hide non-matching rows.
- **`HIGHLIGHT`**: Keep the full visual and ghost non-matching data.
- **`NONE`**: Ignore cross-visual selections.

On a chart or table with `HIGHLIGHT` or `FILTER`, a left click selects. Its `ON_CLICK` actions
move to the right-click menu; see [ACTIONS](actions.md).

## Examples

```sql
CREATE VISUAL CategoryBreakdown AS BAR (
  SOURCE = #sales,
  MAPPINGS (X = Category, Y = Revenue),
  INTERACTIONS (ON_SELECT = HIGHLIGHT)
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
