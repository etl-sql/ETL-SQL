# PIVOT / UNPIVOT

Rotates rows into columns (`PIVOT`) for cross-tab analytical matrix reporting, or transposes columns into rows (`UNPIVOT`) to normalize wide datasets into tall relational structures. Supports both SQL-standard query clauses and dynamic statement forms.

---

## SQL-Standard Clause Syntax

### PIVOT
## Syntax

```sql
SELECT <group_columns...>, <pivoted_columns...>
FROM <source_table>
PIVOT (
  <aggregate_function>(<value_column>)
  FOR <pivot_column> IN (<value_list...>)
) AS <alias>;
```

### UNPIVOT
```sql
SELECT <group_columns...>, <value_column>, <name_column>
FROM <source_table>
UNPIVOT (
  <value_column> FOR <name_column> IN (<column_list...>)
) AS <alias>;
```

---

## Dynamic Statement Syntax (DuckDB Dialect)

ETL-SQL also supports concise standalone statements with automatic dynamic discovery of distinct values:

```sql
-- Dynamic: Automatically pivots all distinct quarters into columns without a hardcoded list
PIVOT #sales ON quarter USING SUM(amount);

-- Explicit enumeration with explicit row grouping
PIVOT #sales ON quarter IN ('Q1', 'Q2', 'Q3', 'Q4') USING SUM(amount) GROUP BY region;

-- Multi-aggregate pivot (produces Q1_total, Q1_cnt, Q2_total, Q2_cnt...)
PIVOT #sales ON quarter USING SUM(amount) AS total, COUNT(*) AS cnt;

-- UNPIVOT dynamically excluding dimension columns
UNPIVOT #wide_metrics ON COLUMNS(* EXCLUDE (department, year)) INTO NAME metric_name VALUE metric_val;
```

---

## Examples

### 1. Quarterly Sales Matrix (Standard PIVOT)

Transform monthly transactional line items into a regional summary matrix:

```sql
SELECT Region, Jan, Feb, Mar, Apr
FROM (
    SELECT Region, Month, Revenue 
    FROM #sales
) AS src
PIVOT (
    SUM(Revenue)
    FOR Month IN ('Jan', 'Feb', 'Mar', 'Apr')
) AS pvt
ORDER BY Region;
```

### 2. Normalizing Wide Excel Imports (UNPIVOT)

Convert wide spreadsheet tables with multi-month column headers into normalized relational records:

```sql
-- #wide_budget schema: (Department VARCHAR, FY24_Q1 DECIMAL, FY24_Q2 DECIMAL, FY24_Q3 DECIMAL, FY24_Q4 DECIMAL)
SELECT Department, FiscalQuarter, BudgetAmount
INTO #normalized_budget
FROM #wide_budget
UNPIVOT (
    BudgetAmount FOR FiscalQuarter IN (FY24_Q1, FY24_Q2, FY24_Q3, FY24_Q4)
) AS unpvt;

-- Load normalized rows into warehouse
INSERT INTO dw.dbo.DepartmentBudgets (Department, Quarter, Amount)
SELECT Department, FiscalQuarter, BudgetAmount FROM #normalized_budget;
```

---

## Null Handling & Column Rules

- **Unmatched Cells**: In a `PIVOT` matrix, cell coordinates with no underlying source rows evaluate to `NULL`. Use `COALESCE(Jan, 0)` or `Jan ?? 0` if default zero-fill is desired.
- **Unpivot Filtering**: `UNPIVOT` excludes `NULL` values by default, producing only populated key-value pairs.
- **Quoting**: Values in static `PIVOT ... IN ('val1', 'val2')` must be quoted string literals.

---

## References & Related Recipes

- [Query Syntax Reference](README.md)
- [SELECT Statement](../dml/select.md)
- [GROUP BY ALL](group-by-all.md)
- [ETL Cookbook: Financial Reporting Pivot](../../../cookbooks/etl/financial-reporting-pivot.md)
- [Syntax Index](../../../syntax-index.md)
