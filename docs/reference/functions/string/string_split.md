# STRING_SPLIT

Splits a string by a delimiter and returns the segments as a table.

## Syntax

```sql
STRING_SPLIT(string, delimiter)
```

## Parameters

- **string** - Source string to split.
- **delimiter** - Separator string.

## Returns

Returns a table with a single `value` column of type `STRING`, one row per segment. Empty segments between consecutive delimiters are included.

## Null Behavior

Returns no rows when `string` is `NULL`. Returns `NULL` segment values only when they are present in the source data model.

## Remarks

- Used with `CROSS APPLY` or as a subquery source.
- To filter empty segments: `WHERE value <> ''`.

## Examples

```sql
SELECT value AS tag
FROM STRING_SPLIT('red,green,blue', ',');
```

```sql
SELECT o.order_id, t.value AS tag
FROM #orders AS o
CROSS APPLY STRING_SPLIT(o.tags, ',') AS t;
```

## References

- [Standard Library](../standard-library.md)
- [SPLIT_PART](split_part.md)
- [REGEXP_SPLIT_TO_TABLE](../general/regexp_split_to_table.md)
