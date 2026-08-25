# HELP
Displays documentation for a keyword, function, connector, or option directly in the REPL or output pane.

## Syntax
```sql
HELP <topic>;

```

## Examples
```sql
-- Get help on a statement keyword
HELP SELECT;
HELP MERGE;
HELP EXPLAIN;

-- Get help on a built-in function
HELP DATEADD;
HELP COALESCE;

-- Get help on a connector type
HELP SNOWFLAKE;
HELP POSTGRES;

-- Get help on a configuration option
HELP BATCHSIZE;

-- List all available code snippets
HELP SNIPPETS;

```

## Notes
- `HELP SNIPPETS` lists all available code snippets with their trigger keyword and description.
- In the VS Code extension, HELP output appears in the results panel.
- In the TUI, HELP output appears in an inline scroll pane that can be navigated without leaving the editor.
- Partial matches are supported: `HELP DATE` returns help for all DATE-prefixed functions.
- See: LINT, SHOW

References:
- [Statements](../README.md)


## References

- [Statements](../README.md)
