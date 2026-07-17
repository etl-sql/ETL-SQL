# GET_TAG_VALUE

Retrieves the metadata tag value assigned to a specific table or column.

## Syntax

```sql
GET_TAG_VALUE(table_name, column_name, tag_name)
```

## Parameters

- **table_name** - Table or dataset name to inspect.
- **column_name** - Column name to inspect.
- **tag_name** - Metadata tag name to retrieve.

## Returns

Returns the tag value as a `STRING`.

## Null Behavior

Returns `NULL` when the requested tag does not exist.

## Examples

```sql
SELECT GET_TAG_VALUE('Customers', 'SSN', 'PII_LEVEL') AS pii_level;
```

```sql
SELECT GET_TAG_VALUE('Orders', 'OrderTotal', 'CLASSIFICATION') AS classification;
```

## References

- [Lineage](../../statements/session-control/lineage.md)
- [GET_TAGS](get_tags.md)
- [HAS_TAG](has_tag.md)
- [User Manual](../../../guides/getting-started.md)
