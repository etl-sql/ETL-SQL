# ADD_TO_LIST

Returns a list with a value appended. `ADD_TO_LIST` is an alias for [`APPEND_TO_LIST`](append_to_list.md).

## Syntax

```sql
ADD_TO_LIST(list, value)
```

## Parameters

- **list** - Existing list value.
- **value** - Value to append.

## Returns

Returns the updated list.

## Null Behavior

If `list` is `NULL`, ETL-SQL treats it as an empty list and returns a one-item list. If `value` is `NULL`, the returned list includes a `NULL` item.

## Remarks

- Assign the returned value back to keep the change.
- Use [`REMOVE_FROM_LIST`](../collections/remove_from_list.md) to remove matching values.
- Use [`SORT_LIST`](../collections/sort_list.md) to sort a list.

## Examples

```sql
DECLARE @tags = [];
SET @tags = ADD_TO_LIST(@tags, 'finance');
SET @tags = ADD_TO_LIST(@tags, 'reviewed');
```

## References

- [Standard Library](../standard-library.md)
- [APPEND_TO_LIST](append_to_list.md)
- [REMOVE_FROM_LIST](../collections/remove_from_list.md)
