# The Parallel Dimension Loader
<!-- description: **▶ Runs as-is.** Optimizes runtime by loading independent, non-conflicting dimension tables simultaneously. -->
Optimizes runtime by loading independent, non-conflicting dimension tables simultaneously.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. Three branches load three dimensions from the built-in `MOCKDB()` at the same time, each into its own `#temp` table:

```sql
CREATE CONNECTION demo AS MOCKDB();

PARALLEL
BEGIN
    -- Branch 1: Products
    BEGIN
        SELECT ProductID, ProductName, Category INTO #DimProduct FROM demo.Products;
    END

    -- Branch 2: Departments
    BEGIN
        SELECT DeptID, DeptName INTO #DimDepartment FROM demo.departments;
    END

    -- Branch 3: Employees
    BEGIN
        SELECT EmpID, Name, DeptID INTO #DimEmployee FROM demo.Employee;
    END
END;

PRINT 'All dimensions refreshed.';

SELECT 'DimProduct' AS Dimension, COUNT(*) AS LoadedRows FROM #DimProduct
UNION ALL SELECT 'DimDepartment', COUNT(*) FROM #DimDepartment
UNION ALL SELECT 'DimEmployee', COUNT(*) FROM #DimEmployee;
```

Each branch finishes before `PARALLEL` returns, so the counts after it see all three tables. The production version writes each branch on to the warehouse:

## Production Version

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
