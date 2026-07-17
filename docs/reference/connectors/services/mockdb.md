# MOCKDB
An in-memory test database for development and unit-testing scripts without connecting to a live database. MOCKDB accepts all DDL and DML operations and discards its data when the session ends.

Syntax:
  CREATE CONNECTION <name> AS MOCKDB();

No options are required.

```sql
CREATE CONNECTION TestDB AS MOCKDB();

-- Create a table on the mock DB
CREATE TABLE TestDB.dbo.Orders (
  id     INT,
  amount DECIMAL(10,2)
);

INSERT INTO TestDB.dbo.Orders (id, amount) VALUES (1, 99.99), (2, 149.00);

SELECT id, amount INTO #test FROM TestDB.dbo.Orders;
PRINT 'Rows: ' + @@ROWCOUNT;
```

Use MOCKDB during script development to avoid modifying real databases. Switch to the real connection when ready by changing the CREATE CONNECTION statement.

References:
- [Data Connectors](../../../administration/platform/README.md)
