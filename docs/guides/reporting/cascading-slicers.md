# Cascading Slicers and Atomic Parameters

Cascading slicers create hierarchical filter workflows where the available options in a child control depend on the values selected in one or more parent controls (e.g., *Country* → *State* → *City*).

Report-SQL processes parameter cascades **atomically**: all dependent visuals and child slicers update in a single transaction, preventing inconsistent intermediate states or partial dashboard refreshes.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Cascade Modes: LOCAL vs. LIVE

Report-SQL supports two cascade execution modes:

| Mode | Behavior | Best Used When... |
| :--- | :--- | :--- |
| `MODE = LOCAL` | Filters the child control's in-memory option vector using `PARENTS`. Works offline and in static `.etlsnap` snapshots. | Datasets are small-to-medium and options are pre-loaded into memory. |
| `MODE = LIVE` | Re-executes the child control's inline `SOURCE = (SELECT ...)` query against the server whenever parent parameters change. | Option lists are massive or live in a remote database. |

### Cascade Configuration Options

- **`PARENTS (@ParentVar = ParentCol, ...)`**: Maps parent parameter variables to columns in the child's source table (`MODE = LOCAL` only).
- **`INVALID = CLEAR | FIRST | ERROR`**: Dictates what happens if a child's current value becomes invalid when its parent changes:
  - `CLEAR` (default): Resets the child control to NULL or its default value.
  - `FIRST`: Automatically selects the first valid child option.
  - `ERROR`: Rejects the parent change and rolls back the parameter transaction.
- **`NULL = ALL`**: Treats NULL or empty parent selections as "select all" (unfiltered child options).
- **`MULTISELECT = ANY | ALL`**: For multi-parent combinations, matches child rows where any or all parent conditions hold.

---

## Example 1: Two-Tier Local Cascading Hierarchy (Region → Country)

This example sets up a Region slicer and a Country slicer using `MODE = LOCAL`. Changing the region instantly filters the countries available in the child dropdown.

```sql
SET REPORT TITLE = 'Global Operations Hierarchy';

DECLARE @region  VARCHAR INPUT = 'All';
DECLARE @country VARCHAR INPUT = 'All';

CREATE CONNECTION db AS MOCKDB();

SELECT RegionCode, CountryCode, CountryName, SalesAmount
INTO #geo_sales
FROM db.GeoSales;

-- Parent Slicer: Region
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT RegionCode FROM #geo_sales ORDER BY RegionCode),
  MAPPINGS (VALUE = RegionCode),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, RegionCode))
);

-- Child Slicer: Country (Cascades from @region)
CREATE VISUAL CountryFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT RegionCode, CountryCode, CountryName FROM #geo_sales),
  MAPPINGS (VALUE = CountryCode, LABEL = CountryName),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@country, CountryCode)),
  CASCADE (
    MODE    = LOCAL,
    PARENTS (@region = RegionCode),
    INVALID = CLEAR,
    NULL    = ALL
  )
);

-- Dependent Visual: Result table
CREATE VISUAL SalesTable AS TABLE (
  SOURCE = (SELECT CountryName, SUM(SalesAmount) AS TotalSales
            FROM #geo_sales
            WHERE (@region = 'All' OR RegionCode = @region)
              AND (@country = 'All' OR CountryCode = @country)
            GROUP BY CountryName)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A B / C C',
    MAP (
      'A' = RegionFilter,
      'B' = CountryFilter,
      'C' = SalesTable
    )
  )
);
```

---

## Example 2: Three-Tier Multi-Parent Cascade (Region & Segment → City)

Child controls can depend on multiple parent controls simultaneously. In this example, the City dropdown is filtered by both `@region` and `@segment`.

