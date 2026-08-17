# VALIDATE
Checks a script or published bundle for parser errors and lint diagnostics without executing it. Two forms are available: `VALIDATE REPORT SCRIPT` (via a Portal connection) and `VALIDATE BUNDLE` (standalone bundle pre-flight).

## Syntax

```sql
-- Form 1: Validate a report script via the Portal
EXECUTE portal_conn BEGIN
  VALIDATE REPORT SCRIPT '<path>' INTO #validation;
END;

-- Form 2: Validate a packaged bundle before publish
VALIDATE BUNDLE '<bundle_name>'
  FROM '<bundle_directory>'
  ENTRY '<entry_script.etlsql>';
```

## VALIDATE REPORT SCRIPT

Sends the `.rptsql` file to the connected Portal for parsing and lint analysis. The Portal evaluates the script path on the **portal host**, not the client machine.

```sql
EXECUTE prod_portal BEGIN
  VALIDATE REPORT SCRIPT 'C:\Reports\Prod\monthly_sales.rptsql' INTO #results;
END;

SELECT severity, rule, message, line_number
FROM #results
WHERE severity = 'ERROR'
ORDER BY line_number;
```

### Result schema (`INTO #table`)

| Column | Type | Description |
| :--- | :--- | :--- |
| `severity` | VARCHAR | `ERROR`, `WARNING`, or `INFO`. |
| `rule` | VARCHAR | Lint rule name that triggered the finding. |
| `message` | VARCHAR | Human-readable description of the finding. |
| `line_number` | INT | Line in the script where the finding originates. Null for file-level findings. |
| `column_number` | INT | Column offset within the line. Null when not applicable. |

## VALIDATE BUNDLE

Performs a client-side parse and lint pass on a packaged bundle directory. Use this before `PUBLISH BUNDLE` to catch errors early without touching the Portal.

```sql
VALIDATE BUNDLE 'finance-load'
  FROM 'C:\Jobs\finance-load'
  ENTRY 'main.etlsql';
```

Results are printed to the session output. Use `PUBLISH BUNDLE` only after `VALIDATE BUNDLE` reports no errors.

## Notes

- `VALIDATE REPORT SCRIPT` requires an active `PORTAL` connection with at least read access.
- The path in `VALIDATE REPORT SCRIPT` is resolved on the portal host filesystem, not the client. Use UNC or portal-relative paths for shared deployments.
- `VALIDATE BUNDLE` does not require a Portal connection — it runs entirely in the engine.
- Both forms run the full lint rule set, including dialect checks, security guardrails, and governance policy validation.
- See `LINT` for in-session script linting without a Portal connection.

## References

- [Orchestrator Jobs](README.md)
- [PUBLISH BUNDLE](publish.md)
- [LINT](../statements/session-control/lint.md)
