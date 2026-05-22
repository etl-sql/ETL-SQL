VALIDATE checks a script or published object without running it.

Syntax:
```sql
EXECUTE portal BEGIN
  VALIDATE REPORT SCRIPT 'C:\Reports\finance.rptsql' INTO #validation;
END;

VALIDATE BUNDLE FROM 'C:\Jobs\finance-load\main.etlsql';
```

Notes:
- `VALIDATE REPORT SCRIPT` returns parser/lint diagnostics from the Report Portal.
- The script path is evaluated on the portal host, not the client machine.
- Use `INTO #table` when deployment scripts need to inspect validation rows.
