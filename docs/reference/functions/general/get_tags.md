# GET_TAGS

Returns metadata tag names defined on a table or column.

## Syntax

```sql
SELECT * FROM GET_TAGS(table_name [, column_name])
```

## Parameters

- **table_name** - Table or dataset name to inspect.
- **column_name** - Optional column name.

## Returns

Returns a table of tag names.

## Null Behavior

Returns no rows when no tags match the requested table or column.

## Examples

```sql
SELECT *
FROM GET_TAGS('Customers', 'SSN');
```

```sql
SELECT *
FROM GET_TAGS('Orders');
```

## References

- [Lineage](../../statements/session-control/lineage.md)
- [GET_TAG_VALUE](get_tag_value.md)
- [HAS_TAG](has_tag.md)
