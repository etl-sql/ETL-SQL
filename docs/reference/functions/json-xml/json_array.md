# JSON_ARRAY

Constructs a JSON array string from a list of values.

## Syntax

```sql
JSON_ARRAY(value1, value2, ...)
```

## Parameters

- **value1, value2, ...** - Values to include in the JSON array.

## Returns

Returns a `STRING` containing a JSON array.

## Null Behavior

`NULL` arguments are included as JSON `null` values.

## Examples

```sql
SELECT JSON_ARRAY(10, 'sales', TRUE) AS payload;
```

```sql
SELECT JSON_ARRAY(customer_id, email, status) AS customer_tuple
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [JSON_OBJECT](json_object.md)
- [JSON_VALUE](json_value.md)
