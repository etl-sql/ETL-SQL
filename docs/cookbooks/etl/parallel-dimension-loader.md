# The Parallel Dimension Loader
Optimizes runtime by loading independent, non-conflicting dimension tables simultaneously.

**Pattern Scenario:** High-volume refresh of data warehouse dimensions.

```sql
PARALLEL
BEGIN
    -- Branch 1: Geography
    BEGIN
        SELECT * INTO #DimGeo FROM pg.Geography;
        INSERT INTO dw.DimGeography SELECT * FROM #DimGeo;
    END

    -- Branch 2: Products
    BEGIN
        SELECT * INTO #DimProd FROM pg.Products;
        INSERT INTO dw.DimProduct SELECT * FROM #DimProd;
    END

    -- Branch 3: Currency Rates
    BEGIN
        SELECT * INTO #DimCurr FROM rates_api.Rates;
        INSERT INTO dw.DimCurrency SELECT * FROM #DimCurr;
    END
END;

PRINT 'All dimensions refreshed.';
```

> [!TIP]
> Each branch in `PARALLEL` should write to a **unique** `#temp` table name. Branches sharing a temp table name will produce undefined results.
