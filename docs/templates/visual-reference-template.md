# VISUAL_TYPE

> **Page-type: Reference — visual**
> Owns: mappings, options, actions, a copy-pasteable example, common failures, and FAQ for one
> visual type.
> Links to (does not restate): Report-SQL guide for multi-visual layout workflow.
> Required sections: Syntax, Mappings, Options, Actions, Example, Common Failures, FAQ,
> References.

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

- **role** — Expected column type and purpose.

## Options

- **OPTION** — Description, default, and valid values.

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

## Common Failures

- **Symptom** — Cause and fix.

## FAQ

**Q: …**
A: …

## References

- [Report SQL Guide](../guides/feature-guides/report-sql.md)
- [Visuals Reference](../reference/visuals-reporting/README.md)
