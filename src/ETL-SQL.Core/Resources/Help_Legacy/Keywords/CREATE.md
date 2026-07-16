# CREATE
Creates connections, temporary tables, indexes, or named sets.

## Variants

**CREATE CONNECTION** — register a named alias to an external system.
**CREATE TABLE** — define a persistent or in-session schema.
**CREATE INDEX** — add an index to a #temp table for join/filter performance.
**CREATE VIEW** — define a session-scoped query alias.
**CREATE SETS** — define a named, reusable list of values (see also: USE SETS).
**CREATE SHARE LINK** — create a portal report share link.
**CREATE EMBED TOKEN** — create a portal embed token.
**CREATE SAVED VIEW** — save portal report parameter values.
**CREATE ALERT** — create a portal report alert.
**CREATE SUBSCRIPTION** — schedule report delivery through the portal.

## Syntax
```sql
-- Connection
CREATE CONNECTION MyDB AS MSSQL (
  SERVER = 'sql01', DATABASE = 'Sales',
  USER = 'sa', PASSWORD = 'ENC:abc123=='
);

-- Temp table (explicit schema)
CREATE TABLE #staging (
  id INT, name STRING, loaded_at DATE
);

-- Index on temp table
CREATE INDEX idx_id ON #staging (id);

-- Named set
CREATE SETS !Regions BEGIN
  @North = 'North', @South = 'South', @East = 'East', @West = 'West'
END;

-- Query view
CREATE VIEW ActiveOrders AS
SELECT order_id, customer_id, amount
FROM #orders
WHERE status = 'Active';

EXECUTE portal BEGIN
  CREATE SHARE LINK FOR REPORT 'Daily Sales' EXPIRES '2026-12-31T23:59:59Z' INTO #link;
  CREATE EMBED TOKEN FOR REPORT 'Operations' NAME 'Ops wallboard' INTO #embed;
  CREATE SAVED VIEW 'EMEA' FOR REPORT 'Daily Sales' PARAMETERS (@region = 'EMEA');
  CREATE ALERT 'HighFailures' FOR REPORT 'Operations' WHEN VISUAL 'Failures' > 10;
END;
```

## Notes
- Connection types: MSSQL, POSTGRES, MYSQL, SQLITE, ORACLE, FLATFILE, SFTP, S3, SMTP, API, SNOWFLAKE, BIGQUERY, REDSHIFT, ODBC.
- `ENC:` prefix marks an encrypted credential value decrypted at connect time.
- `CREATE TABLE` is optional when using `SELECT ... INTO #table` — the schema is inferred automatically.
- `CREATE VIEW` stores a query definition only; rows are evaluated when the view is selected.
- Portal administration variants require `EXECUTE <reportportal-connection> BEGIN ... END`.
- See: DROP, USE SETS, ENCRYPT

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)

