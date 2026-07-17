# HAS_TAG

Checks if a metadata tag exists on a table or column, optionally validating its expected value.

## Syntax

```sql
HAS_TAG(table_name, column_name, tag_name [, expected_value])
```

## Parameters

- **table_name** - Table or dataset name to inspect.
- **column_name** - Column name to inspect.
- **tag_name** - Metadata tag name to check.
- **expected_value** - Optional value to compare with the tag's actual value.

## Returns

Returns `1` when the tag exists and matches `expected_value` when supplied; otherwise returns `0`.

## Null Behavior

Returns `0` when the requested tag does not exist.

## Examples

```sql
SELECT HAS_TAG('Customers', 'SSN', 'PII') AS is_pii;
```

```sql
SELECT HAS_TAG('Customers', 'SSN', 'PII_LEVEL', 'High') AS is_high_pii;
```

## References

- [Lineage](../../statements/session-control/lineage.md)
- [GET_TAGS](get_tags.md)
- [GET_TAG_VALUE](get_tag_value.md)
- [User Manual](../../../guides/getting-started.md)
