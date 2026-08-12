# CREATE BINDING

Declares logical binding metadata for governed tool execution. The current engine parses,
formats, audits, and validates this statement but does not persist a binding catalog or make the
binding available to `EXECUTE TOOL`; do not use it as an authorization or resource boundary.

## Syntax

```sql
CREATE BINDING binding_name AS binding_type (
  OPTION_NAME = value
);
```

## Options

- **binding_name** — Session statement identity for diagnostics.
- **binding_type** — Logical binding class. The current validation-only implementation does not restrict the value.
- **OPTION_NAME = value** — Declarative option captured in the AST. Options currently have no runtime effect.
- **CREATE OR ALTER / CREATE OR REPLACE** — Accepted by the parser, but currently only change the validation/audit message because no binding catalog is persisted.

## Example

```sql
SET WHAT_IF ON;

CREATE BINDING CustomerGateway AS GATEWAY (
  RESOURCE = 'crm-readonly'
);
```

## References

- [CREATE TOOL](create-tool.md)
- [Statement Reference](../README.md)
