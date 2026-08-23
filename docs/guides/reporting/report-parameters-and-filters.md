# Report Parameters and Interactive Filters

Report-SQL connects dashboard visuals to user inputs via `INPUT` variables and interactive filter controls. When a user changes a filter in the web portal or report player, the engine updates the bound variable and re-evaluates all dependent visuals in real time.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Declaring Parameters

Declare parameters at the top of your `.rptsql` script using `DECLARE` with the `INPUT` keyword. Any variable marked `INPUT` can be supplied by the Portal at run time, overridden via subscriptions, or set interactively by filter controls.

```sql
DECLARE @region    VARCHAR INPUT = 'All';
DECLARE @min_sales DECIMAL INPUT = 1000.00;
DECLARE @as_of     DATE    INPUT = '2026-06-01';
DECLARE @range     RELDATE INPUT = 'M-1';
```

### Parameter Types & Filter Controls

| Parameter Type | Recommended Filter Visual | Binding Pattern |
| :--- | :--- | :--- |
| `VARCHAR` / `STRING` | `SLICER`, `MULTISELECT`, `SEARCH`, `TEXTBOX` | `SET_PARAMETER(@var, ColumnName)` or `SET_PARAMETER(@var, value)` |
| `INT` / `DECIMAL` | `SLIDER`, `NUMBERBOX` | `SET_PARAMETER(@var, value)` |
| `DATE` | `DATEPICKER` | `SET_PARAMETER(@var, value)` |
| `RELDATE` | `RELDATEPICKER` | `SET_PARAMETER(@var, value)` |
| `BIT` / `BOOL` | `CHECKBOX` | `SET_PARAMETER(@var, value)` |

> [!IMPORTANT]
> **The Binding Rule**:
> - If a filter visual has a `SOURCE` clause (e.g. `SLICER`, `MULTISELECT`), bind the parameter to the **mapped column name**: `ACTIONS (ON_CHANGE = SET_PARAMETER(@region, region))`
> - If a filter visual has NO `SOURCE` clause (e.g. `DATEPICKER`, `RELDATEPICKER`, `SLIDER`), you **must** bind it to the literal keyword `value`: `ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value))`

---

## Example 1: Categorical Slicer and Metric Slider

This example creates a dropdown slicer for customer segments and a numeric range slider for minimum order values.

```sql
SET REPORT TITLE = 'Customer Revenue Explorer';

DECLARE @segment  VARCHAR INPUT = 'All';
DECLARE @minSpend INT     INPUT = 50;

CREATE CONNECTION db AS MOCKDB();

SELECT CustomerId, Segment, Spend
INTO #customers
FROM db.Customers;

-- Slicer with SOURCE (binds to column 'Segment')
CREATE VISUAL SegmentFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT Segment FROM #customers ORDER BY Segment),
  MAPPINGS (VALUE = Segment),
  DEFAULT  = 'All',
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@segment, Segment))
);

-- Slider without SOURCE (binds to keyword 'value')
CREATE VISUAL SpendSlider AS SLIDER (
  OPTIONS  (MIN = 0, MAX = 500, STEP = 25),
  DEFAULT  = 50,
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@minSpend, value))
);

-- Dependent KPI Card
CREATE VISUAL CustomerCount AS CARD (
  SOURCE = (SELECT COUNT(*) AS Total, 'Active Customers' AS Label
            FROM #customers
            WHERE (@segment = 'All' OR Segment = @segment)
              AND Spend >= @minSpend),
  MAPPINGS (VALUE = Total, LABEL = Label)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A B / C C',
    MAP (
      'A' = SegmentFilter,
      'B' = SpendSlider,
      'C' = CustomerCount
    )
  )
);
```

---

## Example 2: Dynamic Relative Date Filtering (`RELDATE`)

`RELDATE` parameters store expressions like `'D-7'` (last 7 days), `'M-1'` (last month), or `'Q-1'` (last quarter). The engine resolves the expression to an absolute date whenever the report or subscription runs.

```sql
SET REPORT TITLE = 'Sales Trend by Relative Date';

-- Declare relative date boundaries
DECLARE @startDate RELDATE INPUT = 'M-1';
DECLARE @endDate   RELDATE INPUT = 'D';

CREATE CONNECTION db AS MOCKDB();

SELECT OrderDate, Amount
INTO #sales
FROM db.Orders;

-- Relative Date Picker control
CREATE VISUAL DateFilter AS RELDATEPICKER (
  DEFAULT = 'M-1',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value))
);

-- Dependent Visual: use variables directly without CAST
CREATE VISUAL TrendChart AS LINE (
  SOURCE = (SELECT OrderDate, SUM(Amount) AS Total
            FROM #sales
            WHERE OrderDate >= @startDate AND OrderDate <= @endDate
            GROUP BY OrderDate),
  TITLE    = 'Revenue Over Time',
  MAPPINGS (X = OrderDate, Y = Total)
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A / B',
    MAP (
      'A' = DateFilter,
      'B' = TrendChart
    )
  )
);
```

> [!CAUTION]
> **Never CAST a RELDATE Variable in SQL**: Do **not** write `WHERE OrderDate >= CAST(@startDate AS DATE)`. `CAST('M-1' AS DATE)` fails because `'M-1'` is not an ISO date string. Use the variable directly: `WHERE OrderDate >= @startDate`. The engine automatically resolves relative expressions during comparison.

---

## Example 3: Multi-Select Filter with List Variables

Filter data using a multi-select control that passes selected values into an engine query.

```sql
SET REPORT TITLE = 'Department Resource Dashboard';

DECLARE @departments LIST INPUT = ('Finance', 'Engineering');

CREATE CONNECTION db AS MOCKDB();

SELECT EmployeeName, Department, Salary
INTO #staff
FROM db.Employees;

CREATE VISUAL DeptFilter AS MULTISELECT (
  SOURCE   = (SELECT DISTINCT Department FROM #staff ORDER BY Department),
  MAPPINGS (VALUE = Department),
  OPTIONS  (DEFAULT = 'Finance'),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@departments, value))
);

CREATE VISUAL StaffTable AS TABLE (
  SOURCE = (SELECT EmployeeName, Department, Salary
            FROM #staff
            WHERE Department IN @departments),
  SUMMARY (GRAND_TOTAL = ON, SUM(Salary) AS 'Total Payroll')
);

CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A / B',
    MAP (
      'A' = DeptFilter,
      'B' = StaffTable
    )
  )
);
```

---

## Common Pitfalls

- **Using `value` on a Slicer with SOURCE**: Writing `ACTIONS (ON_CHANGE = SET_PARAMETER(@cat, value))` on a `SLICER` causes a lint error; reference the mapped column name (e.g. `Category`) instead.
- **Using column names on a DATEPICKER**: Writing `ACTIONS (ON_CHANGE = SET_PARAMETER(@date, order_date))` on a `DATEPICKER` fails because the control has no source table; use the `value` keyword.
- **Handling 'All' conditions**: When creating single-select slicers with a default `'All'` option, always handle both conditions in your visual queries: `WHERE @region = 'All' OR Region = @region`.

---

## Related Topics

- [Cascading Slicers](cascading-slicers.md) — Hierarchical filters where child options depend on parent selections.
- [Authoring Dashboards](authoring-dashboards.md) — Three-tier architecture and page layout.
- [Relative Date Reference](../../reference/functions/datetime/reldate.md) — Complete `RELDATE` syntax options (`D`, `W`, `M`, `Q`, `Y`).
