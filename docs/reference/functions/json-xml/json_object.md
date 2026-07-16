# JSON_OBJECT

Constructs a JSON object string from a list of key-value pairs.

## Syntax

```sql
JSON_OBJECT(key1, value1, key2, value2, ...)
```

## Parameters

- **key** - Object property name.
- **value** - Object property value.

## Returns

Returns a `STRING` containing a JSON object.

## Null Behavior

`NULL` values are emitted as JSON `null` values. `NULL` keys are invalid.

## Remarks

- Arguments are provided as alternating key and value expressions.
- Use [`JSON_ARRAY`](json_array.md) for array construction.

## Examples

```sql
SELECT JSON_OBJECT('name', 'Alice', 'active', TRUE) AS payload;
```

```sql
SELECT JSON_OBJECT('id', customer_id, 'email', email, 'status', status) AS customer_json
FROM #customers;
```

## References

- [Standard Library](../standard-library.md)
- [JSON_ARRAY](json_array.md)
- [JSON_VALUE](json_value.md)
