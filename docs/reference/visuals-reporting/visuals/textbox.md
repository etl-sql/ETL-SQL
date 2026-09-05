# TEXTBOX
A single-line or multi-line text input field. The typed value is bound to a STRING variable via ACTIONS.

Mappings: none

## Syntax

```sql
CREATE VISUAL VisualName AS TEXTBOX (
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
- **PLACEHOLDER = 'hint text'** — greyed-out text shown when the input is empty
- **DEFAULT = 'initial text'** — pre-populated value on load
- **MAX_LENGTH = n** — positive integer limiting the number of characters the user can enter
- **MULTILINE = ON|OFF** — render as a multiline textarea when ON (default: OFF)
- **ROWS = n** — height in rows for multiline mode (implies MULTILINE = ON)
- **PATTERN = 'regex'** — regular expression pattern for client-side input validation
- **VALIDATION_MESSAGE = 'text'** — custom error message displayed when input fails pattern validation

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — fires when the user types or clears the field
- **ON_SUBMIT = SET_PARAMETER(@variable, value)** — fires when the user presses Enter or leaves the field (blur)

## Examples

```sql
DECLARE @notes STRING = '';

CREATE VISUAL FeedbackInput AS TEXTBOX (
  TITLE          = 'Customer Feedback',
  OPTIONS        (
    LABEL              = 'Comments',
    MULTILINE          = ON,
    ROWS               = 4,
    MAX_LENGTH         = 500,
    PLACEHOLDER        = 'Enter feedback here...',
    PATTERN            = '^[A-Za-z0-9 .,!?\n\r]*$',
    VALIDATION_MESSAGE = 'Special characters are not allowed'
  ),
  ACTIONS        (
    ON_SUBMIT = SET_PARAMETER(@notes, value)
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
