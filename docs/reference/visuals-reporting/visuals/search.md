# SEARCH

A free-text search input with configurable debouncing, match pattern formatting, minimum character threshold, and clear button.

## Syntax

```sql
CREATE VISUAL VisualName AS SEARCH (
  OPTIONS (
    PLACEHOLDER = 'hint text',
    DEFAULT     = 'initial text',
    MATCH_MODE  = CONTAINS|STARTS_WITH|EXACT,
    MIN_CHARS   = n,
    DEBOUNCE    = milliseconds,
    SHOW_CLEAR  = ON|OFF
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@variable, value)
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause. Configure choices and behaviour using `OPTIONS` and `ACTIONS`.

## Options

- **PLACEHOLDER = 'hint text'** — Greyed-out placeholder text when input is empty.
- **DEFAULT = 'initial text'** — Pre-populated value on load.
- **MATCH_MODE = CONTAINS|STARTS_WITH|EXACT** — Wraps the emitted parameter value with wildcards (`%val%` for CONTAINS, `val%` for STARTS_WITH, raw for EXACT) (default EXACT).
- **MIN_CHARS = n** — Suppresses parameter updates until at least n characters are typed (default 0).
- **DEBOUNCE = n** — Milliseconds to wait after keypress before firing `ON_CHANGE` (default 350).
- **SHOW_CLEAR = ON|OFF** — Shows an accessible × button that resets the field and fires parameter updates (default OFF).

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — Fires after the configured debounce interval when the user types or clears the field.

## Examples

```sql
DECLARE @pattern STRING = '';

CREATE VISUAL CustomerSearch AS SEARCH (
  OPTIONS (
    PLACEHOLDER = 'Search customer or email…',
    MATCH_MODE  = CONTAINS,
    MIN_CHARS   = 2,
    SHOW_CLEAR  = ON
  ),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@pattern, value))
);

CREATE VISUAL CustomerTable AS TABLE (
  SOURCE = (SELECT customer_id, name, email
            FROM #customers
            WHERE @pattern = ''
               OR name LIKE @pattern
               OR email LIKE @pattern),
  MAPPINGS (customer_id, name, email)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