```sql
SET REPORT TITLE = 'Targeted Market Analysis';

DECLARE @region  VARCHAR INPUT = 'All';
DECLARE @segment VARCHAR INPUT = 'All';
DECLARE @city    VARCHAR INPUT = 'All';

CREATE CONNECTION db AS MOCKDB();

SELECT RegionCode, SegmentCode, CityCode, CityName, Revenue
INTO #market_data
FROM db.MarketData;

CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT RegionCode FROM #market_data),
  MAPPINGS (VALUE = RegionCode),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, RegionCode))
);

CREATE VISUAL SegmentFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT SegmentCode FROM #market_data),
  MAPPINGS (VALUE = SegmentCode),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@segment, SegmentCode))
);

-- Multi-parent cascade
CREATE VISUAL CityFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT RegionCode, SegmentCode, CityCode, CityName FROM #market_data),
  MAPPINGS (VALUE = CityCode, LABEL = CityName),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@city, CityCode)),
  CASCADE (
    MODE    = LOCAL,
    PARENTS (@region = RegionCode, @segment = SegmentCode),
    INVALID = CLEAR,
    NULL    = ALL
  )
);

CREATE VISUAL MetricCard AS CARD (
  SOURCE = (SELECT SUM(Revenue) AS Total, 'Market Revenue' AS Lbl
            FROM #market_data
            WHERE (@region = 'All' OR RegionCode = @region)
              AND (@segment = 'All' OR SegmentCode = @segment)
              AND (@city = 'All' OR CityCode = @city)),
  MAPPINGS (VALUE = Total, LABEL = Lbl)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A B C / D D D',
    MAP (
      'A' = RegionFilter,
      'B' = SegmentFilter,
      'C' = CityFilter,
      'D' = MetricCard
    )
  )
);
```

---

## Example 3: Live Query Cascade (`MODE = LIVE`)

For large databases where option lists are too large to hold in memory, use `MODE = LIVE`. In live mode, dependencies are inferred from the parameters in the inline query, so the `PARENTS` clause is omitted.

```sql
SET REPORT TITLE = 'Large Scale Live Catalog';

DECLARE @category VARCHAR INPUT = 'Electronics';
DECLARE @product  VARCHAR INPUT = 'All';

CREATE CONNECTION db AS MOCKDB();

SELECT Category, ProductId, ProductName, Stock
INTO #products
FROM db.Inventory;

CREATE VISUAL CategoryFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT Category FROM #products ORDER BY Category),
  MAPPINGS (VALUE = Category),
  DEFAULT  = 'Electronics',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@category, Category))
);

-- LIVE mode: query runs dynamically with current @category
CREATE VISUAL ProductFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT ProductId, ProductName 
              FROM #products 
              WHERE Category = @category 
              ORDER BY ProductName),
  MAPPINGS (VALUE = ProductId, LABEL = ProductName),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@product, ProductId)),
  CASCADE (
    MODE    = LIVE,
    INVALID = FIRST
  )
);

CREATE VISUAL StockCard AS CARD (
  SOURCE = (SELECT SUM(Stock) AS TotalStock, 'Units Available' AS Lbl
            FROM #products
            WHERE Category = @category
              AND (@product = 'All' OR ProductId = @product)),
  MAPPINGS (VALUE = TotalStock, LABEL = Lbl)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A B / C C',
    MAP (
      'A' = CategoryFilter,
      'B' = ProductFilter,
      'C' = StockCard
    )
  )
);
```

---

## Common Pitfalls

- **Specifying `PARENTS` in `MODE = LIVE`**: In `MODE = LIVE`, parent dependencies are parsed directly from the inline SQL query; providing a `PARENTS (...)` clause is redundant and rejected by the linter.
- **Circular dependencies**: Defining Control A to cascade from Control B, and Control B to cascade from Control A produces a static validation error before execution.
- **Missing parent columns in LOCAL source**: In `MODE = LOCAL`, the child slicer's `SOURCE` dataset must project all columns referenced in the `PARENTS` mapping.

---

## Related Topics

- [Report Parameters and Filters](report-parameters-and-filters.md) — Base parameter declarations and control bindings.
- [Authoring Dashboards](authoring-dashboards.md) — Layout grid and 3-tier architecture.
- [CASCADE Reference](../../reference/visuals-reporting/report/cascade.md) — Full grammar and option specification.
