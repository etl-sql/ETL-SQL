# EXPECT SCHEMA
Validates that a table or result set has the expected columns and types, halting with a descriptive error if columns are missing or mistyped.

## Syntax
```sql
EXPECT SCHEMA <source> (
  <column_name> <type> [NOT NULL],
  ...
) [ON DRIFT WARN];

EXPECT SCHEMA <source> FROM '<spec_path>' [ON DRIFT WARN];
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

-- Validate using a JSON specification contract file
EXPECT SCHEMA #orders FROM 'TestData/Specs/customer_spec.json';

-- Warn on drift instead of throwing an error
EXPECT SCHEMA #orders FROM 'TestData/Specs/customer_spec.json' ON DRIFT WARN;

-- Validate a remote connector table (schema check only — no data is loaded)
EXPECT SCHEMA MyConn.dbo.Products (
  product_id INT     NOT NULL,
  sku        VARCHAR NOT NULL,
  price      DECIMAL
);
```

## Notes
- Column matching is by name — column order in the actual table does not matter.
- `NOT NULL` is enforced when specified: the check fails if the column is nullable in the actual schema.
- Extra columns present in the actual table are allowed — the check is additive, not exhaustive.
- When the check fails, the error message names each missing column and each type mismatch.
- When `ON DRIFT WARN` is specified, drift issues are logged as warnings instead of halting execution with an error.
- The JSON specification file must contain a top-level `"schema"` array of objects:
  - `- **column_name**` — (string, required) Name of the column.
  - `- **type_family**` — (string, required) Expected data type family (e.g. `VARCHAR`, `INT`, `DECIMAL`).
  - `- **nullable**` — (boolean, optional) Set to `true` to allow null values, or `false` (default) to enforce NOT NULL.
  - `- **max_length**` — (number, optional) Maximum character length for string fields.
  - `- **precision**` — (number, optional) Precision for numeric fields.
  - `- **scale**` — (number, optional) Scale for numeric fields.
- Use EXPECT SCHEMA before processing external data to catch schema drift early and fail fast with a clear diagnostic.
- See: ASSERT, VALIDATE, CREATE

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
