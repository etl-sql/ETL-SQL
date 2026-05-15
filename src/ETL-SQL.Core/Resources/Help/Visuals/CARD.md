Type: CARD
A prominent KPI tile showing a single large number with an optional label, trend indicator, and goal comparison. Ideal for dashboard headlines.

Mappings:
  VALUE   — the primary metric (required); displayed large
  LABEL   — subtitle text below the number (optional)
  GOAL    — target value; engine computes % of goal and renders a progress indicator
  DELTA   — change vs. prior period; shown with an up/down arrow and colour coding

Options:
  FORMAT  = '.NET format string'  — e.g. 'N0', 'C2', 'P1' (default auto)
  COLORS  = ('positive_color', 'negative_color')  — colours for positive/negative DELTA
  TITLE   = 'card title'

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
  OPTIONS  (FORMAT = 'C0', TITLE = 'Revenue')
);
```

Multiple KPI cards side-by-side (use a PAGE STRUCTURE grid):
```sql
SELECT COUNT(DISTINCT customer_id) AS customers, 'Customers' AS label INTO #c FROM #sales;
SELECT AVG(order_value) AS avg_order, 'Avg Order' AS label          INTO #a FROM #sales;

CREATE VISUAL CustomerCount AS CARD (SOURCE=#c, MAPPINGS(VALUE=customers, LABEL=label));
CREATE VISUAL AvgOrder      AS CARD (SOURCE=#a, MAPPINGS(VALUE=avg_order,  LABEL=label),
                                     OPTIONS(FORMAT='C2'));

CREATE PAGE Summary AS (
  STRUCTURE = 'A B',
  MAP ('A' = CustomerCount, 'B' = AvgOrder)
);
```
