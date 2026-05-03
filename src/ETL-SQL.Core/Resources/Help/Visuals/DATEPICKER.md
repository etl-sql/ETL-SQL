Type: DATEPICKER
An interactive date-selection control. No SOURCE or data query is required. The selected date is bound to a script variable via ACTIONS, which is then used to filter other visuals.

Mappings: none

Options:
  DEFAULT = 'YYYY-MM-DD'   — initial date (or omit for today)
  MIN     = 'YYYY-MM-DD'   — earliest selectable date
  MAX     = 'YYYY-MM-DD'   — latest selectable date (or 'TODAY')

Actions:
  ON_CHANGE = SET_PARAMETER(@variable, value)
              — fires when the user picks a date; passes ISO string to @variable

```sql
-- Declare the variable the picker will drive
DECLARE @from_date DATE = DATEADD(DAY, -30, GETDATE());
DECLARE @to_date   DATE = GETDATE();

-- Date range pickers
CREATE VISUAL StartPicker AS DATEPICKER (
  OPTIONS (DEFAULT = DATEADD(DAY, -30, GETDATE()), MAX = 'TODAY'),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@from_date, value))
);

CREATE VISUAL EndPicker AS DATEPICKER (
  OPTIONS (DEFAULT = GETDATE(), MAX = 'TODAY'),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@to_date, value))
);

-- Chart that responds to the pickers
CREATE VISUAL SalesTrend AS LINE (
  SOURCE   = (SELECT sale_date, SUM(amount) AS total FROM #sales
              WHERE sale_date BETWEEN @from_date AND @to_date
              GROUP BY sale_date),
  MAPPINGS (X = sale_date, Y = total)
);
```
