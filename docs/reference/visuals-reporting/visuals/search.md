# SEARCH
A free-text search input. The typed value is bound to a STRING variable via ACTIONS. Use with LIKE or a CONTAINS expression to filter other visuals.

Mappings: none

## Syntax

```sql
CREATE VISUAL VisualName AS SEARCH (
  OPTIONS (
    ...
  )
);
```

## Mappings

Filter controls do not use a `MAPPINGS` clause. Configure choices and behaviour using `OPTIONS` and `ACTIONS`.

## Options

- **PLACEHOLDER = 'hint text'** - greyed-out text shown when the input is empty
- **DEFAULT = 'initial text'** - pre-populated value on load
- **DEBOUNCE = n** - milliseconds to wait after keypress before firing (default 300)
- **SHOW_CLEAR = ON|OFF** - shows an accessible × button when the input contains text; activating it clears the value and fires `ON_CHANGE` (default OFF)

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** - fires after the configured debounce when the user types or clears the field

## Examples

```sql
DECLARE @search STRING = '';

CREATE VISUAL CustomerSearch AS SEARCH (
  OPTIONS (PLACEHOLDER = 'Search customers...', DEBOUNCE = 400, SHOW_CLEAR = ON),
  ACTIONS (ON_CHANGE   = SET_PARAMETER(@search, value))
);

CREATE VISUAL CustomerTable AS TABLE (
  SOURCE = (SELECT customer_id, name, email, total_spend
            FROM #customers
            WHERE @search = ''
               OR name  LIKE '%' + @search + '%'
               OR email LIKE '%' + @search + '%'),
  MAPPINGS (customer_id, name, email, total_spend)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
