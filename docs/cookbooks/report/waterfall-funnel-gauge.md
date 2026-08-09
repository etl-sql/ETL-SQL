# Financial Waterfall, Funnel & Gauge

**Pattern**: Three financial visuals on one page — a cash-flow waterfall, a sales conversion funnel, and a KPI gauge showing actuals vs target.

**Demonstrates**: `WATERFALL`, `FUNNEL`, `GAUGE`, `MIN`/`MAX` options on GAUGE, `COLORS` for waterfall bars.

```sql
SET REPORT TITLE = 'Financial Performance';

-- ── Inline sample data ────────────────────────────────────────────────────

-- Cash flow waterfall (positive = inflow, negative = outflow)
SELECT 'Starting Balance' AS period, 50000 AS delta INTO #cashflow
UNION ALL SELECT 'Product Sales',    120000
UNION ALL SELECT 'Service Revenue',   35000
UNION ALL SELECT 'COGS',            -68000
UNION ALL SELECT 'Salaries',        -45000
UNION ALL SELECT 'Marketing',       -18000
UNION ALL SELECT 'Office / IT',      -9000
UNION ALL SELECT 'Ending Balance',       0;  -- zero: waterfall total is implicit

-- Sales funnel stages
SELECT 'Leads'       AS stage, 1200 AS count INTO #funnel
UNION ALL SELECT 'Qualified',  480
UNION ALL SELECT 'Demo',       210
UNION ALL SELECT 'Proposal',   130
UNION ALL SELECT 'Closed Won',  68;

-- Revenue vs target (for gauge)
SELECT 283000 AS actual, 300000 AS target, 'Revenue vs Target' AS label INTO #gauge_data;

-- ── Waterfall ─────────────────────────────────────────────────────────────
CREATE VISUAL CashFlow AS WATERFALL (
  SOURCE   = (SELECT period, delta FROM #cashflow),
  TITLE    = 'Cash Flow Statement',
  MAPPINGS (X = period, Y = delta),
  OPTIONS  (
    COLORS (positive = '#28a745', negative = '#dc3545')
  )
);

-- ── Funnel ────────────────────────────────────────────────────────────────
CREATE VISUAL SalesFunnel AS FUNNEL (
  SOURCE   = (SELECT stage, count FROM #funnel ORDER BY count DESC),
  TITLE    = 'Sales Conversion Funnel',
  MAPPINGS (LABEL = stage, VALUE = count)
);

-- ── Gauge ─────────────────────────────────────────────────────────────────
CREATE VISUAL RevenueGauge AS GAUGE (
  SOURCE   = (SELECT actual AS val, target AS mx, label FROM #gauge_data),
  TITLE    = 'Revenue Attainment',
  MAPPINGS (VALUE = val, MAX = mx, LABEL = label),
  OPTIONS  (MIN = 0)
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Financial AS DASHBOARD (
  STRUCTURE = 'A A B / A A C',
  MAP (
    'A' = CashFlow,
    'B' = SalesFunnel,
    'C' = RevenueGauge
  )
);
```
