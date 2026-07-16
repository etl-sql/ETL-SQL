# SORT_LIST

Returns a sorted copy of a list.

## Syntax

```sql
SORT_LIST(list [, direction])
```

## Parameters

- **list** - List value to sort.
- **direction** - Optional sort direction: `'ASC'` or `'DESC'`. Default is `'ASC'`.

## Returns

Returns a list containing the same values in sorted order.

## Null Behavior

Returns `NULL` when `list` is `NULL`.

## Remarks

- `SORT_LIST` does not mutate the original list unless you assign the result back to the same variable.
- Sorting uses ETL-SQL comparison rules for the list item types.

## Examples

```sql
DECLARE @codes = ['b', 'a', 'c'];
SET @codes = SORT_LIST(@codes);
```

```sql
DECLARE @scores = [10, 30, 20];
SET @scores = SORT_LIST(@scores, 'DESC');
```

## References

- [Standard Library](../standard-library.md)
- [ADD_TO_LIST](../collections/add_to_list.md)
- [REMOVE_FROM_LIST](remove_from_list.md)
