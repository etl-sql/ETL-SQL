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
| `PARAMETERS` | Typed parameter assignments applied atomically when the bookmark is activated. Values keep their declared type — `@year = 2026` is a number, `@region = 'West'` a string, `NULL` is null; they are never flattened to quoted strings. |
| `PAGE` | Target page to navigate to. |
| `STATE` | UI state entries for named objects. Only `ObjectName.VISIBLE` and `ObjectName.COLLAPSED` are accepted, each set to `ON` or `OFF`. |
| `DEFAULT` | Marks this bookmark as the author default (`ON` or `OFF`). At most one bookmark may be `DEFAULT = ON`. |

Author bookmarks are shared and versioned with the report. They are distinct from a user's private
**saved views**, which persist per-user in the Portal but use the same resolved-state envelope.

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

## Authoring aids

The editor knows about bookmarks:

- Typing inside `APPLY_BOOKMARK(` or after `DROP BOOKMARK` completes the bookmarks this script
  declares — nothing else is valid in those positions.
- Hovering a bookmark identifier shows its page, typed parameter values, and state entries.
- Renaming a bookmark (F2) rewrites its declaration and every `APPLY_BOOKMARK` and `DROP BOOKMARK`
  reference, so a rename cannot leave a button pointing at a bookmark that no longer exists.
- A bookmark that refers to a page, container, visual, or parameter that has since been renamed or
  removed is reported as a diagnostic rather than failing at run time.

The Report Builder lists bookmarks in its sidebar, where you can add one, edit its title, mark it the
report default, or remove it. Editing a bookmark rewrites only that statement — the rest of the script
is left exactly as written.

## Readers' saved views

A bookmark you write is shared with everyone who opens the report and is versioned with the script. A
reader can also save their **own** view from the Views menu — their current filters and page under a
name only they see. Saved views use the same state contract, so both appear in the same menu, listed
separately, and both apply the same way.

Saved views are private: another person's view can never be opened from its link. If the report is
republished after a view was saved, the reader is warned that parts of it may no longer apply — the
view still opens, with the parts that no longer exist dropped.

Offline snapshots replay bookmarks too. The saved figures cannot change without a server, so applying a
bookmark there restores the page, layout state, and filter selections, and says so.

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
