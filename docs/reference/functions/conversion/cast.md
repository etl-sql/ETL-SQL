# CAST

Converts an expression to a target ETL-SQL data type and raises an error when the value cannot be converted.

## Syntax

```sql
CAST(expression AS type)
```

## Parameters

- **expression** - The value or expression to convert.
- **type** - The target ETL-SQL data type, such as `INT`, `BIGINT`, `DECIMAL(p,s)`, `VARCHAR(n)`, `DATE`, `DATETIME`, `BOOLEAN`, or `VARBINARY`.

## Returns

Returns the converted value using the requested target type.

## Null Behavior

`CAST(NULL AS type)` returns `NULL` with the requested type. Non-null values that cannot be converted raise an execution error.

## Remarks

- Use `CAST` when invalid data should stop the script.
- Use [`TRY_CAST`](try_cast.md) when dirty source data should produce `NULL` instead of an error.
- In engine-context queries, `CAST` uses ETL-SQL conversion behavior. Inside `EXECUTE connection BEGIN ... END`, use the target database's native conversion syntax.

## Examples

```sql
SELECT CAST('1' AS INT) AS customer_id;

SELECT CAST('2026-07-16' AS DATE) AS business_date;

SELECT amount_text, CAST(amount_text AS DECIMAL(18, 2)) AS amount
FROM #staging
WHERE amount_text IS NOT NULL;
```

## References

- [Standard Library](../standard-library.md)
- [TRY_CAST](try_cast.md)
- [CONVERT](convert.md)
