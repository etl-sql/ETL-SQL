# NUMBERBOX
A numeric input field with validation, formatting, and steppers. The value is bound to an INT or DECIMAL variable via ACTIONS.

Mappings: none

## Syntax

```sql
CREATE VISUAL VisualName AS NUMBERBOX (
  OPTIONS (
    ...
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@variable, value),
    ON_SUBMIT = SET_PARAMETER(@variable, value)
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause. Configure choices and behaviour using `OPTIONS` and `ACTIONS`.

## Options

- **LABEL = 'text'** — label text shown next to or above the input
- **LABEL_POSITION = TOP|LEFT|HIDDEN** — position of the visual label (default: TOP)
- **MIN = n** — minimum allowed numeric value
- **MAX = n** — maximum allowed numeric value
- **STEP = n** — increment step amount for stepper buttons or arrow keys (default: 1)
- **SHOW_STEPPER = ON|OFF** — displays increment (+) and decrement (−) stepper buttons (default: OFF)
- **DECIMALS = n** — number of decimal places to allow (default: 0)
- **FORMAT = 'format'** — display formatting string applied when not focused (e.g., 'C2', 'N0', 'P1')
- **PREFIX = 'text'** — unit or currency prefix symbol displayed before the number (e.g., '$')
- **SUFFIX = 'text'** — unit suffix label displayed after the number (e.g., ' kg', ' ms')
- **PLACEHOLDER = 'hint text'** — greyed-out text shown when the input is empty
- **DEFAULT = n** — pre-populated numeric value on load

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — fires when the value changes; passes the numeric result to @variable
- **ON_SUBMIT = SET_PARAMETER(@variable, value)** — fires when the user presses Enter or leaves the field (blur)

## Examples

```sql
DECLARE @threshold DECIMAL = 500.00;

CREATE VISUAL MinAmount AS NUMBERBOX (
  TITLE          = 'Order Threshold',
  OPTIONS        (
    LABEL        = 'Minimum Amount',
    MIN          = 0,
    MAX          = 10000,
    STEP         = 50,
    SHOW_STEPPER = ON,
    FORMAT       = 'C2',
    PREFIX       = '$',
    DEFAULT      = 500
  ),
  ACTIONS        (
    ON_SUBMIT = SET_PARAMETER(@threshold, value)
  )
);

CREATE VISUAL OrdersTable AS TABLE (
  SOURCE = (SELECT * FROM #orders WHERE amount >= @threshold)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
