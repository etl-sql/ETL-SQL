# CARD

A prominent KPI tile showing a single large number with an optional label, trend indicator, goal comparison, status badge, and progress indicator. Ideal for dashboard headlines.

## Syntax

```sql
CREATE VISUAL CardName AS CARD (
  SOURCE = #summary,
  MAPPINGS (
    VALUE = value_column,
    LABEL = label_column,
    DELTA = prior_column,
    DELTA_LABEL = period_column,
    SPARKLINE = #trend (X = x_column, Y = y_column, TYPE = LINE, COLOR = '#10B981', REFERENCE_LINE = 100)
  ),
  OPTIONS (
    VALUE_COLOR = '#10B981'
  ),
  FORMATTING (
    WHEN VALUE >= 1000 THEN '#10B981'
  )
);
```

## Mappings

- **VALUE** — primary metric column (required); displayed large
- **LABEL** — caption column (optional); falls back to the metric column name
- **GOAL** — target value column; drives status and optional progress display
- **DELTA** — prior-period value column; drives trend/delta display
- **DELTA_LABEL** — comparison period label column (optional); dynamic alternative to static OPTIONS (DELTA_LABEL = 'text')
- **SPARKLINE** — native `LINE`, `AREA`, or `BAR` trend from a named temp table; requires `X` and `Y`, optional `COLOR = 'css-color'` and `REFERENCE_LINE = n`

## Options
- **FORMAT = '.NET format string'** — e.g. 'N0', 'C2', 'P1'
- **ABBREVIATE = ON|OFF** — shorten large numbers, e.g. 1250000 -> 1.25M
- **VALUE_COLOR = 'css-color'** — fixed or default color for the primary metric value
- **PREFIX = 'text'** — prepend text to the displayed value
- **SUFFIX = 'text'** — append text to the displayed value
- **GOAL = numeric** — literal target when MAPPINGS(GOAL=...) is not used
- **CLOSE_PCT = decimal** — close threshold, default 0.80
- **MET_PCT = decimal** — met threshold, default 1.00
- **SHOW_GOAL = ON|OFF** — show target value text
- **SHOW_PERCENT_OF_GOAL = ON|OFF** — show percent-to-target text
- **SHOW_PROGRESS = ON|OFF** — show a progress indicator
- **PROGRESS_STYLE = BAR|RING** — progress style, default BAR
- **COLOR_MET = CSS color** — status colour when goal is met
- **COLOR_CLOSE = CSS color** — status colour when close to goal
- **COLOR_MISSED = CSS color** — status colour when goal is missed
- **ICON_SET = CHECKS|ARROWS|TRAFFIC** — preset status badge icon family
- **ICON_MET = 'text'** — custom met icon
- **ICON_CLOSE = 'text'** — custom close icon
- **ICON_MISSED = 'text'** — custom missed icon
- **LABEL_MET = 'text'** — status label override when met
- **LABEL_CLOSE = 'text'** — status label override when close
- **LABEL_MISSED = 'text'** — status label override when missed
- **TREND_DIR = POSITIVE_UP|POSITIVE_DOWN** — favourable delta direction
- **DELTA_FORMAT = '.NET format string'** — format for the delta display
- **DELTA_LABEL = 'text'** — label shown next to the delta (static form)

## Conditional Formatting

Use `FORMATTING (WHEN condition THEN 'css-color')` to apply threshold-driven colors to the primary `VALUE` metric:

```sql
CREATE VISUAL SalesKPI AS CARD (
  SOURCE = #sales_summary,
  MAPPINGS (VALUE = Revenue),
  FORMATTING (
    WHEN VALUE >= 1000000 THEN '#10B981',
    WHEN VALUE < 1000000  THEN '#EF4444'
  )
);
```

## Examples

```sql
SELECT
    SUM(revenue)                                       AS revenue,
    SUM(revenue) - LAG(SUM(revenue), 1) OVER ()        AS delta,
    1000000                                            AS goal,
    'Total Revenue'                                    AS label
INTO #kpi FROM #sales;

CREATE VISUAL RevKPI AS CARD (
  SOURCE   = #kpi,
  MAPPINGS (VALUE = revenue, LABEL = label, GOAL = goal, DELTA = delta),
  OPTIONS  (
    FORMAT               = 'C0',
    ABBREVIATE           = ON,
    SHOW_GOAL            = ON,
    SHOW_PERCENT_OF_GOAL = ON,
    SHOW_PROGRESS        = ON,
    PROGRESS_STYLE       = RING,
    ICON_SET             = CHECKS,
    DELTA_LABEL          = 'vs prior period'
  )
);
```

### Native Card Sparkline

```sql
SELECT 'Mon' AS day, 10 AS amount INTO #daily;
INSERT INTO #daily (day, amount) VALUES ('Tue', 14), ('Wed', 12);

CREATE VISUAL Revenue AS CARD (
  SOURCE = #kpi,
  MAPPINGS (
    VALUE = revenue,
    LABEL = label,
    SPARKLINE = #daily (X = day, Y = amount, TYPE = AREA)
  )
);
```

Multiple KPI cards side-by-side (use a PAGE STRUCTURE grid):
```sql
SELECT COUNT(DISTINCT customer_id) AS customers, 'Customers' AS label INTO #c FROM #sales;
SELECT AVG(order_value) AS avg_order, 'Avg Order' AS label          INTO #a FROM #sales;

CREATE VISUAL CustomerCount AS CARD (SOURCE=#c, MAPPINGS(VALUE=customers, LABEL=label));
CREATE VISUAL AvgOrder      AS CARD (SOURCE=#a, MAPPINGS(VALUE=avg_order,  LABEL=label),
                                     OPTIONS(FORMAT='C2'));

CREATE PAGE Summary AS DASHBOARD (
  STRUCTURE = 'A B',
  MAP ('A' = CustomerCount, 'B' = AvgOrder)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
