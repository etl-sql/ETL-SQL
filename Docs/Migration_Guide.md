# ETL-SQL Migration Guide (v0.10.0)

ETL-SQL v0.10.0 is the current release baseline. Because the app has not had a public stable release before this baseline, this guide is mainly for repository maintainers, early testers, and anyone updating pre-release scripts from older branches.

For current syntax, prefer [User_Manual.md](User_Manual.md), [Reference/Grammar.md](Reference/Grammar.md), [Reference/Data_Connectors.md](Reference/Data_Connectors.md), and [Report_SQL_Guide.md](Report_SQL_Guide.md).

---

## Upgrade Checklist

1. Run the script through the current linter.
2. Replace old report layout experiments with the current `CREATE PAGE`, `CREATE CONTAINER`, `CREATE BUTTON`, and `CREATE VISUAL` forms.
3. Replace page, container, or visual visibility options with `VISIBLE = ON|OFF`.
4. Confirm portal deployments have a `Portal:Jwt:Secret` value of at least 32 characters.
5. Confirm portal-to-orchestrator URLs use the current service ports: Portal `5000`/`5002`, Orchestrator `5001`/`5003`.
6. Run the relevant smoke lane from [Testing.md](Testing.md).

---

## v0.7.0 Baseline Notes

### Report Object Visibility

Report pages, containers, visuals, and buttons use `VISIBLE = ON|OFF` for initial visibility.

```sql
CREATE PAGE DetailPage AS DASHBOARD (
    TITLE = 'Detail',
    VISIBLE = OFF,
    STRUCTURE = 'A',
    MAP ('A' = DetailTable)
);
```

Use `VISIBLE = OFF` when an object should be hidden from the initial navigation or delayed until a user action makes it relevant.

### Report Buttons

Buttons use the page-style form:

```sql
CREATE BUTTON OpenDetail AS (
    LABEL = 'Open Detail',
    ACTIONS (ON_CLICK = NAVIGATE_TO_PAGE(DetailPage))
);
```

Do not use typed button aliases or older layout-specific button forms in new scripts.

### Portal JWT Secret Enforcement

The Report Portal refuses to start if `Portal:Jwt:Secret` is missing or shorter than 32 characters.

Generate and install a portal secret with the CLI:

```bash
ETL-SQL config setup-jwt --update
```

Or generate a secret inside an ETL-SQL session and copy the value into your deployment secret store:

```sql
GENERATE JWT_SECRET;
```

### `FOR` Loop Implicit Start

`FOR` loops can omit the explicit start value. The implicit start is `1`.

```sql
FOR @i TO 10
BEGIN
    PRINT 'Iteration ' + CAST(@i AS STRING);
END
```

The explicit form remains valid:

```sql
FOR @i = 1 TO 10
BEGIN
    PRINT 'Iteration ' + CAST(@i AS STRING);
END
```

### `GO` Batch Separator

`GO` separates script batches. This is useful when a script needs one batch to define objects or state before a later batch consumes them.

```sql
CREATE CONNECTION src AS MOCKDB();
GO

SELECT * FROM src.Users;
```

### `QUALIFY`

Use `QUALIFY` to filter on window function results without wrapping the query in a CTE.

```sql
SELECT
    DeptID,
    Name,
    Salary,
    RANK() OVER (PARTITION BY DeptID ORDER BY Salary DESC) AS rnk
FROM Employee
QUALIFY rnk <= 2;
```

---

## Validation

Use the fast smoke lanes for local upgrade checks:

```powershell
.\scripts\test-smoke.ps1 -Lane all
```

The SQL Logic Test corpus is deployment-only and intentionally excluded from normal local runs. See [Testing.md](Testing.md) before running it.
