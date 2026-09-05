# SLICER

A dropdown, list, tile, or button-bar selector. SOURCE provides the option list; the selected value is bound to a variable via ACTIONS, which filters other visuals.

## Syntax

```sql
CREATE VISUAL VisualName AS SLICER (
  SOURCE   = #dataset,
  MAPPINGS (
    VALUE = column,
    LABEL = column,
    IMAGE = column
  ),
  OPTIONS (
    MODE           = SINGLE|MULTI,
    LAYOUT         = 'DROPDOWN'|'LIST'|'TILE'|'BUTTON_BAR'|'CHIPS',
    SEARCHABLE     = ON|OFF,
    MAX_OPTIONS    = n,
    SORT           = ALPHA|VALUE|SOURCE,
    IMAGE_SIZE     = 'css-size',
    IMAGE_POSITION = LEFT|RIGHT|TOP,
    IMAGE_FIT      = contain|cover|fill,
    DEFAULT        = 'value',
    INCLUDE_ALL    = ON|OFF,
    ALL_LABEL      = 'text',
    TITLE          = 'text'
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@variable, value)
  )
);
```

## Mappings

- **VALUE** — Column supplying selectable values (required).
- **LABEL** — Optional display text column if different from the value stored.
- **IMAGE** — Optional image column supplying URLs, file paths, or data URIs to render with each option.

## Options

- **MODE = SINGLE|MULTI** — Single-select or multi-select operation (default SINGLE).
- **LAYOUT = 'DROPDOWN'|'LIST'|'TILE'|'BUTTON_BAR'|'CHIPS'** — Presentation layout mode (default DROPDOWN).
- **SEARCHABLE = ON|OFF** — Enables in-control search box to filter options (default OFF).
- **MAX_OPTIONS = n** — Limits visible options to n items and displays an overflow indicator.
- **SORT = ALPHA|VALUE|SOURCE** — Controls option sorting order (default SOURCE).
- **IMAGE_SIZE = 'css-size'** — Rendered image dimensions, such as `'24px'` or `'32px'` (default `'24px'`).
- **IMAGE_POSITION = LEFT|RIGHT|TOP** — Placement of the image relative to the option label (default LEFT).
- **IMAGE_FIT = contain|cover|fill** — CSS object-fit behavior for the option image (default cover).
- **DEFAULT = 'value'** — Pre-selected option on page load (default first row).
- **INCLUDE_ALL = ON|OFF** — Prepends an 'All' option in single-select dropdown mode (default ON).
- **ALL_LABEL = 'text'** — Label for the All option (default 'All').
- **TITLE = 'text'** — Control label shown above the visual.

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — Fires when selection changes; passes the selected value to @variable.

## Examples

```sql
DECLARE @region VARCHAR = 'All';

SELECT DISTINCT region INTO #region_list FROM #sales;

CREATE VISUAL RegionSlicer AS SLICER (
  SOURCE   = #region_list,
  MAPPINGS (VALUE = region),
  OPTIONS  (
    MODE       = SINGLE,
    LAYOUT     = 'TILE',
    SEARCHABLE = ON,
    SORT       = ALPHA,
    TITLE      = 'Region'
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, value))
);

CREATE VISUAL SalesBar AS BAR (
  SOURCE = (SELECT product, SUM(amount) AS revenue FROM #sales
            WHERE @region = 'All' OR region = @region
            GROUP BY product),
  MAPPINGS (X = product, Y = revenue)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
