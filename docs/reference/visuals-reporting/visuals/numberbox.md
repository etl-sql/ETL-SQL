Type: NUMBERBOX
A numeric input field with validation. The value is bound to an INT or DECIMAL variable via ACTIONS.

Mappings: none

Properties:
- **LABEL_POSITION = TOP|LEFT|HIDDEN** - position of the visual name label (default: TOP)
- **MIN = n** - minimum allowed value
- **MAX = n** - maximum allowed value
- **DECIMALS = n** - number of decimal places to allow (default: 0)

Options:
- **PLACEHOLDER = 'hint text'** - greyed-out text shown when the input is empty
- **DEFAULT = n** - pre-populated value on load

Actions:
- **ON_CHANGE = SET_PARAMETER(@variable, value)** - fires when the value changes; passes the numeric result to @variable

```sql
DECLARE @threshold DECIMAL = 500.00;

CREATE VISUAL MinAmount AS NUMBERBOX (
  TITLE          = 'Min Order Amount',
  LABEL_POSITION = 'LEFT',
  MIN            = 0,
  MAX            = 10000,
  DECIMALS       = 2,
  ACTIONS        (ON_CHANGE = SET_PARAMETER(@threshold, value))
);

CREATE VISUAL OrdersTable AS TABLE (
  SOURCE = (SELECT * FROM #orders WHERE amount >= @threshold)
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
