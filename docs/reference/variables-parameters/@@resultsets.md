# @@RESULTSETS
Count of distinct result sets returned by the most recently executed statement or stored procedure call. A single `SELECT` returns 1; an `EXECUTE` block that runs multiple `SELECT` statements returns one per `SELECT`.

Set by: SELECT, EXECUTE (when the target returns multiple result sets).
Scope: updated after each statement — read it immediately after the statement you are inspecting.

```sql
-- Check how many result sets a procedure returned
EXECUTE src BEGIN
  EXEC dbo.usp_GetReports;
END;
PRINT 'Result sets returned: ' + @@RESULTSETS;

-- Branch on multi-result-set output
IF @@RESULTSETS > 1
BEGIN
  PRINT 'Warning: unexpected multiple result sets — only the first will be used.';
END;
```

References:
- [Variables and Parameters](README.md)
- [@@ROWCOUNT](@@rowcount.md)
