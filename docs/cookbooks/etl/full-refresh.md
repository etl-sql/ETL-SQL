# Full Refresh (Truncate & Reload)
<!-- description: **▶ Runs as-is.** Wipe the target and reload it from source, with a guard that refuses to truncate when the source comes back empty. For small reference tables. -->
The simplest load strategy — wipe the target and reload completely from source. Appropriate for small reference/dimension tables where CDC is overkill and a full copy is fast enough.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. The built-in `MOCKDB()` products stand in for the source, and `#dim_product` for the warehouse dimension:

```sql
CREATE CONNECTION demo AS MOCKDB();

-- Target dimension with stale content from a previous load
CREATE TABLE #dim_product (ProductID INT, ProductName VARCHAR(100), Category VARCHAR(50), Price DECIMAL(18,2));
INSERT INTO #dim_product VALUES (1, 'Retired item', 'Legacy', 0);

-- 1. Pull the full source table
SELECT ProductID, ProductName, Category, Price
INTO #full_load
FROM demo.Products
WHERE Discontinued = 0;

-- 2. Refuse to wipe the target if the source came back empty
DECLARE @SourceCount INT = (SELECT COUNT(*) FROM #full_load);
IF @SourceCount = 0
    THROW 'Source returned 0 rows — aborting to protect destination data.';

-- 3. Truncate and reload
TRUNCATE TABLE #dim_product;
INSERT INTO #dim_product (ProductID, ProductName, Category, Price)
SELECT ProductID, ProductName, Category, Price FROM #full_load;

PRINT 'Full refresh complete: ' + CAST(@SourceCount AS STRING) + ' rows loaded.';
SELECT Category, COUNT(*) AS Products FROM #dim_product GROUP BY Category ORDER BY Category;
```

The stale `Retired item` row is gone and the active products are loaded. Change the `WHERE` to `Discontinued = 2` to see the empty-source guard stop the run before the truncate. The production version wraps the reload in a transaction:

## Production Version

**Pattern Scenario:** Nightly full reload of a product reference table.

```sql
CREATE CONNECTION src  AS POSTGRES(HOST='pg01', DATABASE='Products', USER='etl', PASSWORD='...');
CREATE CONNECTION dest AS MSSQL(SERVER='dw01', DATABASE='Warehouse', TRUSTED_CONNECTION=TRUE);

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1. Pull the full source table
    SELECT ProductId, Sku, Name, Category, Price, IsActive
    INTO #full_load
    FROM src.products
    WHERE IsActive = 1;

    -- 2. Validate we got something before destroying destination
    DECLARE @SourceCount INT = (SELECT COUNT(*) FROM #full_load);
    IF @SourceCount = 0
        THROW 'Source returned 0 rows — aborting to protect destination data.';

    -- 3. Truncate and reload atomically inside a transaction
    TRUNCATE TABLE dest.dbo.DimProduct;
    INSERT INTO dest.dbo.DimProduct SELECT * FROM #full_load;

    COMMIT;
    PRINT 'Full refresh complete: ' + CAST(@SourceCount AS STRING) + ' rows loaded.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'Full refresh failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```
