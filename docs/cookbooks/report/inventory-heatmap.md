# Inventory Heatmap & Low-Stock Alerts

**Pattern**: A warehouse bin heatmap shows stock intensity at a glance. A table below highlights items below reorder point in red. A MULTISELECT filters by category.

**Demonstrates**: `HEATMAP`, `MULTISELECT`, `FORMATTING`, multi-value parameter pattern.

```sql
SET REPORT TITLE = 'Warehouse Inventory Monitor';
DECLARE @category VARCHAR INPUT = 'All';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'A' AS bin, 'Shelf 1' AS shelf, 'Electronics' AS category, 'Widget Pro'  AS item, 45 AS qty, 20 AS reorder INTO #inv
UNION ALL SELECT 'A', 'Shelf 2', 'Electronics', 'Widget Lite', 8,  15
UNION ALL SELECT 'A', 'Shelf 3', 'Accessories', 'Cable Pack',  62, 25
UNION ALL SELECT 'B', 'Shelf 1', 'Electronics', 'Adapter',     3,  10
UNION ALL SELECT 'B', 'Shelf 2', 'Accessories', 'Mount Kit',   28, 20
UNION ALL SELECT 'B', 'Shelf 3', 'Electronics', 'Charger',     17, 15
UNION ALL SELECT 'C', 'Shelf 1', 'Accessories', 'Case',        55, 30
UNION ALL SELECT 'C', 'Shelf 2', 'Electronics', 'Screen',      6,  12
UNION ALL SELECT 'C', 'Shelf 3', 'Accessories', 'Stand',       41, 20
UNION ALL SELECT 'D', 'Shelf 1', 'Electronics', 'Sensor',      2,  10
UNION ALL SELECT 'D', 'Shelf 2', 'Accessories', 'Hub',         19, 15
UNION ALL SELECT 'D', 'Shelf 3', 'Electronics', 'Dock',        33, 20;

-- ── Category filter ───────────────────────────────────────────────────────
CREATE VISUAL CategoryFilter AS MULTISELECT (
  SOURCE   = (SELECT DISTINCT category FROM #inv ORDER BY category),
  MAPPINGS (VALUE = category),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@category, category))
);

-- ── Heatmap: bin × shelf intensity ───────────────────────────────────────
CREATE VISUAL StockHeatmap AS HEATMAP (
  SOURCE   = (SELECT bin, shelf, SUM(qty) AS qty FROM #inv
              WHERE @category = 'All' OR category = @category
              GROUP BY bin, shelf),
  TITLE    = 'Stock Level by Bin & Shelf',
  MAPPINGS (X = bin, Y = shelf, VALUE = qty)
);

-- ── Donut: stock by category ──────────────────────────────────────────────
CREATE VISUAL CategoryShare AS DONUT (
  SOURCE   = (SELECT category, SUM(qty) AS qty FROM #inv
              WHERE @category = 'All' OR category = @category
              GROUP BY category),
  TITLE    = 'Stock by Category',
  MAPPINGS (LABEL = category, VALUE = qty)
);

-- ── Low-stock alert table ─────────────────────────────────────────────────
CREATE VISUAL LowStockTable AS TABLE (
  SOURCE = (SELECT item, bin, shelf, category, qty, reorder,
                   reorder - qty AS shortfall
            FROM #inv
            WHERE qty < reorder
              AND (@category = 'All' OR category = @category)
            ORDER BY shortfall DESC),
  FORMATTING (
    shortfall >= 10 THEN '#f8d7da',
    shortfall > 0   THEN '#fff3cd'
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Inventory AS DASHBOARD (
  STRUCTURE = 'A B C / D D D',
  MAP (
    'A' = StockHeatmap,
    'B' = CategoryShare,
    'C' = CategoryFilter,
    'D' = LowStockTable
  )
);
```
