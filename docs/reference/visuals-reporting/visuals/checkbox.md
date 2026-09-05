# CHECKBOX
A boolean toggle control. The state is bound to a BIT, BOOLEAN, or domain-string variable via ACTIONS.

Mappings: none

## Syntax

```sql
CREATE VISUAL VisualName AS CHECKBOX (
  OPTIONS (
    ...
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@variable, value)
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause. Configure choices and behaviour using `OPTIONS` and `ACTIONS`.

## Options

- **LABEL = 'text'** — text appearing beside the checkbox or toggle switch element itself
- **DISPLAY_STYLE = CHECKBOX|TOGGLE** — render as a standard checkbox or a modern toggle switch (default: CHECKBOX)
- **TRUE_VALUE = 'value'** — value emitted when the control is checked/active (default: '1')
- **FALSE_VALUE = 'value'** — value emitted when the control is unchecked/inactive (default: '0')
- **DEFAULT = ON|OFF** — initial state of the control (default: OFF)
- **LABEL_POSITION = TOP|LEFT|HIDDEN** — position of the visual label (default: TOP)

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — fires when toggled; passes TRUE_VALUE or FALSE_VALUE to @variable

## Examples

```sql
DECLARE @active_filter STRING = 'N';

CREATE VISUAL ActiveToggle AS CHECKBOX (
  TITLE          = 'Filter Status',
  OPTIONS        (
    LABEL         = 'Active Accounts Only',
    DISPLAY_STYLE = TOGGLE,
    TRUE_VALUE    = 'Y',
    FALSE_VALUE   = 'N',
    DEFAULT       = OFF
  ),
  ACTIONS        (ON_CHANGE = SET_PARAMETER(@active_filter, value))
);

CREATE VISUAL AccountsTable AS TABLE (
  SOURCE = (SELECT * FROM #accounts WHERE @active_filter = 'N' OR is_active = @active_filter)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
