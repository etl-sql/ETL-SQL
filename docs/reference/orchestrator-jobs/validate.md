VALIDATE checks a script or published object without running it.

Syntax:
```sql
EXECUTE portal BEGIN
  VALIDATE REPORT SCRIPT 'C:\Reports\finance.rptsql' INTO #validation;
END;

VALIDATE BUNDLE 'finance-load' FROM 'C:\Jobs\finance-load' ENTRY 'main.etlsql';
```

Notes:
- `VALIDATE REPORT SCRIPT` returns parser/lint diagnostics from the Portal.
- The script path is evaluated on the portal host, not the client machine.
- Use `INTO #table` when deployment scripts need to inspect validation rows.

References:
- [Orchestrator Jobs](README.md)
