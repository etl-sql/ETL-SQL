# DATASET
Defines a shared, optionally cached data source that can be used by multiple visuals or pages within a report. Datasets are evaluated once and stored; visuals reference them by name.

Syntax:
  CREATE DATASET &<name>
    [REFRESH EVERY '<interval>']
    [TTL = '<duration>']
    [ENCRYPT = MACHINE | PASSWORD | KEYFILE]
  AS (SELECT ...);

Options:
  REFRESH EVERY — re-evaluate the query on this schedule during an interactive session (e.g. '5m', '1h')
  TTL           — time-to-live for the cached result; stale results trigger a refresh
  ENCRYPT       — store the cached result encrypted (MACHINE = OS key, PASSWORD = passphrase, KEYFILE = key file)

```sql
-- Sales dataset refreshed every hour
CREATE DATASET &sales_summary REFRESH EVERY '1h' AS (
  SELECT
    region,
    product_category,
    SUM(amount)        AS total_revenue,
    COUNT(DISTINCT id) AS order_count
  FROM dbo.Orders
  WHERE order_date >= DATEADD(MONTH, -1, GETDATE())
  GROUP BY region, product_category
);

-- Reference the same dataset in multiple visuals
CREATE VISUAL RevBar AS BAR (
  SOURCE   = &sales_summary,
  MAPPINGS (X = region, Y = total_revenue)
);

CREATE VISUAL CatPie AS PIE (
  SOURCE   = &sales_summary,
  MAPPINGS (LABEL = product_category, VALUE = total_revenue)
);
```

`&name` is the report-dataset form. Keep intermediate preparation in ordinary `#temp` tables, then expose reusable report data through `CREATE DATASET &dataset` definitions. `USE DATASET` and `REFRESH DATASET` also require the `&dataset` name.

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
