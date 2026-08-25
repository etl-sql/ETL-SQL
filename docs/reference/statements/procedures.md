# Procedures and Functions

ETL-SQL supports modular scripting through user-defined stored procedures, scalar functions, and session-scoped views.

---

## `CREATE PROCEDURE`

Defines a reusable procedural block with input/output parameters:

## Syntax

```sql
CREATE PROCEDURE ArchiveSales @olderThan DATE
AS
BEGIN
    INSERT INTO archive.sales SELECT * FROM prod.sales WHERE created_at < @olderThan;
    DELETE FROM prod.sales WHERE created_at < @olderThan;
END;

EXEC ArchiveSales '2025-01-01';
```

---

## `CREATE FUNCTION`

Defines a reusable scalar function that accepts parameters and returns a single value:

```sql
CREATE FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL
AS
BEGIN
    RETURN @amount * 0.15;
END;

SELECT id, CalculateTax(price) AS Tax FROM #sales;
```

---

## `CREATE OR ALTER` / `DROP`

Idempotent creation, modification, and deletion of procedures and functions:

```sql
CREATE OR ALTER PROCEDURE ArchiveSales @olderThan DATE AS BEGIN ... END;
CREATE OR ALTER FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL AS BEGIN ... END;

DROP FUNCTION  IF EXISTS CalculateTax;
DROP PROCEDURE IF EXISTS ArchiveSales;
```

---

## `CREATE VIEW`

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

SELECT * INTO #views FROM eng.views;
DROP VIEW IF EXISTS ActiveCustomers;
```

### Rules & Behavior
- Views are read-only and cannot be used as `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, or `SELECT INTO` targets.
- Views are resolved in the engine, not created in a remote database. Use `EXECUTE <conn> BEGIN CREATE VIEW ... END` for native database views.
- CTEs and local statement sources can shadow view names inside a statement.
- Direct or indirect recursive view references fail at execution time.

---

## References

- [Statement Reference](README.md)
- [Lifecycle Capability Matrix](lifecycle-matrix.md)
- [Execution Blocks](execution-blocks.md)
- [Syntax Index](../../syntax-index.md)

## Examples

```sql
RUN SCRIPT 'scripts/daily_etl.etlsql';
```
