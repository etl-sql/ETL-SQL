# CHECKBOX
A boolean toggle switch. The state (true/false) is bound to a BIT or BOOLEAN variable via ACTIONS.

Mappings: none

## Syntax

```sql
CREATE VISUAL VisualName AS CHECKBOX (
  OPTIONS (
    ...
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause. Configure choices and behaviour using `OPTIONS` and `ACTIONS`.

## Options

- **LABEL_POSITION = TOP|LEFT|HIDDEN** - position of the visual name label (default: TOP)

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** - fires when the checkbox is toggled; passes 1 (true) or 0 (false) to @variable

## Examples

```sql
DECLARE @show_details BIT = 0;

CREATE VISUAL DetailsToggle AS CHECKBOX (
  TITLE          = 'Show Details',
  LABEL_POSITION = 'LEFT',
  ACTIONS        (ON_CHANGE = SET_PARAMETER(@show_details, value))
);

-- Visual responds to the toggle
CREATE VISUAL SalesTable AS TABLE (
  SOURCE = #sales,
  STYLE  (DISPLAY = @show_details)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
