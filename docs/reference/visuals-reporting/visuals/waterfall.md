Type: WATERFALL
Shows how an initial value increases and decreases across a sequence of steps, ending at a final total. Ideal for financial statements, budget bridges, and variance analysis.

Mappings:
- **NAME** - label for each bar (e.g. 'Opening Balance', 'Sales', 'Costs')
- **VALUE** - the change amount (positive = increase, negative = decrease). Mark the total bar with a special flag (see TOTAL column below)
- **TOTAL** - optional boolean column; rows where TOTAL = 1 are drawn as totals (anchored to zero, showing the running sum)

Options:
  COLORS = ('positive_color', 'negative_color', 'total_color')
           e.g. COLORS = ('#27ae60', '#e74c3c', '#2980b9')
- **SHOW_VALUES = ON|OFF** - display values on bars (default ON)

```sql
SELECT 'Opening' AS item, 50000 AS amount, 1 AS is_total INTO #bridge UNION ALL
SELECT 'Revenue',             120000,            0             UNION ALL
SELECT 'COGS',                -45000,            0             UNION ALL
SELECT 'OpEx',                -30000,            0             UNION ALL
SELECT 'Net Profit',           95000,            1;

CREATE VISUAL ProfitBridge AS WATERFALL (
  SOURCE   = #bridge,
  MAPPINGS (NAME = item, VALUE = amount, TOTAL = is_total),
  OPTIONS  (
    COLORS (
      positive = '#27ae60',
      negative = '#e74c3c',
      total = '#2980b9'
    ),
    SHOW_VALUES = ON,
    TITLE       = 'Q1 Profit Bridge'
  )
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
