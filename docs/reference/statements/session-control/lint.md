# LINT
Runs static analysis on an ETL-SQL script and reports rule violations without executing the script.

## Syntax
```sql
-- Lint an external script file
LINT 'path/to/script.etlsql';

-- Lint the current script (TUI and LSP use this automatically)
LINT this;
```

## Rule code prefixes
| Prefix | Category | Examples |
|---|---|---|
| `SEC-` | Security | Hardcoded passwords, unencrypted SENSITIVE vars |
| `PERF-` | Performance | Missing indexes on large join keys, unbounded SELECTs |
| `STY-` | Style | Inconsistent casing, unused variables, missing semicolons |
| `LOG-` | Lineage | Untraceable data flows, missing source annotations |

## Example output
```
SEC-1  Line 12: Plaintext password detected in CREATE CONNECTION. Use ENC: or a SENSITIVE variable.
PERF-3 Line 34: JOIN on #orders.CustomerId with no index. Consider CREATE INDEX.
STY-2  Line 8:  Variable @Temp declared but never used.
LOG-1  Line 55: Output table #results has no traceable source. Add a lineage comment or MERGE source.
```

## Notes
- Lint runs automatically in the TUI editor (side panel) and VS Code extension (via LSP diagnostics) as you type.
- `LINT 'file'` exits with a non-zero code when any SEC- or PERF- violations are found, making it suitable for CI gates.
- Rule severity can be configured per-rule in the `Lint` section of `appsettings.json`.
- See: ASSERT, TRY

References:
- [Grammar](../../../guides/getting-started.md)
