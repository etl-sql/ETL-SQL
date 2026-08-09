# Master-Detail Cross-Report Drill-through
The most powerful interactive pattern. It allows navigating from a high-level summary report to a completely separate, detailed report file while passing context (like a Region or Order ID) via parameters.

**Pattern Scenario:** High-level Sales Summary → Detailed Regional Transaction Log.

### Summary Report (`summary.rptsql`)
```sql
-- 1. Data Source
CREATE DATASET &SalesSummary AS (
    SELECT Region, SUM(Sales) AS TotalSales FROM #raw GROUP BY Region
);

-- 2. Master Visual
CREATE VISUAL RegionTable AS TABLE (
    SOURCE = &SalesSummary,
    MAPPINGS (COLUMN Region = Region, COLUMN Sales = TotalSales),
    ACTIONS (
        -- Cross-report drill
        ON_CLICK = DRILL_REPORT (
            REPORT = 'SalesDetail',  -- Name defined in reports.json
            PARAMETERS ( @TargetRegion = Region )
        )
    )
);

CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = RegionTable));
```

### Detail Report (`regional_detail.rptsql`)
```sql
-- 1. Declare the input parameter
DECLARE @TargetRegion STRING INPUT = 'All';

-- 2. Data Source filtered by input
CREATE DATASET &Transactions AS (
    SELECT * FROM #all_tx WHERE Region = @TargetRegion OR @TargetRegion = 'All'
);

-- 3. Detail Visual
CREATE VISUAL TxTable AS TABLE (
    SOURCE = &Transactions,
    TITLE  = ('Transactions for: ' + @TargetRegion)
);

CREATE PAGE Main AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = TxTable));
```
