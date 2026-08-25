# SLICER
A dropdown selector. SOURCE provides the option list; the selected value is bound to a variable via ACTIONS, which filters other visuals.
For multi-select checkboxes, use MULTISELECT instead.

## Syntax

```sql
CREATE VISUAL VisualName AS SLICER (
  OPTIONS (
    ...
  )
);
```

## Mappings

- **VALUE** - column supplying selectable values (required)
- **LABEL** - optional display text column if different from the value stored

## Options

- **DEFAULT = 'value'** - pre-selected option on page load (default: first row)
- **INCLUDE_ALL = ON|OFF** - prepend an 'All' option that passes NULL or a special sentinel (default ON)
- **ALL_LABEL = 'text'** - label for the All option (default 'All')
- **TITLE = 'text'** - control label shown above the dropdown

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** - fires when selection changes; passes the VALUE column value to @variable

## Examples

```sql
DECLARE @region VARCHAR = 'All';

-- Populate the option list
SELECT DISTINCT region INTO #region_list FROM #sales;

CREATE VISUAL RegionSlicer AS SLICER (
  SOURCE   = #region_list,
  MAPPINGS (VALUE = region),
  OPTIONS  (INCLUDE_ALL = ON, ALL_LABEL = 'All Regions', TITLE = 'Region'),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, value))
);

-- Chart responds to the slicer
CREATE VISUAL SalesBar AS BAR (
  SOURCE = (SELECT product, SUM(amount) AS revenue FROM #sales
            WHERE @region = 'All' OR region = @region
            GROUP BY product),
  MAPPINGS (X = product, Y = revenue)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
