# VISUAL_TYPE

One-sentence description of when to use this visual.

## Syntax

```sql
CREATE VISUAL VisualName AS VISUAL_TYPE
SOURCE (
  SELECT ...
)
MAPPINGS (
  ...
);
```

## Mappings

- **role** - Expected column type and purpose.

## Options

- **OPTION** - Description, default, and valid values.

## Actions

Describe supported interactions and parameter bindings.

## Example

```sql
CREATE VISUAL Example AS VISUAL_TYPE
SOURCE (
  SELECT category, amount
  FROM #stage
)
MAPPINGS (
  ...
);
```

## References

- [Report SQL Guide](../guides/feature-guides/report-sql.md)

