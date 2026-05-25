# EXPECT SCHEMA
Validates that a table or result set has the expected columns and types, halting with a descriptive error if columns are missing or mistyped.

## Syntax
```sql
EXPECT SCHEMA <source> (
  <column_name> <type> [NOT NULL],
  ...
);
```

## Examples
```sql
-- Validate a temp table before processing
SELECT * FROM ExternalFeed.dbo.RawOrders INTO #orders;

EXPECT SCHEMA #orders (
  order_id   INT         NOT NULL,
  customer   VARCHAR,
  amount     DECIMAL,
  order_date DATE        NOT NULL
);

-- Validate a remote connector table (schema check only — no data is loaded)
EXPECT SCHEMA MyConn.dbo.Products (
  product_id INT     NOT NULL,
  sku        VARCHAR NOT NULL,
  price      DECIMAL
);

-- Validate after a transform to catch logic errors early
SELECT order_id, region, SUM(amount) AS total INTO #summary FROM #orders GROUP BY order_id, region;

EXPECT SCHEMA #summary (
  order_id INT     NOT NULL,
  region   VARCHAR NOT NULL,
  total    DECIMAL NOT NULL
);
```

## Notes
- Column matching is by name — column order in the actual table does not matter.
- `NOT NULL` is enforced when specified: the check fails if the column is nullable in the actual schema.
- Extra columns present in the actual table are allowed — the check is additive, not exhaustive.
- When the check fails, the error message names each missing column and each type mismatch.
- Use EXPECT SCHEMA before processing external data to catch schema drift early and fail fast with a clear diagnostic.
- See: ASSERT, VALIDATE, CREATE

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
