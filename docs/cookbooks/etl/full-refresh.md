# Full Refresh (Truncate & Reload)
The simplest load strategy — wipe the target and reload completely from source. Appropriate for small reference/dimension tables where CDC is overkill and a full copy is fast enough.

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
