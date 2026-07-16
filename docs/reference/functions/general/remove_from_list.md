# REMOVE_FROM_LIST

Returns a list with matching values removed.

## Syntax

```sql
REMOVE_FROM_LIST(list, value)
```

## Parameters

- **list** - Existing list value.
- **value** - Value to remove.

## Returns

Returns a list with all matching values removed.

## Null Behavior

Returns `NULL` when `list` is `NULL`.

## Remarks

- Assign the returned value back to keep the change.
- Matching uses ETL-SQL equality semantics.
- Use [`ADD_TO_LIST`](../collections/add_to_list.md) or [`APPEND_TO_LIST`](../collections/append_to_list.md) to add values.

## Examples

```sql
DECLARE @tags = ['finance', 'draft', 'reviewed'];
SET @tags = REMOVE_FROM_LIST(@tags, 'draft');
```

## References

- [Standard Library](../standard-library.md)
- [ADD_TO_LIST](../collections/add_to_list.md)
- [SORT_LIST](sort_list.md)
