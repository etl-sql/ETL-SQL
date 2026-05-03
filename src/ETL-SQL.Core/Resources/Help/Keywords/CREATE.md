# CREATE
Creates connections, temporary tables, indexes, or named sets.

## Variants

**CREATE CONNECTION** — register a named alias to an external system.
**CREATE TABLE** — define a persistent or in-session schema.
**CREATE INDEX** — add an index to a #temp table for join/filter performance.
**CREATE SETS** — define a named, reusable list of values (see also: USE SETS).

## Syntax
```sql
-- Connection
CREATE CONNECTION MyDB AS MSSQL (
  SERVER = 'sql01', DATABASE = 'Sales',
  USERNAME = 'sa', PASSWORD = ENC:abc123==
);

-- Temp table (explicit schema)
CREATE TABLE #staging (
  id INT, name STRING, loaded_at DATE
);

-- Index on temp table
CREATE INDEX idx_id ON #staging (id);

-- Named set
CREATE SETS !Regions AS ('North', 'South', 'East', 'West');
```

## Notes
- Connection types: MSSQL, POSTGRES, MYSQL, SQLITE, ORACLE, FLATFILE, SFTP, S3, SMTP, API, SNOWFLAKE, BIGQUERY, REDSHIFT, ODBC.
- `ENC:` prefix marks an encrypted credential value decrypted at connect time.
- `CREATE TABLE` is optional when using `SELECT ... INTO #table` — the schema is inferred automatically.
- See: DROP, USE SETS, ENCRYPT