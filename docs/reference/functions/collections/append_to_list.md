# APPEND_TO_LIST

Returns a list with a value appended.

## Syntax

```sql
APPEND_TO_LIST(list, value)
```

## Parameters

- **list** - Existing list value.
- **value** - Value to append.

## Returns

Returns the updated list.

## Null Behavior

If `list` is `NULL`, ETL-SQL treats it as an empty list and returns a one-item list. If `value` is `NULL`, the returned list includes a `NULL` item.

## Remarks

- `APPEND_TO_LIST` does not mutate a variable unless you assign the returned value.
- [`ADD_TO_LIST`](add_to_list.md) is an alias.

## Examples

```sql
DECLARE @columns = [];
SET @columns = APPEND_TO_LIST(@columns, 'customer_id');
SET @columns = APPEND_TO_LIST(@columns, 'email');
```

## References

- [Functions](../README.md)
- [ADD_TO_LIST](add_to_list.md)
- [REMOVE_FROM_LIST](../collections/remove_from_list.md)
