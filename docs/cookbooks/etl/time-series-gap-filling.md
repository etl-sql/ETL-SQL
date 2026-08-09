# Time Series Gap Filling (FILL_DATES)
When building reporting dashboards (e.g. daily sales charts), missing dates in the raw transaction log will cause dates to be skipped on the chart axis. The `TRANSFORM ... USING FILL_DATES` statement solves this by materializing the missing date rows per group in-memory and filling them with a default gap value (e.g. `0`).

**Pattern Scenario:** Extract regional sales transactions, stage them, fill missing dates per region, and load the continuous daily series into a postgreSQL reporting warehouse.

```sql
-- 1. Infrastructure Connections
CREATE CONNECTION pg_src  AS POSTGRES(HOST='prod-pg', DATABASE='Sales', USER='read', PASSWORD='...');
CREATE CONNECTION pg_dest AS POSTGRES(HOST='warehouse-pg', DATABASE='Reporting', USER='loader', PASSWORD='...');

BEGIN TRY
    -- 2. Extract raw transaction counts by date and region into a temp table
    SELECT 
        CAST(OrderDate AS DATE) AS OrderDate,
        Region,
        COUNT(OrderId) AS TransactionCount
    INTO #raw_daily_sales
    FROM pg_src.Orders
    WHERE OrderDate >= DATEADD(DAY, -30, GETDATE())
    GROUP BY CAST(OrderDate AS DATE), Region;

    -- 3. Transform to fill gaps (if a region had 0 sales on a Tuesday, materialize that row)
    TRANSFORM #filled_daily_sales
    FROM #raw_daily_sales
    USING FILL_DATES (
        DATE_COL = 'OrderDate',
        GAPS_FILL = 0,
        BY_GROUP = 'Region'
    );

    -- 4. Load the continuous daily sales series into the reporting target
    TRUNCATE TABLE pg_dest.dbo.DailySalesSummary;
    
    INSERT INTO pg_dest.dbo.DailySalesSummary (OrderDate, Region, TransactionCount)
    SELECT OrderDate, Region, TransactionCount
    FROM #filled_daily_sales;

    PRINT 'Daily sales gap-filling complete.';
END TRY
BEGIN CATCH
    PRINT 'Gap filling failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```
