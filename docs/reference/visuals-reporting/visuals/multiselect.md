# MULTISELECT

A multi-value selection control rendering checkboxes, popup dropdowns, or chips. The selection is bound to a LIST parameter via ACTIONS and used to filter other visuals.

## Syntax

```sql
CREATE VISUAL VisualName AS MULTISELECT (
  SOURCE   = #dataset,
  MAPPINGS (
    VALUE = column,
    LABEL = column,
    IMAGE = column
  ),
  OPTIONS (
    DEFAULT          = ('val1', 'val2'),
    LAYOUT           = LIST|CHIPS|DROPDOWN,
    SEARCHABLE       = ON|OFF,
    SHOW_SELECT_ALL  = ON|OFF,
    SELECT_ALL_LABEL = 'text',
    CLEAR_ALL_LABEL  = 'text',
    MAX_OPTIONS      = n,
    SORT             = ALPHA|VALUE|SOURCE,
    IMAGE_SIZE       = 'css-size',
    IMAGE_POSITION   = LEFT|RIGHT|TOP,
    IMAGE_FIT        = contain|cover|fill
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@variable, value)
  )
);
```

## Mappings

- **VALUE** — Column supplying selectable values (required).
- **LABEL** — Optional display text column if different from the stored value.
- **IMAGE** — Optional image column supplying URLs, file paths, or data URIs to render with each item.

## Options

- **DEFAULT = ('val1', 'val2')** — Pre-selected values on initial load. Accepts a list of literals or a single scalar string.
- **LAYOUT = LIST|CHIPS|DROPDOWN** — Display layout mode (default LIST).
- **SEARCHABLE = ON|OFF** — Shows an in-control type-to-filter search input (default OFF).
- **SHOW_SELECT_ALL = ON|OFF** — Displays Select All and Clear All controls (default ON).
- **SELECT_ALL_LABEL = 'text'** — Custom text for the Select All action (default 'Select All').
- **CLEAR_ALL_LABEL = 'text'** — Custom text for the Clear All action (default 'Clear All').
- **MAX_OPTIONS = n** — Limits visible options to n items and displays an overflow indicator.
- **SORT = ALPHA|VALUE|SOURCE** — Controls option sorting order (default SOURCE).
- **IMAGE_SIZE = 'css-size'** — Rendered image dimensions (default `'24px'`).
- **IMAGE_POSITION = LEFT|RIGHT|TOP** — Placement of the image relative to the option label (default LEFT).
- **IMAGE_FIT = contain|cover|fill** — CSS object-fit behavior for the option image (default cover).

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — Passes the current array of selected values as a JSON list to @variable.

## Examples

```sql
DECLARE @selected_regions LIST = ('East', 'West');

SELECT DISTINCT region INTO #region_opts FROM #sales;

CREATE VISUAL RegionFilter AS MULTISELECT (
  SOURCE   = #region_opts,
  MAPPINGS (VALUE = region),
  OPTIONS  (
    DEFAULT         = ('East', 'West'),
    LAYOUT          = CHIPS,
    SEARCHABLE      = ON,
    SHOW_SELECT_ALL = ON
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@selected_regions, value))
);

CREATE VISUAL RegionBar AS BAR (
  SOURCE   = (SELECT region, SUM(amount) AS revenue FROM #sales
              WHERE region IN @selected_regions
              GROUP BY region),
  MAPPINGS (X = region, Y = revenue)
);
```

## References

- [SLICER](slicer.md)
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
