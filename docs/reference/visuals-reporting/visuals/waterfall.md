# WATERFALL

Shows how an initial value increases and decreases across a sequence of steps, ending at a final total. Ideal for financial statements, budget bridges, and variance analysis.

## Syntax

```sql
CREATE VISUAL VisualName AS WATERFALL (
  SOURCE = #tableName,
  MAPPINGS (
    NAME = col_category,      -- or X = col_category
    VALUE = col_amount,       -- or Y = col_amount
    TOTAL = col_is_total,
    SUBTOTAL = col_is_subtotal
  ),
  OPTIONS (
    ORIENTATION = VERTICAL,
    CONNECTOR_LINES = ON,
    COLOR_TOTAL = '#2980b9',
    COLOR_SUBTOTAL = '#475569',
    COLOR_UP = '#27ae60',
    COLOR_DOWN = '#e74c3c'
  )
);
```

## Mappings

- **NAME** — Label for each bar step (e.g. 'Opening Balance', 'Sales', 'Costs'). Alias: **X**.
- **VALUE** — The change amount (positive = increase, negative = decrease). Alias: **Y**.
- **X** — Categorical step label (alias for **NAME**).
- **Y** — Step delta value (alias for **VALUE**).
- **TOTAL** — Optional column indicating total or subtotal rows. Rows where TOTAL is true/1 or 'TOTAL' are drawn as totals (anchored to zero). Values equal to 'SUBTOTAL' are drawn as intermediate subtotals.
- **SUBTOTAL** — Optional dedicated boolean/flag column marking intermediate subtotal bars (anchored to zero, continuing running base).

## Options

- **CONNECTOR_LINES = ON|OFF** — Toggle connector lines between adjacent bars (default ON).
- **CONNECTOR_LINE_COLOR = '#rrggbb'** — Color of the connector line between bars (default `#9ca3af`).
- **CONNECTOR_LINE_WIDTH = n** — Stroke width in pixels for connector lines (default 1).
- **ORIENTATION = VERTICAL|HORIZONTAL** — Direction of waterfall bars (default VERTICAL). Set to `HORIZONTAL` for horizontal bar waterfall bridges.
- **COLOR_TOTAL = '#rrggbb'** — Fill color for grand total bars (default `#2980b9`).
- **COLOR_SUBTOTAL = '#rrggbb'** — Fill color for intermediate subtotal bars (default `#475569`).
- **COLOR_UP = '#rrggbb'** — Fill color for positive increase bars (default `#27ae60`). Alias: `COLOR_INCREASE`.
- **COLOR_DOWN = '#rrggbb'** — Fill color for negative decrease bars (default `#e74c3c`). Alias: `COLOR_DECREASE`.
- **SHOW_VALUES = ON|OFF** — Display values on bars (default ON).

## Examples

### Example 1: Vertical Bridge with NAME / VALUE Mappings

```sql
SELECT 'Opening' AS item, 50000 AS amount, 1 AS is_total, 0 AS is_subtotal INTO #bridge UNION ALL
SELECT 'Revenue',             120000,            0,             0             UNION ALL
SELECT 'COGS',                -45000,            0,             0             UNION ALL
SELECT 'Gross Margin',             0,            0,             1             UNION ALL
SELECT 'OpEx',                -30000,            0,             0             UNION ALL
SELECT 'Net Profit',           95000,            1,             0;

CREATE VISUAL ProfitBridge AS WATERFALL (
  SOURCE   = #bridge,
  MAPPINGS (
    NAME     = item,
    VALUE    = amount,
    TOTAL    = is_total,
    SUBTOTAL = is_subtotal
  ),
  OPTIONS  (
    ORIENTATION     = VERTICAL,
    CONNECTOR_LINES = ON,
    COLOR_TOTAL     = '#2980b9',
    COLOR_SUBTOTAL  = '#475569',
    COLOR_UP        = '#27ae60',
    COLOR_DOWN      = '#e74c3c',
    TITLE           = 'Q1 Profit Bridge'
  )
);
```

### Example 2: Horizontal Bridge with X / Y Mappings

```sql
SELECT 'Q1 Start' AS period, 1000 AS delta, 1 AS is_tot INTO #hbridge UNION ALL
SELECT 'Inflow',              600,          0           UNION ALL
SELECT 'Outflow',            -350,          0           UNION ALL
SELECT 'Q1 Close',           1250,          1;

CREATE VISUAL HorizontalCashFlow AS WATERFALL (
  SOURCE   = #hbridge,
  MAPPINGS (
    X     = period,
    Y     = delta,
    TOTAL = is_tot
  ),
  OPTIONS  (
    ORIENTATION          = HORIZONTAL,
    CONNECTOR_LINES      = ON,
    CONNECTOR_LINE_COLOR = '#94a3b8',
    COLOR_TOTAL          = '#1e40af',
    COLOR_UP             = '#166534',
    COLOR_DOWN           = '#991b1b',
    TITLE                = 'Cash Flow (Horizontal)'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
