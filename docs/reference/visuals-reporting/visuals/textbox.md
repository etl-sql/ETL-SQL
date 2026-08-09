Type: TEXTBOX
A single-line text input field. The typed value is bound to a STRING variable via ACTIONS.

Mappings: none

Properties:
- **LABEL_POSITION = TOP|LEFT|HIDDEN** - position of the visual name label (default: TOP)

Options:
- **PLACEHOLDER = 'hint text'** - greyed-out text shown when the input is empty
- **DEFAULT = 'initial text'** - pre-populated value on load

Actions:
- **ON_CHANGE = SET_PARAMETER(@variable, value)** - fires when the user types or clears the field

```sql
DECLARE @user_filter STRING = '';

CREATE VISUAL UserInput AS TEXTBOX (
  TITLE          = 'Username',
  LABEL_POSITION = 'LEFT',
  OPTIONS        (PLACEHOLDER = 'Enter username...'),
  ACTIONS        (ON_CHANGE = SET_PARAMETER(@user_filter, value))
);

CREATE VISUAL UserList AS TABLE (
  SOURCE = (SELECT * FROM #users WHERE @user_filter = '' OR username LIKE '%' + @user_filter + '%')
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
