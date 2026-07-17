# Procedures and Functions


### 12.1 `CREATE PROCEDURE`
```sql
CREATE PROCEDURE ArchiveSales @olderThan DATE
AS
BEGIN
    INSERT INTO archive.sales SELECT * FROM prod.sales WHERE created_at < @olderThan;
    DELETE FROM prod.sales WHERE created_at < @olderThan;
END;

EXEC ArchiveSales '2025-01-01';
```

### 12.2 `CREATE FUNCTION`
```sql
CREATE FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL
AS
BEGIN
    RETURN @amount * 0.15;
END;

SELECT id, CalculateTax(price) AS Tax FROM #sales;
```

### 12.3 `CREATE OR ALTER` / `DROP`
```sql
CREATE OR ALTER PROCEDURE ArchiveSales @olderThan DATE AS BEGIN ... END;
CREATE OR ALTER FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL AS BEGIN ... END;

DROP FUNCTION  IF EXISTS CalculateTax;
DROP PROCEDURE IF EXISTS ArchiveSales;
```

### 12.4 `CREATE VIEW`

Views are session-scoped query aliases. They store a query definition and evaluate it every time the view is referenced. They do not materialize rows; use `SELECT ... INTO #temp` or `CREATE DATASET` when you need stored results.

```sql
CREATE VIEW ActiveCustomers AS
SELECT id, name, region
FROM #customers
WHERE active = 1;

SELECT * FROM ActiveCustomers WHERE region = 'West';

ALTER VIEW ActiveCustomers AS
SELECT id, name, region, status
FROM #customers
WHERE active = 1;

CREATE OR ALTER VIEW ActiveCustomers AS
SELECT id, name, region
FROM #customers
WHERE active = 1;

SHOW VIEWS INTO #views;
DROP VIEW IF EXISTS ActiveCustomers;
```

Rules:
- Views are read-only and cannot be used as `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, or `SELECT INTO` targets.
- Views are resolved in the engine, not created in a remote database. Use `EXECUTE <conn> BEGIN CREATE VIEW ... END` for native database views.
- CTEs and local statement sources can shadow view names inside a statement.
- Direct or indirect recursive view references fail at execution time.

## References

- [Statement Reference](README.md)
- [Syntax Index](../../syntax-index.md)

