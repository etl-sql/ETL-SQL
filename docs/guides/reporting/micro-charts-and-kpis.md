# Micro-Charts and KPI Cards

Report-SQL supports native in-cell micro-charts (sparklines, spark-bars, and progress bars) inside `TABLE` visuals and standalone `CARD` components. These micro-visuals render as clean, lightweight vector SVG in web browsers and PDF exports without requiring server-side browser engines.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Micro-Chart Types

| Feature | Visual Type | Declaration Syntax |
| :--- | :--- | :--- |
| **Card Sparkline** | `CARD` | `SPARKLINE = #trend (X = colX, Y = colY, TYPE = LINE \| AREA \| BAR)` |
| **Table Sparkline** | `TABLE` | `SPARKLINE(col1, col2, col3, ...) LINE \| AREA \| BAR AS 'Header'` |
| **Progress Bar** | `TABLE` | `colName PROGRESS_BAR (MIN = n, MAX = n, COLOR = '#hex') AS 'Header'` |

---

## Example 1: KPI Card with Area Sparkline

This example displays a primary revenue figure alongside a 7-day trend sparkline inside a single KPI card.

```sql
SET REPORT TITLE = 'Executive KPI Overview';

CREATE CONNECTION db AS MOCKDB();

SELECT 'Revenue' AS Metric, 48500.00 AS CurrentTotal
INTO #metric_summary;

SELECT 'Mon' AS DayName, 5200 AS Amount INTO #daily_sales;
INSERT INTO #daily_sales (DayName, Amount) VALUES 
  ('Tue', 6400), ('Wed', 7100), ('Thu', 8300), ('Fri', 9200), ('Sat', 6900), ('Sun', 5400);

-- Card with embedded sparkline dataset
CREATE VISUAL RevenueCard AS CARD (
  SOURCE   = #metric_summary,
  TITLE    = 'Weekly Revenue',
  MAPPINGS (
    LABEL     = Metric,
    VALUE     = CurrentTotal,
    SPARKLINE = #daily_sales (X = DayName, Y = Amount, TYPE = AREA)
  ),
  OPTIONS  (FORMAT = 'C0')
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = RevenueCard)
  )
);
```

---

## Example 2: Table Scorecard with Sparklines and Progress Bars

Render monthly performance trends across multiple quarters and visualize quota attainment percentages directly within a data table.

```sql
SET REPORT TITLE = 'Team Performance Scorecard';

CREATE CONNECTION db AS MOCKDB();

-- Performance data with wide quarterly columns
SELECT 
  'Alpha' AS Team, 10500 AS Q1, 12200 AS Q2, 14800 AS Q3, 16100 AS Q4, 0.94 AS Attainment
INTO #team_goals;

INSERT INTO #team_goals (Team, Q1, Q2, Q3, Q4, Attainment) VALUES
  ('Bravo', 8900, 9400, 11200, 10800, 0.78),
  ('Charlie', 14200, 15800, 16900, 18500, 1.12);

-- Scorecard table with sparkline and progress bar mappings
CREATE VISUAL ScorecardTable AS TABLE (
  SOURCE = #team_goals,
  MAPPINGS (
    Team,
    SPARKLINE(Q1, Q2, Q3, Q4) LINE AS 'Quarterly Trend',
    Attainment PROGRESS_BAR (MIN = 0, MAX = 1.2, COLOR = '#16A34A') AS 'Quota Attainment'
  )
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = ScorecardTable)
  )
);
```

---

## Example 3: Multiple Metric Cards in a KPI Header Row

Combine multiple KPI cards into a top-level summary container using CSS grid layout.

```sql
SET REPORT TITLE = 'Operational Health';

CREATE CONNECTION db AS MOCKDB();

SELECT 'Active Users' AS Metric, 1250 AS Val INTO #users;
SELECT 'Avg Latency' AS Metric, 42 AS Val INTO #latency;
SELECT 'Error Rate' AS Metric, 0.002 AS Val INTO #errors;

CREATE VISUAL UserCard AS CARD (
  SOURCE = #users,
  MAPPINGS (VALUE = Val, LABEL = Metric),
  OPTIONS (FORMAT = 'N0')
);

CREATE VISUAL LatencyCard AS CARD (
  SOURCE = #latency,
  MAPPINGS (VALUE = Val, LABEL = Metric),
  OPTIONS (FORMAT = 'N0 ms')
);

CREATE VISUAL ErrorCard AS CARD (
  SOURCE = #errors,
  MAPPINGS (VALUE = Val, LABEL = Metric),
  OPTIONS (FORMAT = 'P2')
);

CREATE CONTAINER KpiRow AS BOX (
  LAYOUT (
    STRUCTURE = 'A B C',
    MAP (
      'A' = UserCard,
      'B' = LatencyCard,
      'C' = ErrorCard
    ),
    GAP = '16px'
  )
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = KpiRow)
  )
);
```

---

## Common Pitfalls

- **Narrow progress bar ranges**: If `Attainment` is `0.85` and `MAX` is omitted (defaulting to 100 instead of 1.0), the progress bar will only appear 0.85% filled. Ensure `MIN` and `MAX` match the scale of your numeric data.
- **Null values in wide sparklines**: Null values in column lists (e.g. `SPARKLINE(Q1, Q2, Q3, Q4)`) render as gaps in the sparkline. Use `COALESCE(Q3, 0)` in your source query if gaps should be treated as zero.

---

## Related Topics

- [Authoring Dashboards](authoring-dashboards.md) — 3-tier architecture and page layout.
- [CARD Visual Reference](../../reference/visuals-reporting/visuals/card.md) — Properties and formatting options.
- [TABLE Visual Reference](../../reference/visuals-reporting/visuals/table.md) — Summary rows, column formatting, and sorting.
