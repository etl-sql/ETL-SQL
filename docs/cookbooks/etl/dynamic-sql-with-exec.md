# Dynamic SQL with EXEC
Build and execute SQL statements at runtime — essential for parameterized table names, dynamic column lists, and multi-tenant pipelines where the schema varies per client.

**Pattern Scenario:** Archive orders for each tenant into their own table.

```sql
CREATE CONNECTION orders_db AS MSSQL(SERVER='multi-db', DATABASE='Orders', TRUSTED_CONNECTION=TRUE);

DECLARE @Tenants LIST = (SELECT DISTINCT TenantId FROM orders_db.dbo.Orders);

FOREACH @Tenant IN @Tenants
BEGIN
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
EXEC @LocalSql;   -- No ON clause = runs locally against engine temp tables
```

> [!IMPORTANT]
> `EXEC sql_string ON connection` executes against a remote database. `EXEC sql_string` (no `ON`) parses and runs the string locally in engine context — able to access `#temp` tables and `@variables`. Both forms support `INTO #temp` to capture results.
