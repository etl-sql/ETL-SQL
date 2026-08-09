# Multi-Select + Search Filter Table

**Pattern**: A MULTISELECT narrows a TABLE to chosen categories; a SEARCH box further filters by text. Both controls update `@category` and `@search` independently.

**Demonstrates**: `MULTISELECT`, `SEARCH`, `SLIDER`, combined parameter filtering, typed parameters.

```sql
SET REPORT TITLE = 'Product Catalog Browser';
DECLARE @category  VARCHAR INPUT = 'All';
DECLARE @search    VARCHAR INPUT = '';
DECLARE @max_price DECIMAL INPUT = 500;

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Electronics' AS category, 'Widget Pro 2000' AS name, 299.99 AS price, 4.8 AS rating INTO #products
UNION ALL SELECT 'Electronics', 'Widget Lite',        149.99, 4.2
UNION ALL SELECT 'Electronics', 'Sensor Module',       79.99, 3.9
UNION ALL SELECT 'Electronics', 'Smart Dock',         199.99, 4.6
UNION ALL SELECT 'Accessories', 'Carry Case',          29.99, 4.5
UNION ALL SELECT 'Accessories', 'Mount Kit Pro',       44.99, 4.3
UNION ALL SELECT 'Accessories', 'USB-C Cable 3m',       9.99, 4.7
UNION ALL SELECT 'Accessories', 'Screen Protector',    14.99, 4.1
UNION ALL SELECT 'Software',    'Dashboard License',  499.00, 4.9
UNION ALL SELECT 'Software',    'Analytics Add-on',   199.00, 4.4
UNION ALL SELECT 'Software',    'Support Plan 1yr',   299.00, 4.8;

-- ── Filter controls ───────────────────────────────────────────────────────
CREATE VISUAL CategoryPicker AS MULTISELECT (
  SOURCE   = (SELECT DISTINCT category FROM #products ORDER BY category),
  MAPPINGS (VALUE = category),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@category, category))
);

CREATE VISUAL NameSearch AS SEARCH (
  TITLE   = 'Search by name...',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@search, value))
);

CREATE VISUAL MaxPrice AS SLIDER (
  TITLE   = 'Max Price',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@max_price, value)),
  OPTIONS (MIN = 0, MAX = 500)
);

-- ── Filtered product table ────────────────────────────────────────────────
CREATE VISUAL ProductTable AS TABLE (
  SOURCE = (
    SELECT category, name, price, rating
    FROM #products
    WHERE (@category = 'All' OR category = @category)
      AND (@search    = ''    OR name LIKE '%' + @search + '%')
      AND price <= @max_price
    ORDER BY category, price DESC
  ),
  FORMATTING (
    rating >= 4.7 THEN '#d4edda',
    rating < 4.0  THEN '#f8d7da'
  )
);

-- ── Bar: average price by category ───────────────────────────────────────
CREATE VISUAL PriceByCategory AS BAR (
  SOURCE   = (SELECT category, AVG(price) AS avg_price FROM #products
              WHERE @category = 'All' OR category = @category
              GROUP BY category),
  TITLE    = 'Average Price by Category',
  MAPPINGS (X = category, Y = avg_price),
  OPTIONS  (Y_AXIS (LABEL = 'Avg Price ($)', MIN = 0))
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Catalog AS DASHBOARD (
  STRUCTURE = 'A B C / D D E',
  MAP (
    'A' = CategoryPicker,
    'B' = NameSearch,
    'C' = MaxPrice,
    'D' = ProductTable,
    'E' = PriceByCategory
  )
);
```
