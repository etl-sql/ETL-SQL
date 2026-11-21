# Dynamic SQL with EXEC
<!-- description: **▶ Runs as-is.** Build and execute SQL statements at runtime — essential for parameterized table names, dynamic column lists, and multi-tenant pipelines where the schema varies per client. -->
Build and execute SQL statements at runtime — essential for parameterized table names, dynamic column lists, and multi-tenant pipelines where the schema varies per client.

## Try It Now (No Database Needed)

Paste into a `.etlsql` file and press **F5**. The built-in `MOCKDB()` sales stand in for the orders table; each region gets its own archive table, named at runtime:

```sql
CREATE CONNECTION demo AS MOCKDB();

CREATE TABLE #summary (Region VARCHAR(50), ArchiveTable VARCHAR(100), Orders INT);

-- One archive table per region, named at runtime
FOREACH @row IN (SELECT DISTINCT Region FROM demo.Sales)
BEGIN
    DECLARE @Region = @row.Region;
    -- 'North America' -> #Archive_North_America
    DECLARE @ArchiveTable = '#Archive_' + REPLACE(@Region, ' ', '_');

    -- Dynamic SELECT INTO: the table name is only known at runtime
    DECLARE @CopySql =
        'SELECT SaleID, OrderDate, Total INTO ' + @ArchiveTable +
        ' FROM demo.Sales WHERE Region = ''' + REPLACE(@Region, '''', '''''') + ''';';
    EXEC (@CopySql);

    -- Dynamic count from the table just created
    DECLARE @CountSql =
        'INSERT INTO #summary SELECT ''' + @Region + ''', ''' + @ArchiveTable + ''', COUNT(*) FROM ' + @ArchiveTable + ';';
    EXEC (@CountSql);

    PRINT 'Archived ' + @Region + ' into ' + @ArchiveTable;
END

SELECT Region, ArchiveTable, Orders FROM #summary ORDER BY Region;
```

Four archive tables are created, one per region, and the summary counts the rows in each. The `REPLACE` calls matter: one turns `North America` into a valid table name, the other doubles any quote inside a value so it cannot end the string literal early. The production version builds the same statements and runs them on the remote database with `AT`:

## Production Version

**Pattern Scenario:** Archive orders for each tenant into their own table.

```sql
CREATE CONNECTION orders_db AS MSSQL(SERVER='multi-db', DATABASE='Orders', TRUSTED_CONNECTION=TRUE);

FOREACH @row IN (SELECT DISTINCT TenantId FROM orders_db.dbo.Orders)
BEGIN
    DECLARE @Tenant = @row.TenantId;

    -- Build the archive table name dynamically
    DECLARE @ArchiveTable = 'Archive_' + @Tenant + '_Orders';
    DECLARE @ArchiveYear  = CAST(YEAR(GETDATE()) AS STRING);

    -- Dynamic DDL — create the archive table if it doesn't exist
    DECLARE @CreateSql = 
        'IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ''' + @ArchiveTable + ''') ' +
        'CREATE TABLE ' + @ArchiveTable + ' (OrderId INT, Amount DECIMAL(18,2), OrderDate DATETIME, ArchivedAt DATETIME);';

    EXEC (@CreateSql) AT orders_db;   -- Execute against the remote connection

    -- Dynamic INSERT — archive this tenant's old orders
    DECLARE @InsertSql =
        'INSERT INTO ' + @ArchiveTable + ' (OrderId, Amount, OrderDate, ArchivedAt) ' +
        'SELECT OrderId, Amount, OrderDate, GETDATE() ' +
        'FROM dbo.Orders ' +
        'WHERE TenantId = ''' + @Tenant + ''' AND YEAR(OrderDate) < ' + @ArchiveYear + ';';

    EXEC (@InsertSql) AT orders_db;

    PRINT 'Archived orders for tenant: ' + @Tenant;
END

-- Local dynamic execution example (runs in engine context, not remote)
DECLARE @LocalSql = 'SELECT COUNT(*) AS TotalArchived FROM #summary;';
EXEC (@LocalSql);   -- No AT clause = runs locally against engine temp tables
```

> [!IMPORTANT]
> `EXEC (sql_string) AT connection` executes against a remote database. `EXEC (sql_string)` (no `AT`) parses and runs the string locally in engine context — able to access `#temp` tables and `@variables`. Both forms support `INTO #temp` to capture results. Without the parentheses, `EXEC @name` calls a stored procedure named by the variable.
