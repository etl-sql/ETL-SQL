# Troubleshooting: Report-SQL and Dashboard Issues

This guide addresses common pitfalls, errors, and rendering issues encountered when building dashboards with Report-SQL (`.rptsql`).

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. "Failed to cast" Error with RELDATE Parameters

### Problem
A query using a `RELDATE` parameter fails with an error: `Failed to cast 'D-7' to DATE`.

### Cause
Writing `CAST(@startDate AS DATE)` or `CONVERT(DATE, @startDate)` in SQL queries. `RELDATE` variables contain relative expressions (e.g. `'D-7'`, `'M-1'`), which cannot be parsed as literal ISO dates by `CAST()`.

### Solution
Do not cast the variable. Use it directly in comparison operators:

```sql
-- ❌ INCORRECT:
SELECT * FROM #sales WHERE SaleDate >= CAST(@startDate AS DATE);

-- ✓ CORRECT:
SELECT * FROM #sales WHERE SaleDate >= @startDate;
```
The engine resolves relative date expressions dynamically at comparison time.

---

## 2. The "Tier 2 Trap" (Slicer Does Not Update Data)

### Problem
Moving a Slicer filter in the Web Portal or Report Player does not change the data displayed in dashboard charts.

### Cause
The `@parameter` was placed inside a `SELECT ... INTO #tempTable` statement. `#temp` tables are evaluated **once** during initial report build time and are not re-executed when slicers move.

### Solution
Stage unfiltered data in `#temp` tables (Tier 2), and apply the `@parameter` filter inside the `SOURCE = (SELECT ...)` clause of the visual (Tier 3):

```sql
-- Tier 2 (Base Table: all regions)
SELECT Region, Category, Sales INTO #sales_base FROM db.Sales;

-- Tier 3 (Visual Query: filtered by slicer)
CREATE VISUAL SalesChart AS BAR (
  SOURCE = (SELECT Category, SUM(Sales) AS Total 
            FROM #sales_base 
            WHERE @region = 'All' OR Region = @region 
            GROUP BY Category),
  MAPPINGS (X = Category, Y = Total)
);
```

---

## 3. Slicer vs. DatePicker Action Binding Mismatches

### Problem
- Slicer fails with: `Invalid action parameter binding: expected column identifier`.
- DatePicker fails with: `Unknown column in action binding`.

### Solution
- **Visuals WITH a `SOURCE`** (`SLICER`, `MULTISELECT`): Bind to the **mapped column name**:
  ```sql
  ACTIONS (ON_CHANGE = SET_PARAMETER(@dept, DepartmentName))
  ```
- **Visuals WITHOUT a `SOURCE`** (`DATEPICKER`, `RELDATEPICKER`, `SLIDER`): Bind to the keyword **`value`**:
  ```sql
  ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value))
  ```

---

## 4. `LayerOrder` Linter Error

### Problem
Linter reports: `Referenced page in navigation has not been declared`.

### Cause
`CREATE NAVIGATION` was defined before `CREATE PAGE` statements.

### Solution
Place all `CREATE PAGE` statements before `CREATE NAVIGATION` at the bottom of the script.

---

## Related Topics

- [Authoring Dashboards](../reporting/authoring-dashboards.md) — 3-tier architecture.
- [Report Parameters and Filters](../reporting/report-parameters-and-filters.md) — Parameter bindings.
- [Cascading Slicers](../reporting/cascading-slicers.md) — Dependent filter hierarchies.
