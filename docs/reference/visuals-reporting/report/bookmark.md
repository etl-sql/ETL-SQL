# BOOKMARK

Author bookmarks capture named combinations of parameter values, page, and UI state.
They are declared in the report script and appear in the manifest for runtime consumption.

## Syntax

```sql
CREATE BOOKMARK <name> AS (
  TITLE = 'label',
  PARAMETERS (
    @param1 = 'value1',
    @param2 = value2
  ),
  PAGE = PageName,
  STATE (
    ObjectName.PROPERTY = ON|OFF
  ),
  DEFAULT = ON
);
```

All clauses are optional. A bookmark with only `PAGE` acts as a named navigation target.

## Clauses

| Clause | Description |
|--------|-------------|
| `TITLE` | Display label shown in the bookmark picker. |
| `PARAMETERS` | Parameter assignments applied atomically when the bookmark is activated. |
| `PAGE` | Target page to navigate to. |
| `STATE` | UI state entries (VISIBLE, COLLAPSED) for named objects. |
| `DEFAULT` | Marks this bookmark as the author default. Only one bookmark may be DEFAULT. |

## APPLY_BOOKMARK Action

Buttons and visuals can apply a bookmark via the `APPLY_BOOKMARK` action:

```sql
CREATE BUTTON ViewWest AS (
  TITLE = 'West Coast View',
  ACTIONS (ON_CLICK = APPLY_BOOKMARK(WestCoastDetail))
);
```

## Deep Links

Use identifier-only URL hashes — no parameter values are exposed:

```
#bookmark=WestCoastDetail
```

## Launch Precedence

1. URL bookmark (`#bookmark=Name`)
2. Selected saved view (Portal)
3. User default view (Portal)
4. Author default bookmark (`DEFAULT = ON`)
5. Declared parameter defaults

## Lifecycle

```sql
DROP BOOKMARK IF EXISTS WestCoastDetail;
```

## Examples

```sql
CREATE BOOKMARK Overview AS (
  TITLE = 'Overview (All Regions)',
  PARAMETERS (@region = 'All', @year = 2026),
  PAGE = Main,
  DEFAULT = ON
);

CREATE BOOKMARK DetailView AS (
  TITLE = 'Detail View',
  PARAMETERS (@region = 'West'),
  PAGE = Detail,
  STATE (FilterPanel.COLLAPSED = ON)
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
